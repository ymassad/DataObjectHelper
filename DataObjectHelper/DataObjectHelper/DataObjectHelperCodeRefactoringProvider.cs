using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DataObjectHelper
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(DataObjectHelperCodeRefactoringProvider)), Shared]
    internal class DataObjectHelperCodeRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var node = root.FindNode(context.Span);

            var attributeSyntax = node.TryCast().To<AttributeSyntax>()
                .ValueOrMaybe(() => node.Parent.TryCast().To<AttributeSyntax>());

            var attributeNameSyntax = attributeSyntax
                .ChainValue(x => x.Name.TryCast().To<IdentifierNameSyntax>());

            if (attributeNameSyntax.HasValueAnd(x => x.Identifier.Text.EqualsAny("CreateMatchMethods", "CreateMatchMethodsAttribute")))
            {
                var action = CodeAction.Create(
                    "Create Match methods",
                    c => CreateMatchMethods(context.Document, attributeSyntax.GetValue(), root, c));

                context.RegisterRefactoring(action);

            }

            if (attributeNameSyntax.HasValueAnd(x => x.Identifier.Text.EqualsAny("CreateWithMethods", "CreateWithMethodsAttribute")))
            {
                var action = CodeAction.Create(
                    "Create With methods",
                    c => CreateWithMethods(context.Document, attributeSyntax.GetValue(), root, c));

                context.RegisterRefactoring(action);
            }
        }

        public class PropertyAndParameter
        {
            public IParameterSymbol Parameter { get; }
            public IPropertySymbol Property { get; }

            public PropertyAndParameter(IParameterSymbol parameter, IPropertySymbol property)
            {
                Parameter = parameter;
                Property = property;
            }
        }

        private async Task<Solution> CreateWithMethods(Document document, AttributeSyntax attributeSyntax, SyntaxNode root, CancellationToken cancellationToken)
        {
            var originalSolution = document.Project.Solution;

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            var hostClass =
                ResolveStaticClassWhereToInsertMethods(
                    attributeSyntax);

            var doTypeSymbol =
                GetDataObjectType(attributeSyntax)
                    .ChainValue(d =>
                        semanticModel.GetSymbolInfo(d).Symbol
                        .TryCast().To<INamedTypeSymbol>()
                        .If(x => x.TypeKind == TypeKind.Class)
                        .If(x => !x.IsAbstract));

            var dataObjectProperties =
                doTypeSymbol
                    .ChainValue(x => x.GetMembers().OfType<IPropertySymbol>().ToArray());

            var doParamsAndProps =
                (doTypeSymbol, dataObjectProperties)
                    .ChainValues(GetConstructorParametersAndCorrespondingProperties);

            var hostClassSymbol = hostClass
                .ChainValue(x => semanticModel.GetDeclaredSymbol(x))
                .TryCast().To<INamedTypeSymbol>();

            var existingWithMethods =
                (hostClassSymbol, doTypeSymbol)
                    .ChainValues(GetExistingWithMethods);

            var methodsToAdd =
                (doParamsAndProps, doTypeSymbol, existingWithMethods)
                    .ChainValues(CreateWithMethodsToAdd);

            Solution AddMethodsToClass(
                ClassDeclarationSyntax classDeclarationSyntax,
                MethodDeclarationSyntax[] methodDeclarationSyntaxs)
            {
                return originalSolution.WithDocumentSyntaxRoot(document.Id,
                    root.ReplaceNode(
                        classDeclarationSyntax,
                        classDeclarationSyntax.AddMembers(methodDeclarationSyntaxs)));
            }

            var solutionToReturn =
                (hostClass, methodsToAdd)
                    .ChainValues(AddMethodsToClass);

            return solutionToReturn.ValueOr(originalSolution);
        }

        private static string[] GetExistingWithMethods(INamedTypeSymbol hostClass, INamedTypeSymbol dataObject)
        {
            return hostClass.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(x => x.Name.StartsWith("With"))
                .Where(x => x.Parameters.Length == 2 && x.Parameters[0].Type.Equals(dataObject))
                .Select(x => x.Name)
                .ToArray();
        }


        private MethodDeclarationSyntax[] CreateWithMethodsToAdd(
            PropertyAndParameter[] constructorParametersAndCorrespondingProperties,
            INamedTypeSymbol doType,
            string[] existingWithMethodNames)
        {
            return
                constructorParametersAndCorrespondingProperties
                    .Select(item => new { item, newMethodName = "With" + item.Property.Name })
                    .Where(x => !existingWithMethodNames.Contains(x.newMethodName))
                    .Select(x => CreateWithMethod(doType, constructorParametersAndCorrespondingProperties, x.item))
                    .ToArray();
        }

        private static Maybe<PropertyAndParameter[]> GetConstructorParametersAndCorrespondingProperties(
            INamedTypeSymbol symbol,
            IPropertySymbol[] properties)
        {
            return
                symbol.Constructors
                .FirstOrNoValue()
                .ChainValue(x =>
                    x.Parameters
                        .Select(param =>
                            GetMatchingProperty(properties, param)
                                .ChainValue(p => new PropertyAndParameter(param, p)))
                        .ToArray()
                        .Traverse());
        }

        private static Maybe<IPropertySymbol> GetMatchingProperty(
            IPropertySymbol[] properties,
            IParameterSymbol param)
        {
            return
                properties
                    .FirstOrNoValue(prop =>
                        prop.Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase)
                        && prop.Type.Equals(param.Type));
        }

        private static Maybe<TypeSyntax> GetDataObjectType(AttributeSyntax attributeSyntax)
        {
            return attributeSyntax.ArgumentList.Arguments.FirstOrNoValue()
                .ChainValue(x => x.Expression.TryCast().To<TypeOfExpressionSyntax>())
                .ChainValue(x => x.Type);
        }

        private static Maybe<ClassDeclarationSyntax> ResolveStaticClassWhereToInsertMethods(
            AttributeSyntax attributeSyntax)
        {
            return
                attributeSyntax.Ancestors()
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrNoValue()
                    .If(x => x.Modifiers.Any(IsStaticKeyword));
        }

        private static bool IsStaticKeyword(SyntaxToken m)
        {
            return m.RawKind == (int)SyntaxKind.StaticKeyword;
        }

        private MethodDeclarationSyntax CreateWithMethod(
            INamedTypeSymbol doTypeSymbol,
            PropertyAndParameter[] allProperties,
            PropertyAndParameter propertyToCreateMethodFor)
        {
            var newMethodName = "With" + propertyToCreateMethodFor.Property.Name;

            var doFullname = GetFullName(doTypeSymbol);

            var thisParameterName = MakeFirstLetterSmall(doTypeSymbol.Name);

            var propertyTypeFullName = GetFullName(propertyToCreateMethodFor.Property.Type);

            List<SyntaxNodeOrToken> nodes = new List<SyntaxNodeOrToken>();

            foreach (var property in allProperties)
            {
                var propertyName = property.Property.Name;

                var propertyNameCamelCase = MakeFirstLetterSmall(property.Property.Name);

                if (nodes.Count > 0)
                    nodes.Add(Token(SyntaxKind.CommaToken));

                if (property.Property.Equals(propertyToCreateMethodFor.Property))
                {
                    nodes.Add(
                        Argument(
                                IdentifierName("newValue"))
                            .WithNameColon(
                                NameColon(
                                    IdentifierName(propertyNameCamelCase))));
                }
                else
                {
                    nodes.Add(
                        Argument(
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName(thisParameterName),
                                    IdentifierName(propertyName)))
                            .WithNameColon(
                                NameColon(
                                    IdentifierName(propertyNameCamelCase))));
                }
            }


            return MethodDeclaration(
                    IdentifierName(doFullname),
                    Identifier(newMethodName))
                .WithModifiers(
                    TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
                .WithParameterList(
                    ParameterList(
                        SeparatedList<ParameterSyntax>(
                            new SyntaxNodeOrToken[]{
                                Parameter(
                                        Identifier(thisParameterName))
                                    .WithModifiers(
                                        TokenList(
                                            Token(SyntaxKind.ThisKeyword)))
                                    .WithType(
                                        IdentifierName(doFullname)),
                                Token(SyntaxKind.CommaToken),
                                Parameter(
                                        Identifier("newValue"))
                                    .WithType(
                                        IdentifierName(propertyTypeFullName))})))
                .WithBody(
                    Block(
                        SingletonList<StatementSyntax>(
                            ReturnStatement(
                                ObjectCreationExpression(
                                        IdentifierName(doFullname))
                                    .WithArgumentList(
                                        ArgumentList(
                                            SeparatedList<ArgumentSyntax>(
                                                nodes.ToArray())))))));
        }


        private async Task<Solution> CreateMatchMethods(
            Document document, AttributeSyntax attributeSyntax, SyntaxNode root, CancellationToken cancellationToken)
        {


            var originalSolution = document.Project.Solution;

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            var hostClass =
                ResolveStaticClassWhereToInsertMethods(
                    attributeSyntax);

            var doTypeSymbol =
                GetDataObjectType(attributeSyntax)
                    .ChainValue(d =>
                        semanticModel.GetSymbolInfo(d).Symbol
                            .TryCast().To<INamedTypeSymbol>()
                            .If(x => x.TypeKind == TypeKind.Class)
                            .If(x => x.IsAbstract));

            var hostClassSymbol = hostClass
                .ChainValue(x => semanticModel.GetDeclaredSymbol(x))
                .TryCast().To<INamedTypeSymbol>();

            var existingMethods =
                (hostClassSymbol, doTypeSymbol)
                    .ChainValues((host, doSym) =>
                        host.GetMembers("Match")
                        .OfType<IMethodSymbol>()
                        .Where(x => x.Parameters.Length >= 1 && x.Parameters[0].Type.Equals(doSym))
                        .ToArray());

            var derivedTypes =
                await doTypeSymbol.ChainValue(x =>
                     SymbolFinder.FindDerivedClassesAsync(x, originalSolution,
                        cancellationToken: cancellationToken));

            var nonAbstractDerivedTypes =
                derivedTypes
                    .ChainValue(types => types.Where(x => !x.IsAbstract).ToArray())
                    .If(x => x.Length > 0);

            var methodsToAdd = (doTypeSymbol, nonAbstractDerivedTypes, existingMethods)
                .ChainValues((ts, derived, existing) =>
                {
                    bool methodMethod1Exist = existing.Any(e => GetNamedParameterTypes(e).Any(IsFunc));

                    bool methodMethod2Exist = existing.Any(e => GetNamedParameterTypes(e).Any(IsAction));

                    List<MethodDeclarationSyntax> methods = new List<MethodDeclarationSyntax>();

                    if (!methodMethod1Exist)
                        methods.Add(CreateMatchMethod(ts, derived));

                    if (!methodMethod2Exist)
                        methods.Add(CreateMatchMethodThatReturnsVoid(ts, derived));

                    return methods.ToArray();
                });

            Solution AddMethodsToClass(
                ClassDeclarationSyntax classDeclarationSyntax,
                MethodDeclarationSyntax[] methodDeclarationSyntaxs)
            {
                return originalSolution.WithDocumentSyntaxRoot(document.Id,
                    root.ReplaceNode(
                        classDeclarationSyntax,
                        classDeclarationSyntax.AddMembers(methodDeclarationSyntaxs)));
            }

            var solutionToReturn =
                (hostClass, methodsToAdd)
                .ChainValues(AddMethodsToClass)
                .ValueOr(originalSolution);

            return solutionToReturn;
        }

        private bool IsAction(INamedTypeSymbol namedTypeSymbol)
        {
            return namedTypeSymbol.IsGenericType && namedTypeSymbol.ConstructUnboundGenericType().Name == "Action";
        }

        private bool IsFunc(INamedTypeSymbol type)
        {
            return type.IsGenericType && type.ConstructUnboundGenericType().Name == "Func";
        }

        private INamedTypeSymbol[] GetNamedParameterTypes(IMethodSymbol methodSymbol)
        {
            return methodSymbol.Parameters.Select(x => x.Type).ToArray().OfType<INamedTypeSymbol>().ToArray();
        }

        private MethodDeclarationSyntax CreateMatchMethod(
            INamedTypeSymbol typeSymbol,
            INamedTypeSymbol[] nonAbstractDerivedTypes)
        {
            var doClassName = typeSymbol.Name;

            var doClassFullname = GetFullName(typeSymbol);

            var classFullname = doClassFullname;

            var firstParameterName = MakeFirstLetterSmall(doClassName);

            List<SyntaxNodeOrToken> parametersAndCommas = new List<SyntaxNodeOrToken>();

            parametersAndCommas.Add(Parameter(
                    Identifier(firstParameterName))
                .WithModifiers(
                    TokenList(
                        Token(SyntaxKind.ThisKeyword)))
                .WithType(
                    IdentifierName(classFullname)));

            foreach (var subType in nonAbstractDerivedTypes)
            {
                var subTypeFullname = GetFullName(subType);

                var subTypeCamelCaseName = MakeFirstLetterSmall(subType.Name);

                var newParam = Parameter(
                        Identifier(subTypeCamelCaseName + "Case"))
                    .WithType(
                        GenericName(
                                Identifier("System.Func"))
                            .WithTypeArgumentList(
                                TypeArgumentList(
                                    SeparatedList<TypeSyntax>(
                                        new SyntaxNodeOrToken[]
                                        {
                                            IdentifierName(subTypeFullname),
                                            Token(SyntaxKind.CommaToken),
                                            IdentifierName("TResult")
                                        }))));

                parametersAndCommas.Add(Token(SyntaxKind.CommaToken));
                parametersAndCommas.Add(newParam);
            }

            var parameterListSyntax = ParameterList(
                SeparatedList<ParameterSyntax>(
                    parametersAndCommas.ToArray()));

            List<StatementSyntax> statements = new List<StatementSyntax>();

            foreach (var subType in nonAbstractDerivedTypes)
            {
                var subTypeFullname = GetFullName(subType);

                var subTypeCamelCaseName = MakeFirstLetterSmall(subType.Name);

                var newStatement = IfStatement(
                    IsPatternExpression(
                        IdentifierName(firstParameterName),
                        DeclarationPattern(
                            IdentifierName(subTypeFullname),
                            SingleVariableDesignation(
                                Identifier(subTypeCamelCaseName)))),
                    ReturnStatement(
                        InvocationExpression(
                                IdentifierName(subTypeCamelCaseName + "Case"))
                            .WithArgumentList(
                                ArgumentList(
                                    SingletonSeparatedList(
                                        Argument(
                                            IdentifierName(subTypeCamelCaseName)))))));

                statements.Add(newStatement);
            }

            var throwStatementSyntax = ThrowStatement(
                ObjectCreationExpression(
                        IdentifierName("System.Exception"))
                    .WithArgumentList(
                        ArgumentList(
                            SingletonSeparatedList(
                                Argument(
                                    LiteralExpression(
                                        SyntaxKind.StringLiteralExpression,
                                        Literal("Invalid " + doClassName + " type")))))));

            statements.Add(throwStatementSyntax);

            return MethodDeclaration(
                    IdentifierName("TResult"),
                    Identifier("Match"))
                .WithModifiers(
                    TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
                .WithTypeParameterList(
                    TypeParameterList(
                        SingletonSeparatedList(
                            TypeParameter(
                                Identifier("TResult")))))
                .WithParameterList(
                    parameterListSyntax)
                .WithBody(
                    Block(
                        statements.ToArray()));
        }

        private MethodDeclarationSyntax CreateMatchMethodThatReturnsVoid(
            INamedTypeSymbol typeSymbol,
            INamedTypeSymbol[] nonAbstractDerivedTypes)
        {
            var doClassName = typeSymbol.Name;

            var doClassFullname = GetFullName(typeSymbol);

            var classFullname = doClassFullname;
            var firstParameterName = MakeFirstLetterSmall(doClassName);

            List<SyntaxNodeOrToken> parametersAndCommas = new List<SyntaxNodeOrToken>();

            parametersAndCommas.Add(Parameter(
                    Identifier(firstParameterName))
                .WithModifiers(
                    TokenList(
                        Token(SyntaxKind.ThisKeyword)))
                .WithType(
                    IdentifierName(classFullname)));

            foreach (var subType in nonAbstractDerivedTypes)
            {
                var subTypeFullname = GetFullName(subType);

                var subTypeCamelCaseName = MakeFirstLetterSmall(subType.Name);

                var newParam = Parameter(
                        Identifier(subTypeCamelCaseName + "Case"))
                    .WithType(
                        GenericName(
                                Identifier("System.Action"))
                            .WithTypeArgumentList(
                                TypeArgumentList(
                                    SeparatedList<TypeSyntax>(
                                        new SyntaxNodeOrToken[]
                                        {
                                            IdentifierName(subTypeFullname)
                                        }))));

                parametersAndCommas.Add(Token(SyntaxKind.CommaToken));
                parametersAndCommas.Add(newParam);
            }

            var parameterListSyntax = ParameterList(
                SeparatedList<ParameterSyntax>(
                    parametersAndCommas.ToArray()));

            List<StatementSyntax> statements = new List<StatementSyntax>();

            foreach (var subType in nonAbstractDerivedTypes)
            {
                var subTypeFullname = GetFullName(subType);

                var subTypeCamelCaseName = MakeFirstLetterSmall(subType.Name);

                var newStatement = IfStatement(
                    IsPatternExpression(
                        IdentifierName(firstParameterName),
                        DeclarationPattern(
                            IdentifierName(subTypeFullname),
                            SingleVariableDesignation(
                                Identifier(subTypeCamelCaseName)))),
                    Block(
                        ExpressionStatement(
                            InvocationExpression(
                                    IdentifierName(subTypeCamelCaseName + "Case"))
                                .WithArgumentList(
                                    ArgumentList(
                                        SingletonSeparatedList(
                                            Argument(
                                                IdentifierName(subTypeCamelCaseName)))))),
                        ReturnStatement()));

                statements.Add(newStatement);
            }

            var throwStatementSyntax = ThrowStatement(
                ObjectCreationExpression(
                        IdentifierName("System.Exception"))
                    .WithArgumentList(
                        ArgumentList(
                            SingletonSeparatedList(
                                Argument(
                                    LiteralExpression(
                                        SyntaxKind.StringLiteralExpression,
                                        Literal("Invalid " + doClassName + " type")))))));

            statements.Add(throwStatementSyntax);

            return MethodDeclaration(
                    IdentifierName("void"),
                    Identifier("Match"))
                .WithModifiers(
                    TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
                .WithTypeParameterList(
                    TypeParameterList(
                        SingletonSeparatedList(
                            TypeParameter(
                                Identifier("TResult")))))
                .WithParameterList(
                    parameterListSyntax)
                .WithBody(
                    Block(
                        statements.ToArray()));
        }

        private string GetFullName(ITypeSymbol typeSymbol)
        {
            string name = typeSymbol.Name;

            if (typeSymbol is INamedTypeSymbol namedType)
            {
                if (namedType.IsGenericType)
                {
                    name += "<" + string.Join(", ", namedType.TypeArguments.Select(x => GetFullName(x))) + ">";
                }
            }

            if (typeSymbol.ContainingType != null)
                return GetFullName(typeSymbol.ContainingType) + "." + name;

            if (typeSymbol.ContainingNamespace != null)
                return GetFullName(typeSymbol.ContainingNamespace) + "." + name;

            return name;
        }

        private string GetFullName(INamespaceSymbol @namespace)
        {
            string name = @namespace.Name;

            if (@namespace.ContainingNamespace != null && !@namespace.ContainingNamespace.IsGlobalNamespace)
                return GetFullName(@namespace.ContainingNamespace) + "." + name;

            return name;

        }

        private string MakeFirstLetterSmall(string str)
        {
            return str.Substring(0, 1).ToLower() + str.Substring(1);
        }
    }
}
