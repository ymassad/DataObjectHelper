using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataObjectHelper
{
    public static class Utilities
    {
        public static Maybe<TypeSyntax[]> GetDataObjectTypesSpecifiedInAttribute(AttributeSyntax attributeSyntax)
        {
            return attributeSyntax.ArgumentList.Arguments
                .Select(x => x.Expression.TryCast().To<TypeOfExpressionSyntax>())
                .Select(e => e.ChainValue(x => x.Type))
                .Traverse()
                .ChainValue(x => x.ToArray());
        }

        public static Maybe<ClassDeclarationSyntax> ResolveStaticClassWhereToInsertMethods(
            AttributeSyntax attributeSyntax)
        {
            return
                attributeSyntax.Ancestors()
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrNoValue()
                    .If(x => x.Modifiers.Any(IsStaticKeyword));
        }

        public static bool IsStaticKeyword(SyntaxToken m)
        {
            return m.RawKind == (int)SyntaxKind.StaticKeyword;
        }

        public static bool IsAction(INamedTypeSymbol namedTypeSymbol)
        {
            return namedTypeSymbol.IsGenericType && namedTypeSymbol.ConstructUnboundGenericType().Name == "Action";
        }

        public static bool IsFunc(INamedTypeSymbol type)
        {
            return type.IsGenericType && type.ConstructUnboundGenericType().Name == "Func";
        }

        public static string GetFullName(ITypeSymbol typeSymbol)
        {
            if (typeSymbol.TypeKind == TypeKind.TypeParameter)
                return typeSymbol.Name;

            string name = typeSymbol.Name;

            if (typeSymbol is INamedTypeSymbol namedType)
            {
                if (namedType.IsGenericType && namedType.TypeArguments.Any())
                {
                    name += "<" + String.Join(", ", namedType.TypeArguments.Select(x => GetFullName(x))) + ">";
                }
            }

            if (typeSymbol.ContainingType != null)
                return GetFullName(typeSymbol.ContainingType) + "." + name;

            if (typeSymbol.ContainingNamespace != null)
                return GetFullName(typeSymbol.ContainingNamespace) + "." + name;

            return name;
        }

        public static string GetFullName(INamespaceSymbol @namespace)
        {
            string name = @namespace.Name;

            if (@namespace.ContainingNamespace != null && !@namespace.ContainingNamespace.IsGlobalNamespace)
                return GetFullName(@namespace.ContainingNamespace) + "." + name;

            return name;

        }

        public static string MakeFirstLetterSmall(string str)
        {
            return str.Substring(0, 1).ToLower() + str.Substring(1);
        }

        public static INamedTypeSymbol[] GetParameterTypes(IMethodSymbol methodSymbol)
        {
            return methodSymbol.Parameters.Select(x => x.Type).ToArray().OfType<INamedTypeSymbol>().ToArray();
        }

        public static ITypeSymbol[] GetOpenTypeParameters(INamedTypeSymbol doTypeSymbol)
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

        public static INamedTypeSymbol GetFullConstructedForm(INamedTypeSymbol doTypeSymbol)
        {
            if (!doTypeSymbol.IsUnboundGenericType)
                return doTypeSymbol;
            
            if (doTypeSymbol.ContainingType != null)
            {
                return GetFullConstructedForm(doTypeSymbol.ContainingType)
                    .GetTypeMembers(doTypeSymbol.Name, doTypeSymbol.Arity).First();
            }

            return doTypeSymbol.ConstructedFrom;
        }
    }
}
