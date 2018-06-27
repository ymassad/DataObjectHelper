using System;
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

            var doTypeSymbols = GetDataObjectTypesToCreateMatchMethodsFor(attributeSyntax, semanticModel);

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

        public static async Task<Solution> CreateMatchMethods(
            Document document, ClassDeclarationSyntax classSyntax, SyntaxNode root, CancellationToken cancellationToken)
        {
            AttributeSyntax CreateMatchMethodAttribute(SyntaxAnnotation syntaxAnnotation)
            {
                return SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName("CreateMatchMethods"),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SeparatedList(new[]
                        {
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.TypeOfExpression(
                                    SyntaxFactory.IdentifierName(classSyntax.Identifier.Text))),
                        }))).WithAdditionalAnnotations(syntaxAnnotation);
            }

            var originalSolution = document.Project.Solution;

            var staticExtensionsClassName = classSyntax.Identifier.Text + "ExtensionMethods";

            var annotationForAttributeSyntax = new SyntaxAnnotation();


            var hostClass =
                classSyntax.Parent.ChildNodes().OfType<ClassDeclarationSyntax>()
                    .Where(x => x.IsStatic() && x.Identifier.Text == staticExtensionsClassName)
                    .FirstOrNoValue();

            if (hostClass.HasNoValue)
            {
                var newHostClass =
                    Utilities
                        .CreateEmptyPublicStaticClass(staticExtensionsClassName)
                        .WithAttributeLists(SyntaxFactory.List(new[]
                        {
                            SyntaxFactory.AttributeList(SyntaxFactory.SeparatedList(new[]
                            {
                                CreateMatchMethodAttribute(annotationForAttributeSyntax),
                            }))
                        }));

                var newSolutionWithHostClassAdded = originalSolution.WithDocumentSyntaxRoot(document.Id,
                    root.InsertNodesAfter(classSyntax, new[] { newHostClass }));

                var newDocument = newSolutionWithHostClassAdded.GetDocument(document.Id);

                var newDocumentRoot = await newDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                return await CreateMatchMethods(
                    newDocument,
                    (AttributeSyntax)newDocumentRoot.GetAnnotatedNodes(annotationForAttributeSyntax).Single(),
                    newDocumentRoot,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var hostClassValue = hostClass.GetValue();
                var existingAttribute = hostClassValue.AttributeLists.SelectMany(x => x.Attributes)
                    .FirstOrNoValue(Utilities.IsCreateMatchMethodsAttribute);

                if (existingAttribute.HasNoValue)
                {
                    var updatedHostClass = hostClassValue.AddAttributeLists(SyntaxFactory.AttributeList(
                        SyntaxFactory.SeparatedList(new[]
                        {
                            CreateMatchMethodAttribute(annotationForAttributeSyntax),
                        })));


                    var newSolutionWithHostClassAdded = originalSolution.WithDocumentSyntaxRoot(document.Id,
                        root.ReplaceNode(hostClassValue, updatedHostClass));

                    var newDocument = newSolutionWithHostClassAdded.GetDocument(document.Id);

                    var newDocumentRoot = await newDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                    return await CreateMatchMethods(
                        newDocument,
                        (AttributeSyntax)newDocumentRoot.GetAnnotatedNodes(annotationForAttributeSyntax).Single(),
                        newDocumentRoot,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    return await CreateMatchMethods(
                        document,
                        existingAttribute.GetValue(),
                        root,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }


        private static Maybe<INamedTypeSymbol[]> GetDataObjectTypesToCreateMatchMethodsFor(
            AttributeSyntax attributeSyntax,
            SemanticModel semanticModel)
        {
            return Utilities.GetDataObjectTypesSpecifiedInAttribute(attributeSyntax)
                .ChainValue(types =>
                    types
                        .Select(type =>
                            semanticModel.GetSymbolInfo(type).Symbol
                                .TryCast().To<INamedTypeSymbol>()
                                .If(x => x.TypeKind == TypeKind.Class)
                                .ChainValue(x => GetDataObjectsTypes(x)))
                        .Traverse()
                        .ChainValue(x => x.SelectMany(y => y)))
                .ChainValue(x => x.ToArray());
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
                return new[] { Utilities.GetFullConstructedForm(@class) };
            }
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
                        .Select(x => CreateClassCase(doTypeSymbol, x).ChainValue(c => (Case)c))
                        .ItemsWithValue()
                        .ToArray()
                        .ToMaybe()
                        .If(x => x.Length > 0);
                });
        }

        private static Maybe<Case.ClassCase> CreateClassCase(
            INamedTypeSymbol doClassSymbol,
            INamedTypeSymbol subClassSymbol)
        {
            var containingType = subClassSymbol.ContainingType;

            var openDoClassSymbol = doClassSymbol.IsGenericType ? doClassSymbol.ConstructedFrom : doClassSymbol;

            if (containingType == null)
            {
                if (subClassSymbol.TypeParameters.Length != doClassSymbol.TypeParameters.Length)
                    return Maybe.NoValue;

                var baseTypeOfSubClass = subClassSymbol.BaseType;

                if(baseTypeOfSubClass.IsGenericType && baseTypeOfSubClass.TypeArguments.Any(x => x.Kind != SymbolKind.TypeParameter))
                    return Maybe.NoValue;

                if(!doClassSymbol.IsGenericType || doClassSymbol.IsUnboundGenericType)
                    return new Case.ClassCase(subClassSymbol);

                return new Case.ClassCase(subClassSymbol.Construct(doClassSymbol.TypeArguments.ToArray()));

            }

            if (containingType.Equals(openDoClassSymbol))
            {
                if (HasTypeParameters(subClassSymbol))
                    return Maybe.NoValue;

                if(!openDoClassSymbol.IsGenericType || openDoClassSymbol.IsUnboundGenericType)
                    return new Case.ClassCase(subClassSymbol);


                var namedTypeSymbol = doClassSymbol.GetMembers()
                    .OfType<INamedTypeSymbol>().Single(x => x.OriginalDefinition.Equals(subClassSymbol));
                return new Case.ClassCase(
                    namedTypeSymbol);
            }

            return Maybe.NoValue;
        }

        private static bool HasTypeParameters(INamedTypeSymbol x)
        {
            return x.TypeParameters.Length != 0;
        }

        public static MethodDeclarationSyntax CreateMatchMethod(
            INamedTypeSymbol doTypeSymbol,
            Case[] cases)
        {
            var doClassName = doTypeSymbol.Name;

            var doClassFullname = Utilities.GetFullName(doTypeSymbol);

            var classFullname = doClassFullname;

            var firstParameterName = "instance";

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
                                    Utilities.CreateSimpleMemberAccessSyntax(firstParameterName, "Is" + fsharpNullCase.Name),
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

            var method =
                SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.IdentifierName("TResult"),
                    SyntaxFactory.Identifier("Match"))
                .WithModifiers(
                    SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithParameterList(
                    parameterListSyntax)
                .WithBody(
                    SyntaxFactory.Block(
                        statements.ToArray()));

            List<TypeParameterSyntax> typeParameters = new List<TypeParameterSyntax>();

            if (doTypeSymbol.IsGenericType)
            {
                var openTypeParams = Utilities.GetOpenTypeParameters(doTypeSymbol);

                if (openTypeParams.Any())
                {
                    typeParameters.AddRange(
                        openTypeParams
                            .Select(x => SyntaxFactory.TypeParameter(SyntaxFactory.Identifier(x.Name))));
                }
            }

            typeParameters.Add(
                SyntaxFactory.TypeParameter(
                    SyntaxFactory.Identifier("TResult")));

            method = method.WithTypeParameterList(
                SyntaxFactory.TypeParameterList(
                        SyntaxFactory.SeparatedList(
                            typeParameters)));

            return method;
        }

        public static MethodDeclarationSyntax CreateMatchMethodThatReturnsVoid(
            INamedTypeSymbol doTypeSymbol,
            Case[] cases)
        {
            var doClassName = doTypeSymbol.Name;

            var doClassFullname = Utilities.GetFullName(doTypeSymbol);

            var classFullname = doClassFullname;

            var firstParameterName = "instance";

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
                                    Utilities.CreateSimpleMemberAccessSyntax(firstParameterName, "Is" + fsharpNullCase.Name),
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

            var method =
                SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.IdentifierName("void"),
                    SyntaxFactory.Identifier("Match"))
                .WithModifiers(
                    SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithParameterList(
                    parameterListSyntax)
                .WithBody(
                    SyntaxFactory.Block(
                        statements.ToArray()));

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
