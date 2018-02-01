﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DataObjectHelper
{
    public static class MatchCreation
    {
        public static async Task<Solution> CreateMatchMethods(
            Document document, AttributeSyntax attributeSyntax, SyntaxNode root, CancellationToken cancellationToken)
        {
            var originalSolution = document.Project.Solution;

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            var hostClass =
                Utilities.ResolveStaticClassWhereToInsertMethods(
                    attributeSyntax);

            var doTypeSymbols = Utilities.GetDataObjectTypesSpecifiedInAttribute(attributeSyntax)
                .ChainValue(types =>
                    types
                        .Select(type =>
                            semanticModel.GetSymbolInfo(type).Symbol
                                .TryCast().To<INamedTypeSymbol>()
                                .If(x => x.TypeKind == TypeKind.Class))
                        .ToArray()
                        .Traverse());

            var hostClassSymbol = hostClass
                .ChainValue(x => semanticModel.GetDeclaredSymbol(x))
                .TryCast().To<INamedTypeSymbol>();

            var methodsToAdd =
                (hostClassSymbol, doTypeSymbols)
                    .ChainValues((host, symbols) => 
                        symbols
                            .Select(symbol => 
                                GetMethodsToAddForType(host, symbol, originalSolution))
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
                .ChainValues(AddMethodsToClass)
                .ValueOr(originalSolution);

            return solutionToReturn;
        }

        private static Maybe<MethodDeclarationSyntax[]> GetMethodsToAddForType(INamedTypeSymbol hostClassSymbol, INamedTypeSymbol doTypeSymbol, Solution originalSolution)
        {
            var existingMethods =
                hostClassSymbol.GetMembers("Match")
                    .OfType<IMethodSymbol>()
                    .Where(x => x.Parameters.Length >= 1 && x.Parameters[0].Type.Equals(doTypeSymbol))
                    .ToArray();

            var casesMaybe = CreateCases(doTypeSymbol, originalSolution);

            return casesMaybe
                .ChainValue(cases =>
                {
                    bool methodMethod1Exist1 = existingMethods.Any(e => Utilities.GetParameterTypes(e).Any(Utilities.IsFunc));

                    bool methodMethod2Exist1 = existingMethods.Any(e => Utilities.GetParameterTypes(e).Any(Utilities.IsAction));

                    List<MethodDeclarationSyntax> methods1 = new List<MethodDeclarationSyntax>();

                    if (!methodMethod1Exist1)
                        methods1.Add(CreateMatchMethod(doTypeSymbol, cases));

                    if (!methodMethod2Exist1)
                        methods1.Add(CreateMatchMethodThatReturnsVoid(doTypeSymbol, cases));

                    return methods1.ToArray();
                });
        }

        public static Maybe<Case[]> CreateCases(INamedTypeSymbol doTypeSymbol,
            Solution originalSolution)
        {
            var allMembers = doTypeSymbol.GetMembers();

            var tagsClass =
                allMembers
                    .Where(x => x.Name == "Tags")
                    .OfType<INamedTypeSymbol>()
                    .Where(x => x.IsStatic)
                    .FirstOrNoValue();

            return tagsClass.Match(
                whenHasValue: cls =>
                {

                    Case CreateCase(string name)
                    {
                        var nestedType = allMembers.OfType<INamedTypeSymbol>().Where(x => x.Name == name)
                            .FirstOrNoValue();

                        return nestedType.Match(x => (Case)new Case.ClassCase(x), () => new Case.FSharpNullCase(name));
                    }

                    return cls.GetMembers().OfType<IFieldSymbol>().Select(x => CreateCase(x.Name)).ToArray().ToMaybe().If(x => x.Length > 0);

                }, caseHasNoValue: () =>
                {
                    var caseTypes =
                        SymbolFinder.FindDerivedClassesAsync(doTypeSymbol, originalSolution).Result;

                    return caseTypes
                        .Where(x => !x.IsAbstract)
                        .Select(x => (Case)new Case.ClassCase(x))
                        .ToArray()
                        .ToMaybe()
                        .If(x => x.Length > 0);
                });
        }

        public static MethodDeclarationSyntax CreateMatchMethod(
            INamedTypeSymbol typeSymbol,
            Case[] cases)
        {
            var doClassName = typeSymbol.Name;

            var doClassFullname = Utilities.GetFullName(typeSymbol);

            var classFullname = doClassFullname;

            var firstParameterName = Utilities.MakeFirstLetterSmall(doClassName);

            List<SyntaxNodeOrToken> parametersAndCommas = new List<SyntaxNodeOrToken>();

            parametersAndCommas.Add(SyntaxFactory.Parameter(
                    SyntaxFactory.Identifier(firstParameterName))
                .WithModifiers(
                    SyntaxFactory.TokenList(
                        SyntaxFactory.Token(SyntaxKind.ThisKeyword)))
                .WithType(
                    SyntaxFactory.IdentifierName(classFullname)));

            foreach (var currentCase in cases)
            {
                var paramToAdd =
                    currentCase.Match(caseClassCase: classCase =>
                        {
                            var subTypeFullname = Utilities.GetFullName(classCase.Symbol);

                            var subTypeCamelCaseName = Utilities.MakeFirstLetterSmall(classCase.Symbol.Name);

                            return
                                SyntaxFactory.Parameter(
                                        SyntaxFactory.Identifier(subTypeCamelCaseName + "Case"))
                                    .WithType(
                                        SyntaxFactory.GenericName(
                                                SyntaxFactory.Identifier("System.Func"))
                                            .WithTypeArgumentList(
                                                SyntaxFactory.TypeArgumentList(
                                                    SyntaxFactory.SeparatedList<TypeSyntax>(
                                                        new SyntaxNodeOrToken[]
                                                        {
                                                            SyntaxFactory.IdentifierName(subTypeFullname),
                                                            SyntaxFactory.Token(SyntaxKind.CommaToken),
                                                            SyntaxFactory.IdentifierName("TResult")
                                                        }))));
                        },
                        caseFSharpNullCase: fsharpNullCase =>
                        {
                            return
                                SyntaxFactory.Parameter(
                                        SyntaxFactory.Identifier(Utilities.MakeFirstLetterSmall(fsharpNullCase.Name) + "Case"))
                                    .WithType(
                                        SyntaxFactory.GenericName(
                                                SyntaxFactory.Identifier("System.Func"))
                                            .WithTypeArgumentList(
                                                SyntaxFactory.TypeArgumentList(
                                                    SyntaxFactory.SeparatedList<TypeSyntax>(
                                                        new SyntaxNodeOrToken[]
                                                        {
                                                            SyntaxFactory.IdentifierName("TResult")
                                                        }))));
                        });

                parametersAndCommas.Add(SyntaxFactory.Token(SyntaxKind.CommaToken));
                parametersAndCommas.Add(paramToAdd);
            }

            var parameterListSyntax = SyntaxFactory.ParameterList(
                SyntaxFactory.SeparatedList<ParameterSyntax>(
                    parametersAndCommas.ToArray()));

            List<StatementSyntax> statements = new List<StatementSyntax>();

            foreach (var currentCase in cases)
            {

                var statementToAdd =
                    currentCase.Match(caseClassCase: classCase =>
                        {

                            var subTypeFullname = Utilities.GetFullName(classCase.Symbol);

                            var caseCamelCaseName = Utilities.MakeFirstLetterSmall(classCase.Symbol.Name);

                            return
                                SyntaxFactory.IfStatement(
                                    SyntaxFactory.IsPatternExpression(
                                        SyntaxFactory.IdentifierName(firstParameterName),
                                        SyntaxFactory.DeclarationPattern(
                                            SyntaxFactory.IdentifierName(subTypeFullname),
                                            SyntaxFactory.SingleVariableDesignation(
                                                SyntaxFactory.Identifier(caseCamelCaseName)))),
                                    SyntaxFactory.ReturnStatement(
                                        SyntaxFactory.InvocationExpression(
                                                SyntaxFactory.IdentifierName(caseCamelCaseName + "Case"))
                                            .WithArgumentList(
                                                SyntaxFactory.ArgumentList(
                                                    SyntaxFactory.SingletonSeparatedList(
                                                        SyntaxFactory.Argument(
                                                            SyntaxFactory.IdentifierName(caseCamelCaseName)))))));
                        },
                        caseFSharpNullCase: fsharpNullCase =>
                        {
                            var caseCamelCaseName = Utilities.MakeFirstLetterSmall(fsharpNullCase.Name);

                            return
                                SyntaxFactory.IfStatement(

                                    SyntaxFactory.MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        SyntaxFactory.IdentifierName(firstParameterName),
                                        SyntaxFactory.IdentifierName("Is" + fsharpNullCase.Name)),
                                    SyntaxFactory.ReturnStatement(
                                        SyntaxFactory.InvocationExpression(
                                                SyntaxFactory.IdentifierName(caseCamelCaseName + "Case"))
                                            .WithArgumentList(
                                                SyntaxFactory.ArgumentList())));
                        });
                statements.Add(statementToAdd);
            }

            var throwStatementSyntax = SyntaxFactory.ThrowStatement(
                SyntaxFactory.ObjectCreationExpression(
                        SyntaxFactory.IdentifierName("System.Exception"))
                    .WithArgumentList(
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.StringLiteralExpression,
                                        SyntaxFactory.Literal("Invalid " + doClassName + " type")))))));

            statements.Add(throwStatementSyntax);

            return SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.IdentifierName("TResult"),
                    SyntaxFactory.Identifier("Match"))
                .WithModifiers(
                    SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithTypeParameterList(
                    SyntaxFactory.TypeParameterList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.TypeParameter(
                                SyntaxFactory.Identifier("TResult")))))
                .WithParameterList(
                    parameterListSyntax)
                .WithBody(
                    SyntaxFactory.Block(
                        statements.ToArray()));
        }

        public static MethodDeclarationSyntax CreateMatchMethodThatReturnsVoid(
            INamedTypeSymbol typeSymbol,
            Case[] cases)
        {
            var doClassName = typeSymbol.Name;

            var doClassFullname = Utilities.GetFullName(typeSymbol);

            var classFullname = doClassFullname;

            var firstParameterName = Utilities.MakeFirstLetterSmall(doClassName);

            List<SyntaxNodeOrToken> parametersAndCommas = new List<SyntaxNodeOrToken>();

            parametersAndCommas.Add(SyntaxFactory.Parameter(
                    SyntaxFactory.Identifier(firstParameterName))
                .WithModifiers(
                    SyntaxFactory.TokenList(
                        SyntaxFactory.Token(SyntaxKind.ThisKeyword)))
                .WithType(
                    SyntaxFactory.IdentifierName(classFullname)));

            foreach (var currentCase in cases)
            {
                var paramToAdd =
                    currentCase.Match(caseClassCase: classCase =>
                        {
                            var subTypeFullname = Utilities.GetFullName(classCase.Symbol);

                            var subTypeCamelCaseName = Utilities.MakeFirstLetterSmall(classCase.Symbol.Name);

                            return
                                SyntaxFactory.Parameter(
                                        SyntaxFactory.Identifier(subTypeCamelCaseName + "Case"))
                                    .WithType(
                                        SyntaxFactory.GenericName(
                                                SyntaxFactory.Identifier("System.Action"))
                                            .WithTypeArgumentList(
                                                SyntaxFactory.TypeArgumentList(
                                                    SyntaxFactory.SeparatedList<TypeSyntax>(
                                                        new SyntaxNodeOrToken[]
                                                        {
                                                            SyntaxFactory.IdentifierName(subTypeFullname),
                                                        }))));


                        },
                        caseFSharpNullCase: fsharpNullCase =>
                        {
                            return
                                SyntaxFactory.Parameter(
                                        SyntaxFactory.Identifier(Utilities.MakeFirstLetterSmall(fsharpNullCase.Name) + "Case"))
                                    .WithType(
                                        SyntaxFactory.IdentifierName("System.Action"));
                        });

                parametersAndCommas.Add(SyntaxFactory.Token(SyntaxKind.CommaToken));
                parametersAndCommas.Add(paramToAdd);
            }

            var parameterListSyntax = SyntaxFactory.ParameterList(
                SyntaxFactory.SeparatedList<ParameterSyntax>(
                    parametersAndCommas.ToArray()));

            List<StatementSyntax> statements = new List<StatementSyntax>();

            foreach (var currentCase in cases)
            {
                var statementToAdd =
                    currentCase.Match(caseClassCase: classCase =>
                        {
                            var subTypeFullname = Utilities.GetFullName(classCase.Symbol);

                            var caseCamelCaseName = Utilities.MakeFirstLetterSmall(classCase.Symbol.Name);

                            return
                                SyntaxFactory.IfStatement(
                                    SyntaxFactory.IsPatternExpression(
                                        SyntaxFactory.IdentifierName(firstParameterName),
                                        SyntaxFactory.DeclarationPattern(
                                            SyntaxFactory.IdentifierName(subTypeFullname),
                                            SyntaxFactory.SingleVariableDesignation(
                                                SyntaxFactory.Identifier(caseCamelCaseName)))),
                                    SyntaxFactory.Block(
                                        SyntaxFactory.ExpressionStatement(
                                            SyntaxFactory.InvocationExpression(
                                                    SyntaxFactory.IdentifierName(caseCamelCaseName + "Case"))
                                                .WithArgumentList(
                                                    SyntaxFactory.ArgumentList(
                                                        SyntaxFactory.SingletonSeparatedList(
                                                            SyntaxFactory.Argument(
                                                                SyntaxFactory.IdentifierName(caseCamelCaseName)))))),
                                        SyntaxFactory.ReturnStatement()));


 

                        },
                        caseFSharpNullCase: fsharpNullCase =>
                        {
                            var caseCamelCaseName = Utilities.MakeFirstLetterSmall(fsharpNullCase.Name);

                            return
                                SyntaxFactory.IfStatement(

                                    SyntaxFactory.MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        SyntaxFactory.IdentifierName(firstParameterName),
                                        SyntaxFactory.IdentifierName("Is" + fsharpNullCase.Name)),
                                    SyntaxFactory.Block(
                                        SyntaxFactory.ExpressionStatement(
                                            SyntaxFactory.InvocationExpression(
                                                    SyntaxFactory.IdentifierName(caseCamelCaseName + "Case"))
                                                .WithArgumentList(
                                                    SyntaxFactory.ArgumentList())),
                                        SyntaxFactory.ReturnStatement()));
                        });
                statements.Add(statementToAdd);
            }

            var throwStatementSyntax = SyntaxFactory.ThrowStatement(
                SyntaxFactory.ObjectCreationExpression(
                        SyntaxFactory.IdentifierName("System.Exception"))
                    .WithArgumentList(
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.StringLiteralExpression,
                                        SyntaxFactory.Literal("Invalid " + doClassName + " type")))))));

            statements.Add(throwStatementSyntax);

            return SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.IdentifierName("void"),
                    SyntaxFactory.Identifier("Match"))
                .WithModifiers(
                    SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithParameterList(
                    parameterListSyntax)
                .WithBody(
                    SyntaxFactory.Block(
                        statements.ToArray()));
        }
    }
}
