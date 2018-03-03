using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace DataObjectHelper.Tests
{
    [TestFixture]
    public class CreateMatchMethodsTests
    {
        private static string createMatchMethodsAttributeCode;
        private static string sumTypeClassCode;

        static CreateMatchMethodsTests()
        {
            createMatchMethodsAttributeCode = 
@"public class CreateMatchMethodsAttribute : Attribute
{
    public CreateMatchMethodsAttribute(params Type[] types)
    {
        Types = types;
    }

    public Type[] Types { get; }
}";
            sumTypeClassCode = 
@"public abstract class SumType
{
    public class Option1 : SumType{}

    public class Option2 : SumType{}
}";
        }

        [Test]
        public void TestCreateMatchMethodsWhereClassIsInsideANamespace()
        {
            //Arrange
            var methodsClassCode =
@"[CreateMatchMethods(typeof(SumType))]
public static class Methods
{

}";

            var expectedMethodsClassCodeAfterRefactoring = 
@"[CreateMatchMethods(typeof(SumType))]
public static class Methods
{
    public static TResult Match<TResult>(this Namespace1.SumType sumType, System.Func<Namespace1.SumType.Option1, TResult> option1Case, System.Func<Namespace1.SumType.Option2, TResult> option2Case)
    {
        if (sumType is Namespace1.SumType.Option1 option1)
            return option1Case(option1);
        if (sumType is Namespace1.SumType.Option2 option2)
            return option2Case(option2);
        throw new System.Exception(""Invalid SumType type"");
    }

    public static void Match(this Namespace1.SumType sumType, System.Action<Namespace1.SumType.Option1> option1Case, System.Action<Namespace1.SumType.Option2> option2Case)
    {
        if (sumType is Namespace1.SumType.Option1 option1)
        {
            option1Case(option1);
            return;
        }

        if (sumType is Namespace1.SumType.Option2 option2)
        {
            option2Case(option2);
            return;
        }

        throw new System.Exception(""Invalid SumType type"");
    }
}";
            var content =
                InNamespace(
                    MergeParts(createMatchMethodsAttributeCode, sumTypeClassCode, methodsClassCode),
                    "Namespace1");

            var expectedContentAfterRefactoring =
                NormalizeCode(
                InNamespace(
                    MergeParts(createMatchMethodsAttributeCode, sumTypeClassCode, expectedMethodsClassCodeAfterRefactoring),
                    "Namespace1"));

            //Act
            var actualContentAfterRefactoring = NormalizeCode(ApplyRefactoring(content, SelectSpanWhereCreateMatchMethodsAttributeIsApplied));

            //Assert
            Assert.AreEqual(expectedContentAfterRefactoring, actualContentAfterRefactoring);
        }


        [Test]
        public void TestCreateMatchMethodsWhereClassIsNotInsideANamespace()
        {
            //Arrange
            var methodsClassCode =
@"[CreateMatchMethods(typeof(SumType))]
public static class Methods
{

}";

            var expectedMethodsClassCodeAfterRefactoring =
@"[CreateMatchMethods(typeof(SumType))]
public static class Methods
{
    public static TResult Match<TResult>(this SumType sumType, System.Func<SumType.Option1, TResult> option1Case, System.Func<SumType.Option2, TResult> option2Case)
    {
        if (sumType is SumType.Option1 option1)
            return option1Case(option1);
        if (sumType is SumType.Option2 option2)
            return option2Case(option2);
        throw new System.Exception(""Invalid SumType type"");
    }

    public static void Match(this SumType sumType, System.Action<SumType.Option1> option1Case, System.Action<SumType.Option2> option2Case)
    {
        if (sumType is SumType.Option1 option1)
        {
            option1Case(option1);
            return;
        }

        if (sumType is SumType.Option2 option2)
        {
            option2Case(option2);
            return;
        }

        throw new System.Exception(""Invalid SumType type"");
    }
}";
            var content =
                MergeParts(createMatchMethodsAttributeCode, sumTypeClassCode, methodsClassCode);

            var expectedContentAfterRefactoring =
                NormalizeCode(
                    MergeParts(createMatchMethodsAttributeCode, sumTypeClassCode, expectedMethodsClassCodeAfterRefactoring));

            //Act
            var actualContentAfterRefactoring = NormalizeCode(ApplyRefactoring(content, SelectSpanWhereCreateMatchMethodsAttributeIsApplied));

            //Assert
            Assert.AreEqual(expectedContentAfterRefactoring, actualContentAfterRefactoring);
        }

        public string NormalizeCode(string code)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var newRoot = syntaxTree.GetRoot().NormalizeWhitespace();

            return newRoot.ToString();
        }

        public string MergeParts(params string[] parts)
        {
            return string.Join(Environment.NewLine, parts);
        }

        public string InNamespace(string content, string @namespace)
        {
            return $@"namespace {@namespace}
{{
    {@content}
}}";
        }

        private static TextSpan SelectSpanWhereCreateMatchMethodsAttributeIsApplied(SyntaxNode rootNode)
        {
            return rootNode.DescendantNodes()
                .OfType<AttributeSyntax>()
                .Single(x => x.Name is SimpleNameSyntax name && name.Identifier.Text == "CreateMatchMethods")
                .Span;
        }

        private static string ApplyRefactoring(string content, Func<SyntaxNode, TextSpan> spanSelector)
        {
            var workspace = new AdhocWorkspace();

            var solution = workspace.CurrentSolution;

            var projectId = ProjectId.CreateNewId();

            solution = AddNewProjectToWorkspace(solution, "NewProject", projectId);

            var documentId = DocumentId.CreateNewId(projectId);

            solution = AddNewSourceFile(solution, content, "NewFile.cs", documentId);

            var document = solution.GetDocument(documentId);

            var syntaxNode = document.GetSyntaxRootAsync().Result;

            var span = spanSelector(syntaxNode);

            var refactoringActions = new List<CodeAction>();

            var refactoringContext =
                new CodeRefactoringContext(
                    document,
                    span,
                    action => refactoringActions.Add(action),
                    CancellationToken.None);

            var sut = new DataObjectHelperCodeRefactoringProvider();

            sut.ComputeRefactoringsAsync(refactoringContext).Wait();
            
            refactoringActions.ForEach(action =>
            {
                var operations = action.GetOperationsAsync(CancellationToken.None).Result;

                foreach (var operation in operations)
                {
                    operation.Apply(workspace, CancellationToken.None);
                }
            });
            
            var updatedDocument = workspace.CurrentSolution.GetDocument(documentId);

            return updatedDocument.GetSyntaxRootAsync().Result.GetText().ToString();
        }

        private static Solution AddNewSourceFile(
            Solution solution,
            string fileContent,
            string fileName,
            DocumentId documentId)
        {
            return solution.AddDocument(documentId, fileName, SourceText.From(fileContent));
        }

        private static Solution AddNewProjectToWorkspace(Solution solution, string projName, ProjectId projectId)
        {
            MetadataReference csharpSymbolsReference = MetadataReference.CreateFromFile(typeof(CSharpCompilation).Assembly.Location);
            MetadataReference codeAnalysisReference = MetadataReference.CreateFromFile(typeof(Compilation).Assembly.Location);

            MetadataReference corlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            MetadataReference systemCoreReference = MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);

            return
                solution.AddProject(
                    ProjectInfo.Create(
                        projectId,
                        VersionStamp.Create(),
                        projName,
                        projName,
                        LanguageNames.CSharp)
                        .WithMetadataReferences(new[]
                        {
                            corlibReference,
                            systemCoreReference,
                            csharpSymbolsReference,
                            codeAnalysisReference
                        }));
        }
    }
}
