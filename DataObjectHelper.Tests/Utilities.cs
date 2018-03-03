using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataObjectHelper.Tests.FSharpProject;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DataObjectHelper.Tests
{
    public static class Utilities
    {
        public static PortableExecutableReference GetFsharpTestProjectReference()
        {
            return MetadataReference.CreateFromFile(typeof(Module.FSharpDiscriminatedUnion).Assembly.Location);
        }

        public static string NormalizeCode(string code)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var newRoot = syntaxTree.GetRoot().NormalizeWhitespace();

            return newRoot.ToString();
        }

        public static string MergeParts(params string[] parts)
        {
            return String.Join(Environment.NewLine, parts);
        }

        public static string InNamespace(string content, string @namespace)
        {
            return $@"namespace {@namespace}
{{
    {content}
}}";
        }

        public static string ApplyRefactoring(string content, Func<SyntaxNode, TextSpan> spanSelector, params MetadataReference[] additionalReferences)
        {
            var workspace = new AdhocWorkspace();

            var solution = workspace.CurrentSolution;

            var projectId = ProjectId.CreateNewId();

            solution = AddNewProjectToWorkspace(solution, "NewProject", projectId, additionalReferences);

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

        private static Solution AddNewProjectToWorkspace(
            Solution solution, string projName, ProjectId projectId, params MetadataReference[] additionalReferences)
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
                        }.Concat(additionalReferences).ToArray()));
        }
    }
}
