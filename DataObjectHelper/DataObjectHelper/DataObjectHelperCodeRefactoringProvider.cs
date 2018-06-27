using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataObjectHelper
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(DataObjectHelperCodeRefactoringProvider)), Shared]
    public class DataObjectHelperCodeRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var node = root.FindNode(context.Span);

            var attributeSyntax = node.TryCast().To<AttributeSyntax>()
                .ValueOrMaybe(() => node.Parent.TryCast().To<AttributeSyntax>());

            if (attributeSyntax.HasValueAnd(Utilities.IsCreateMatchMethodsAttribute))
            {
                var action = CodeAction.Create(
                    "Create Match methods",
                    c => MatchCreation.CreateMatchMethods(context.Document, attributeSyntax.GetValue(), root, c));

                context.RegisterRefactoring(action);

            }

            if (attributeSyntax.HasValueAnd(Utilities.IsCreateWithMethodsAttribute))
            {
                var action = CodeAction.Create(
                    "Create With methods",
                    c => WithMethodCreation.CreateWithMethods(context.Document, attributeSyntax.GetValue(), root, c));

                context.RegisterRefactoring(action);
            }

            var classSyntax = node.TryCast().To<ClassDeclarationSyntax>().If(x => !x.IsStatic());

            if (classSyntax.HasValue)
            {
                var action = CodeAction.Create(
                    "Create Match methods",
                    c => MatchCreation.CreateMatchMethods(context.Document, classSyntax.GetValue(), root, c));

                context.RegisterRefactoring(action);

            }

        }
    }
}
