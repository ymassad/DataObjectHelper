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

            var attributeNameSyntax = attributeSyntax
                .ChainValue(x => x.Name.TryCast().To<IdentifierNameSyntax>());

            if (attributeNameSyntax.HasValueAnd(x => x.Identifier.Text.EqualsAny("CreateMatchMethods", "CreateMatchMethodsAttribute")))
            {
                var action = CodeAction.Create(
                    "Create Match methods",
                    c => MatchCreation.CreateMatchMethods(context.Document, attributeSyntax.GetValue(), root, c));

                context.RegisterRefactoring(action);

            }

            if (attributeNameSyntax.HasValueAnd(x => x.Identifier.Text.EqualsAny("CreateWithMethods", "CreateWithMethodsAttribute")))
            {
                var action = CodeAction.Create(
                    "Create With methods",
                    c => WithMethodCreation.CreateWithMethods(context.Document, attributeSyntax.GetValue(), root, c));

                context.RegisterRefactoring(action);
            }
        }
    }
}
