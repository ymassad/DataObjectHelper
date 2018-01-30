using System;
using System.Collections.Generic;
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

            var doTypeSymbol = Utilities.GetDataObjectType(attributeSyntax)
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
                .ChainValues<ClassDeclarationSyntax, MethodDeclarationSyntax[], Solution>(AddMethodsToClass);

            return solutionToReturn.ValueOr(originalSolution);
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
            PropertyAndParameter[] constructorParametersAndCorrespondingProperties,
            INamedTypeSymbol doType,
            string[] existingWithMethodNames)
        {
            return
                constructorParametersAndCorrespondingProperties
                    .Select(item => new { item, newMethodName = "With" + item.Property.Name })
                    .Where(x => !existingWithMethodNames.Contains(x.newMethodName))
                    .Select(x => CreateWithMethod(doType, constructorParametersAndCorrespondingProperties, x.item))
                    .ToArray<MethodDeclarationSyntax>();
        }

        public static Maybe<PropertyAndParameter[]> GetConstructorParametersAndCorrespondingProperties(
            INamedTypeSymbol symbol,
            IPropertySymbol[] properties)
        {
            return
                symbol.Constructors
                    .FirstOrNoValue().ChainValue<PropertyAndParameter[]>(x =>
                        ImmutableArrayExtensions.Select<IParameterSymbol, Maybe<PropertyAndParameter>>(x.Parameters, param => GetMatchingProperty(properties, param)
                                    .ChainValue(p => new PropertyAndParameter(param, p)))
                            .ToArray()
                            .Traverse());
        }

        public static Maybe<IPropertySymbol> GetMatchingProperty(
            IPropertySymbol[] properties,
            IParameterSymbol param)
        {
            return
                properties
                    .FirstOrNoValue(prop =>
                        prop.Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase)
                        && prop.Type.Equals(param.Type));
        }

        public static MethodDeclarationSyntax CreateWithMethod(
            INamedTypeSymbol doTypeSymbol,
            PropertyAndParameter[] allProperties,
            PropertyAndParameter propertyToCreateMethodFor)
        {
            var newMethodName = "With" + propertyToCreateMethodFor.Property.Name;

            var doFullname = Utilities.GetFullName(doTypeSymbol);

            var thisParameterName = Utilities.MakeFirstLetterSmall(doTypeSymbol.Name);

            var propertyTypeFullName = Utilities.GetFullName(propertyToCreateMethodFor.Property.Type);

            List<SyntaxNodeOrToken> nodes = new List<SyntaxNodeOrToken>();

            foreach (var property in allProperties)
            {
                var propertyName = property.Property.Name;

                var propertyNameCamelCase = Utilities.MakeFirstLetterSmall(property.Property.Name);

                if (nodes.Count > 0)
                    nodes.Add(SyntaxFactory.Token(SyntaxKind.CommaToken));

                if (property.Property.Equals(propertyToCreateMethodFor.Property))
                {
                    nodes.Add(
                        SyntaxFactory.Argument(
                                SyntaxFactory.IdentifierName("newValue"))
                            .WithNameColon(
                                SyntaxFactory.NameColon(
                                    SyntaxFactory.IdentifierName(propertyNameCamelCase))));
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
                                    SyntaxFactory.IdentifierName(propertyNameCamelCase))));
                }
            }


            return SyntaxFactory.MethodDeclaration(
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
                                SyntaxFactory.ObjectCreationExpression(
                                        SyntaxFactory.IdentifierName(doFullname))
                                    .WithArgumentList(
                                        SyntaxFactory.ArgumentList(
                                            SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                                nodes.ToArray())))))));
        }
    }
}
