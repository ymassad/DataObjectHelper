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

            var doTypeSymbols = GetDataObjectTypeToCreateWithMethodsFor(attributeSyntax, semanticModel);

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

        private static Maybe<INamedTypeSymbol[]> GetDataObjectTypeToCreateWithMethodsFor(
            AttributeSyntax attributeSyntax,
            SemanticModel semanticModel)
        {
            return Utilities.GetDataObjectTypesSpecifiedInAttribute(attributeSyntax)
                .ChainValue(types =>
                    types
                        .Select(type =>
                        {
                            return semanticModel.GetSymbolInfo(type).Symbol
                                .TryCast().To<INamedTypeSymbol>()
                                .If(x => x.TypeKind == TypeKind.Class)
                                .ChainValue(GetDataObjectsTypes);
                        })
                        .Traverse())
                .ChainValue(x => x.SelectMany(c => c).ToArray());
        }

        private static INamedTypeSymbol[] GetDataObjectsTypes(INamedTypeSymbol @class)
        {
            if (@class.IsStatic)
            {
                return Utilities.GetModuleClasses(@class)
                    .SelectMany(GetDataObjectsTypes)
                    .ToArray();
            }
            else
            {
                INamedTypeSymbol[] GetDerivedChildren(INamedTypeSymbol type)
                {
                    return type.GetTypeMembers().Where(x => object.Equals(x.BaseType, type)).ToArray();
                }

                var constructed = Utilities.GetFullConstructedForm(@class);

                return GetDerivedChildren(constructed).Concat(new[] { constructed }).Where(c => !c.IsAbstract).ToArray();
            }
        }

        private static Maybe<MethodDeclarationSyntax[]> GetMethodsToAddForType(
            INamedTypeSymbol doTypeSymbol,
            ClassDeclarationSyntax hostClass,
            SemanticModel semanticModel)
        {
            var dataObjectProperties =
                GetDataObjectProperties(doTypeSymbol);

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

        private static IPropertySymbol[] GetDataObjectProperties(INamedTypeSymbol doTypeSymbol)
        {
            var props = doTypeSymbol.GetMembers().OfType<IPropertySymbol>().ToList();

            if (doTypeSymbol.BaseType != null)
            {
                props.AddRange(GetDataObjectProperties(doTypeSymbol.BaseType));
            }

            return props.ToArray();
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
                            Utilities.CreateSimpleMemberAccessSyntax(thisParameterName, propertyName))
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
                                    Utilities.CreateSimpleMemberAccessSyntax(Utilities.GetFullName(doTypeSymbol.BaseType), "New" + doTypeSymbol.Name))
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
                var openTypeParams = Utilities.GetOpenTypeParameters(doTypeSymbol);

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
    }
}
