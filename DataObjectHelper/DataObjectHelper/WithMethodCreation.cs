using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataObjectHelper
{
    public static class WithMethodCreation
    {
        public static async Task<Solution> CreateWithMethods(Document document, AttributeSyntax attributeSyntax, SyntaxNode root, CancellationToken cancellationToken)
        {
            var originalSolution = document.Project.Solution;

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            var hostClass = Utilities.ResolveStaticClassWhereToInsertMethods(
                attributeSyntax);

            var doTypeSymbols = Utilities.GetDataObjectTypes(attributeSyntax)
                .ChainValue(types =>
                    types
                        .Select(type =>
                            semanticModel.GetSymbolInfo(type).Symbol
                                .TryCast().To<INamedTypeSymbol>()
                                .If(x => x.TypeKind == TypeKind.Class)
                                .If(x => !x.IsAbstract))
                        .Traverse());

            var methodsToAdd =
                (hostClass, doTypeSymbols)
                .ChainValues((host, symbols) =>
                    symbols
                        .Select(symbol =>
                            GetMethodsToAddForType(symbol, host, semanticModel))
                        .ItemsWithValue()
                        .SelectMany(x => x)
                        .ToArray())
                .If(x => x.Length > 0);
 
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

        private static Maybe<MethodDeclarationSyntax[]> GetMethodsToAddForType(
            INamedTypeSymbol doTypeSymbol,
            ClassDeclarationSyntax hostClass,
            SemanticModel semanticModel)
        {
            if (doTypeSymbol.IsUnboundGenericType)
                doTypeSymbol = GetFullConstructedForm(doTypeSymbol);

            var dataObjectProperties =
                doTypeSymbol.GetMembers().OfType<IPropertySymbol>().ToArray();

            var typeConstructorDetails =
                GetTypeConstructorDetails(doTypeSymbol, dataObjectProperties);

            var hostClassSymbol = semanticModel.GetDeclaredSymbol(hostClass)
                .TryCast().To<INamedTypeSymbol>();

            var existingWithMethods =
                hostClassSymbol
                    .ChainValue(host => GetExistingWithMethods(host, doTypeSymbol));

            return (typeConstructorDetails, existingWithMethods)
                .ChainValues((details, existing) => CreateWithMethodsToAdd(details, doTypeSymbol, existing));
        }

        private static INamedTypeSymbol GetFullConstructedForm(INamedTypeSymbol doTypeSymbol)
        {
            if (doTypeSymbol.ContainingType != null)
            {
                return GetFullConstructedForm(doTypeSymbol.ContainingType)
                    .GetTypeMembers(doTypeSymbol.Name, doTypeSymbol.Arity).First();
            }

            return doTypeSymbol.ConstructedFrom;
        }

        public static string[] GetExistingWithMethods(INamedTypeSymbol hostClass, INamedTypeSymbol dataObject)
        {
            return hostClass.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(x => x.Name.StartsWith("With"))
                .Where(x => x.Parameters.Length == 2 && x.Parameters[0].Type.Equals(dataObject))
                .Select(x => x.Name)
                .ToArray();
        }

        public static MethodDeclarationSyntax[] CreateWithMethodsToAdd(
            TypeConstructorDetails typeConstructorDetails,
            INamedTypeSymbol doType,
            string[] existingWithMethodNames)
        {
            return
                typeConstructorDetails.PropertyAndParameters
                    .Select(item => new { item, newMethodName = "With" + item.Property.Name })
                    .Where(x => !existingWithMethodNames.Contains(x.newMethodName))
                    .Select(x => CreateWithMethod(doType, typeConstructorDetails, x.item))
                    .ToArray();
        }

        public static Maybe<TypeConstructorDetails> GetTypeConstructorDetails(
            INamedTypeSymbol symbol,
            IPropertySymbol[] properties)
        {
            var normalConstructor = symbol.Constructors
                .Where(x => x.Parameters.Length == properties.Length)
                .FirstOrNoValue();

            Maybe<ImmutableArray<PropertyAndParameter>> CreatePropertyAndParameterMap(IMethodSymbol methodSymbol)
            {
                return methodSymbol.Parameters
                    .Select(param =>
                        GetMatchingProperty(properties, param)
                            .ChainValue(p => new PropertyAndParameter(param, p)))
                    .Traverse()
                    .ChainValue(x => x.ToImmutableArray());
            }

            return normalConstructor.Match(cons =>
            {
                return CreatePropertyAndParameterMap(cons).ChainValue(x =>
                    new TypeConstructorDetails(new Constructor.NormalConstructor(), x));
            },
            () =>
            {
                var newMethod =
                    symbol.BaseType.ToMaybe()
                        .ChainValue(x =>
                            x.GetMembers("New" + symbol.Name)
                                .OfType<IMethodSymbol>()
                                .FirstOrNoValue())
                        .If(x => x.IsStatic)
                        .If(x => x.DeclaredAccessibility == Accessibility.Public);

                return newMethod.ChainValue(x => CreatePropertyAndParameterMap(x)).ChainValue(x =>
                    new TypeConstructorDetails(new Constructor.FSharpUnionCaseNewMethod(), x));
            });
        }

        public static Maybe<IPropertySymbol> GetMatchingProperty(
            IPropertySymbol[] properties,
            IParameterSymbol param)
        {
            bool AreEqual(IPropertySymbol prop)
            {
                return prop.Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase) ||
                       ("_" + prop.Name).Equals(param.Name, StringComparison.OrdinalIgnoreCase);
            }

            return
                properties
                    .FirstOrNoValue(prop =>
                        AreEqual(prop)
                        && prop.Type.Equals(param.Type));
        }

        public static MethodDeclarationSyntax CreateWithMethod(
            INamedTypeSymbol doTypeSymbol,
            TypeConstructorDetails typeConstructorDetails,
            PropertyAndParameter propertyToCreateMethodFor)
        {
            var newMethodName = "With" + propertyToCreateMethodFor.Property.Name;

            var doFullname = Utilities.GetFullName(doTypeSymbol);

            var thisParameterName = Utilities.MakeFirstLetterSmall(doTypeSymbol.Name);

            var propertyTypeFullName = Utilities.GetFullName(propertyToCreateMethodFor.Property.Type);

            List<SyntaxNodeOrToken> nodes = new List<SyntaxNodeOrToken>();

            foreach (var property in typeConstructorDetails.PropertyAndParameters)
            {
                var propertyName = property.Property.Name;

                var parameterName = Utilities.MakeFirstLetterSmall(property.Parameter.Name);

                if (nodes.Count > 0)
                    nodes.Add(SyntaxFactory.Token(SyntaxKind.CommaToken));

                if (property.Property.Equals(propertyToCreateMethodFor.Property))
                {
                    nodes.Add(
                        SyntaxFactory.Argument(
                                SyntaxFactory.IdentifierName("newValue"))
                            .WithNameColon(
                                SyntaxFactory.NameColon(
                                    SyntaxFactory.IdentifierName(parameterName))));
                }
                else
                {
                    nodes.Add(
                        SyntaxFactory.Argument(
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    SyntaxFactory.IdentifierName(thisParameterName),
                                    SyntaxFactory.IdentifierName(propertyName)))
                            .WithNameColon(
                                SyntaxFactory.NameColon(
                                    SyntaxFactory.IdentifierName(parameterName))));
                }
            }


            var constructionExpressionSyntax =
                typeConstructorDetails.Constructor
                    .Match<ExpressionSyntax>(caseNormalConstructor: () => 
                        SyntaxFactory.ObjectCreationExpression(
                            SyntaxFactory.IdentifierName(doFullname))
                                .WithArgumentList(
                                    SyntaxFactory.ArgumentList(
                                        SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                            nodes.ToArray()))),
                        caseFSharpUnionCaseNewMethod: () =>
                            SyntaxFactory.CastExpression(
                                SyntaxFactory.IdentifierName(doFullname),
                                SyntaxFactory.InvocationExpression(
                                    SyntaxFactory.MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        SyntaxFactory.IdentifierName(Utilities.GetFullName(doTypeSymbol.BaseType)),
                                        SyntaxFactory.IdentifierName("New" + doTypeSymbol.Name)))
                                    .WithArgumentList(
                                        SyntaxFactory.ArgumentList(
                                            SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                                nodes.ToArray())))));


            var method =
                SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.IdentifierName(doFullname),
                    SyntaxFactory.Identifier(newMethodName))
                .WithModifiers(
                    SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithParameterList(
                    SyntaxFactory.ParameterList(
                        SyntaxFactory.SeparatedList<ParameterSyntax>(
                            new SyntaxNodeOrToken[]{
                                SyntaxFactory.Parameter(
                                        SyntaxFactory.Identifier(thisParameterName))
                                    .WithModifiers(
                                        SyntaxFactory.TokenList(
                                            SyntaxFactory.Token(SyntaxKind.ThisKeyword)))
                                    .WithType(
                                        SyntaxFactory.IdentifierName(doFullname)),
                                SyntaxFactory.Token(SyntaxKind.CommaToken),
                                SyntaxFactory.Parameter(
                                        SyntaxFactory.Identifier("newValue"))
                                    .WithType(
                                        SyntaxFactory.IdentifierName(propertyTypeFullName))})))
                .WithBody(
                    SyntaxFactory.Block(
                        SyntaxFactory.SingletonList<StatementSyntax>(
                            SyntaxFactory.ReturnStatement(
                                constructionExpressionSyntax))));

            if (doTypeSymbol.IsGenericType)
            {
                var openTypeParams = GetOpenTypeParameters(doTypeSymbol);

                if (openTypeParams.Any())
                {
                    method = method.WithTypeParameterList(
                        SyntaxFactory.TypeParameterList(
                            SyntaxFactory.SeparatedList(
                                openTypeParams
                                    .Select(x => SyntaxFactory.TypeParameter(SyntaxFactory.Identifier(x.Name))))));
                }
            }

            return method;
        }

        private static ITypeSymbol[] GetOpenTypeParameters(INamedTypeSymbol doTypeSymbol)
        {
            var myParams = doTypeSymbol.TypeArguments
                .Where(x => x.TypeKind == TypeKind.TypeParameter)
                .ToArray();

            if (doTypeSymbol.ContainingType != null)
            {
                return GetOpenTypeParameters(doTypeSymbol.ContainingType).Concat(myParams).ToArray();
            }

            return myParams;
        }
    }
}
