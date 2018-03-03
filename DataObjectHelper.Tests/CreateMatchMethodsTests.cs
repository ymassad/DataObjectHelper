using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataObjectHelper.Tests.FSharpProject;
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
        private static string genericSumTypeClassCode;
        private static string sumTypeClassWithSubClassesInOuterScopeCode;
        private static string genericSumTypeClassWithSubClassesInOuterScopeCode;

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

            sumTypeClassWithSubClassesInOuterScopeCode =
@"public abstract class SumType{}

public class Option1 : SumType{}

public class Option2 : SumType{}";



            genericSumTypeClassCode =
@"public abstract class GenericSumType<T>
{
    public class Option1 : GenericSumType<T>{}

    public class Option2 : GenericSumType<T>{}
}";


            genericSumTypeClassWithSubClassesInOuterScopeCode =
@"public abstract class GenericSumType<T>{}

public class Option1<T1> : GenericSumType<T1>{}

public class Option2<T2> : GenericSumType<T2>{}
";

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
    public static TResult Match<TResult>(this Namespace1.SumType instance, System.Func<Namespace1.SumType.Option1, TResult> option1Case, System.Func<Namespace1.SumType.Option2, TResult> option2Case)
    {
        if (instance is Namespace1.SumType.Option1 option1)
            return option1Case(option1);
        if (instance is Namespace1.SumType.Option2 option2)
            return option2Case(option2);
        throw new System.Exception(""Invalid SumType type"");
    }

    public static void Match(this Namespace1.SumType instance, System.Action<Namespace1.SumType.Option1> option1Case, System.Action<Namespace1.SumType.Option2> option2Case)
    {
        if (instance is Namespace1.SumType.Option1 option1)
        {
            option1Case(option1);
            return;
        }

        if (instance is Namespace1.SumType.Option2 option2)
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
    public static TResult Match<TResult>(this SumType instance, System.Func<SumType.Option1, TResult> option1Case, System.Func<SumType.Option2, TResult> option2Case)
    {
        if (instance is SumType.Option1 option1)
            return option1Case(option1);
        if (instance is SumType.Option2 option2)
            return option2Case(option2);
        throw new System.Exception(""Invalid SumType type"");
    }

    public static void Match(this SumType instance, System.Action<SumType.Option1> option1Case, System.Action<SumType.Option2> option2Case)
    {
        if (instance is SumType.Option1 option1)
        {
            option1Case(option1);
            return;
        }

        if (instance is SumType.Option2 option2)
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

        [Test]
        public void TestCreateMatchMethodsWhereSubclassesAreInOuterScope()
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
    public static TResult Match<TResult>(this SumType instance, System.Func<Option1, TResult> option1Case, System.Func<Option2, TResult> option2Case)
    {
        if (instance is Option1 option1)
            return option1Case(option1);
        if (instance is Option2 option2)
            return option2Case(option2);
        throw new System.Exception(""Invalid SumType type"");
    }

    public static void Match(this SumType instance, System.Action<Option1> option1Case, System.Action<Option2> option2Case)
    {
        if (instance is Option1 option1)
        {
            option1Case(option1);
            return;
        }

        if (instance is Option2 option2)
        {
            option2Case(option2);
            return;
        }

        throw new System.Exception(""Invalid SumType type"");
    }
}";
            var content =
                MergeParts(createMatchMethodsAttributeCode, sumTypeClassWithSubClassesInOuterScopeCode, methodsClassCode);

            var expectedContentAfterRefactoring =
                NormalizeCode(
                    MergeParts(createMatchMethodsAttributeCode, sumTypeClassWithSubClassesInOuterScopeCode, expectedMethodsClassCodeAfterRefactoring));

            //Act
            var actualContentAfterRefactoring = NormalizeCode(ApplyRefactoring(content, SelectSpanWhereCreateMatchMethodsAttributeIsApplied));

            //Assert
            Assert.AreEqual(expectedContentAfterRefactoring, actualContentAfterRefactoring);
        }


        [Test]
        public void TestCreateMatchMethodsForOpenGenericClass()
        {
            //Arrange
            var methodsClassCode =
@"[CreateMatchMethods(typeof(GenericSumType<>))]
public static class Methods
{

}";

            var expectedMethodsClassCodeAfterRefactoring =
@"[CreateMatchMethods(typeof(GenericSumType<>))]
public static class Methods
{
    public static TResult Match<T, TResult>(this GenericSumType<T> instance, System.Func<GenericSumType<T>.Option1, TResult> option1Case, System.Func<GenericSumType<T>.Option2, TResult> option2Case)
    {
        if (instance is GenericSumType<T>.Option1 option1)
            return option1Case(option1);
        if (instance is GenericSumType<T>.Option2 option2)
            return option2Case(option2);
        throw new System.Exception(""Invalid GenericSumType type"");
    }

    public static void Match<T>(this GenericSumType<T> instance, System.Action<GenericSumType<T>.Option1> option1Case, System.Action<GenericSumType<T>.Option2> option2Case)
    {
        if (instance is GenericSumType<T>.Option1 option1)
        {
            option1Case(option1);
            return;
        }

        if (instance is GenericSumType<T>.Option2 option2)
        {
            option2Case(option2);
            return;
        }

        throw new System.Exception(""Invalid GenericSumType type"");
    }
}";
            var content =
                MergeParts(createMatchMethodsAttributeCode, genericSumTypeClassCode, methodsClassCode);

            var expectedContentAfterRefactoring =
                NormalizeCode(
                    MergeParts(createMatchMethodsAttributeCode, genericSumTypeClassCode, expectedMethodsClassCodeAfterRefactoring));

            //Act
            var actualContentAfterRefactoring = NormalizeCode(ApplyRefactoring(content, SelectSpanWhereCreateMatchMethodsAttributeIsApplied));

            //Assert
            Assert.AreEqual(expectedContentAfterRefactoring, actualContentAfterRefactoring);
        }

        [Test]
        public void TestCreateMatchMethodsForClosedGenericClass()
        {
            //Arrange
            var methodsClassCode =
@"[CreateMatchMethods(typeof(GenericSumType<System.String>))]
public static class Methods
{

}";

            var expectedMethodsClassCodeAfterRefactoring =
@"[CreateMatchMethods(typeof(GenericSumType<System.String>))]
public static class Methods
{
    public static TResult Match<TResult>(this GenericSumType<System.String> instance, System.Func<GenericSumType<System.String>.Option1, TResult> option1Case, System.Func<GenericSumType<System.String>.Option2, TResult> option2Case)
    {
        if (instance is GenericSumType<System.String>.Option1 option1)
            return option1Case(option1);
        if (instance is GenericSumType<System.String>.Option2 option2)
            return option2Case(option2);
        throw new System.Exception(""Invalid GenericSumType type"");
    }

    public static void Match(this GenericSumType<System.String> instance, System.Action<GenericSumType<System.String>.Option1> option1Case, System.Action<GenericSumType<System.String>.Option2> option2Case)
    {
        if (instance is GenericSumType<System.String>.Option1 option1)
        {
            option1Case(option1);
            return;
        }

        if (instance is GenericSumType<System.String>.Option2 option2)
        {
            option2Case(option2);
            return;
        }

        throw new System.Exception(""Invalid GenericSumType type"");
    }
}";
            var content =
                MergeParts(createMatchMethodsAttributeCode, genericSumTypeClassCode, methodsClassCode);

            var expectedContentAfterRefactoring =
                NormalizeCode(
                    MergeParts(createMatchMethodsAttributeCode, genericSumTypeClassCode, expectedMethodsClassCodeAfterRefactoring));

            //Act
            var actualContentAfterRefactoring = NormalizeCode(ApplyRefactoring(content, SelectSpanWhereCreateMatchMethodsAttributeIsApplied));

            //Assert
            Assert.AreEqual(expectedContentAfterRefactoring, actualContentAfterRefactoring);
        }

        [Test]
        public void TestCreateMatchMethodsForOpenGenericClassWhoseSubclassesAreInOuterScope()
        {
            //Arrange
            var methodsClassCode =
@"[CreateMatchMethods(typeof(GenericSumType<>))]
public static class Methods
{

}";

            var expectedMethodsClassCodeAfterRefactoring =
@"[CreateMatchMethods(typeof(GenericSumType<>))]
public static class Methods
{
    public static TResult Match<T, TResult>(this GenericSumType<T> instance, System.Func<Option1<T>, TResult> option1Case, System.Func<Option2<T>, TResult> option2Case)
    {
        if (instance is Option1<T> option1)
            return option1Case(option1);
        if (instance is Option2<T> option2)
            return option2Case(option2);
        throw new System.Exception(""Invalid GenericSumType type"");
    }

    public static void Match<T>(this GenericSumType<T> instance, System.Action<Option1<T>> option1Case, System.Action<Option2<T>> option2Case)
    {
        if (instance is Option1<T> option1)
        {
            option1Case(option1);
            return;
        }

        if (instance is Option2<T> option2)
        {
            option2Case(option2);
            return;
        }

        throw new System.Exception(""Invalid GenericSumType type"");
    }
}";
            var content =
                MergeParts(createMatchMethodsAttributeCode, genericSumTypeClassWithSubClassesInOuterScopeCode, methodsClassCode);

            var expectedContentAfterRefactoring =
                NormalizeCode(
                    MergeParts(createMatchMethodsAttributeCode, genericSumTypeClassWithSubClassesInOuterScopeCode, expectedMethodsClassCodeAfterRefactoring));

            //Act
            var actualContentAfterRefactoring = NormalizeCode(ApplyRefactoring(content, SelectSpanWhereCreateMatchMethodsAttributeIsApplied));

            //Assert
            Assert.AreEqual(expectedContentAfterRefactoring, actualContentAfterRefactoring);
        }

        [Test]
        public void TestCreateMatchMethodsForClosedGenericClassWhoseSubclassesAreInOuterScope()
        {
            //Arrange
            var methodsClassCode =
@"[CreateMatchMethods(typeof(GenericSumType<System.String>))]
public static class Methods
{

}";

            var expectedMethodsClassCodeAfterRefactoring =
@"[CreateMatchMethods(typeof(GenericSumType<System.String>))]
public static class Methods
{
    public static TResult Match<TResult>(this GenericSumType<System.String> instance, System.Func<Option1<System.String>, TResult> option1Case, System.Func<Option2<System.String>, TResult> option2Case)
    {
        if (instance is Option1<System.String> option1)
            return option1Case(option1);
        if (instance is Option2<System.String> option2)
            return option2Case(option2);
        throw new System.Exception(""Invalid GenericSumType type"");
    }

    public static void Match(this GenericSumType<System.String> instance, System.Action<Option1<System.String>> option1Case, System.Action<Option2<System.String>> option2Case)
    {
        if (instance is Option1<System.String> option1)
        {
            option1Case(option1);
            return;
        }

        if (instance is Option2<System.String> option2)
        {
            option2Case(option2);
            return;
        }

        throw new System.Exception(""Invalid GenericSumType type"");
    }
}";
            var content =
                MergeParts(createMatchMethodsAttributeCode, genericSumTypeClassWithSubClassesInOuterScopeCode, methodsClassCode);

            var expectedContentAfterRefactoring =
                NormalizeCode(
                    MergeParts(createMatchMethodsAttributeCode, genericSumTypeClassWithSubClassesInOuterScopeCode, expectedMethodsClassCodeAfterRefactoring));

            //Act
            var actualContentAfterRefactoring = NormalizeCode(ApplyRefactoring(content, SelectSpanWhereCreateMatchMethodsAttributeIsApplied));

            //Assert
            Assert.AreEqual(expectedContentAfterRefactoring, actualContentAfterRefactoring);
        }

        [Test]
        public void TestCreateMatchMethodsForFSharpDiscriminatedUnion()
        {
            //Arrange
            var methodsClassCode =
@"[CreateMatchMethods(typeof(DataObjectHelper.Tests.FSharpProject.Module.FSharpDiscriminatedUnion))]
public static class Methods
{

}";

            var expectedMethodsClassCodeAfterRefactoring =
@"[CreateMatchMethods(typeof (DataObjectHelper.Tests.FSharpProject.Module.FSharpDiscriminatedUnion))]
public static class Methods
{
    public static TResult Match<TResult>(
        this DataObjectHelper.Tests.FSharpProject.Module.FSharpDiscriminatedUnion instance,
        System.Func<DataObjectHelper.Tests.FSharpProject.Module.FSharpDiscriminatedUnion.Option1, TResult> option1Case,
        System.Func<TResult> option2Case)
    {
        if (instance is DataObjectHelper.Tests.FSharpProject.Module.FSharpDiscriminatedUnion.Option1 option1)
            return option1Case(option1);
        if (instance.IsOption2)
            return option2Case();
        throw new System.Exception(""Invalid FSharpDiscriminatedUnion type"");
    }

    public static void Match(
        this DataObjectHelper.Tests.FSharpProject.Module.FSharpDiscriminatedUnion instance,
        System.Action<DataObjectHelper.Tests.FSharpProject.Module.FSharpDiscriminatedUnion.Option1> option1Case,
        System.Action option2Case)
    {
        if (instance is DataObjectHelper.Tests.FSharpProject.Module.FSharpDiscriminatedUnion.Option1 option1)
        {
            option1Case(option1);
            return;
        }

        if (instance.IsOption2)
        {
            option2Case();
            return;
        }

        throw new System.Exception(""Invalid FSharpDiscriminatedUnion type"");
    }
}";
            var content =
                MergeParts(createMatchMethodsAttributeCode, methodsClassCode);

            var expectedContentAfterRefactoring =
                NormalizeCode(
                    MergeParts(createMatchMethodsAttributeCode, expectedMethodsClassCodeAfterRefactoring));

            //Act
            var actualContentAfterRefactoring = NormalizeCode(
                ApplyRefactoring(
                    content,
                    SelectSpanWhereCreateMatchMethodsAttributeIsApplied,
                    GetFsharpTestProjectReference()));

            //Assert
            Assert.AreEqual(expectedContentAfterRefactoring, actualContentAfterRefactoring);
        }

        [Test]
        public void TestCreateMatchMethodsForOpenGenericFSharpDiscriminatedUnion()
        {
            //Arrange
            var methodsClassCode =
@"[CreateMatchMethods(typeof(DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<>))]
public static class Methods
{

}";

            var expectedMethodsClassCodeAfterRefactoring =
@"[CreateMatchMethods(typeof (DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<>))]
public static class Methods
{
    public static TResult Match<a, TResult>(
        this DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<a> instance,
        System.Func<DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<a>.Option1, TResult> option1Case,
        System.Func<TResult> option2Case)
    {
        if (instance is DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<a>.Option1 option1)
            return option1Case(option1);
        if (instance.IsOption2)
            return option2Case();
        throw new System.Exception(""Invalid GenericFSharpDiscriminatedUnion type"");
    }

    public static void Match<a>(
        this DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<a> instance,
        System.Action<DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<a>.Option1> option1Case,
        System.Action option2Case)
    {
        if (instance is DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<a>.Option1 option1)
        {
            option1Case(option1);
            return;
        }

        if (instance.IsOption2)
        {
            option2Case();
            return;
        }

        throw new System.Exception(""Invalid GenericFSharpDiscriminatedUnion type"");
    }
}";
            var content =
                MergeParts(createMatchMethodsAttributeCode, methodsClassCode);

            var expectedContentAfterRefactoring =
                NormalizeCode(
                    MergeParts(createMatchMethodsAttributeCode, expectedMethodsClassCodeAfterRefactoring));

            //Act
            var actualContentAfterRefactoring = NormalizeCode(
                ApplyRefactoring(
                    content,
                    SelectSpanWhereCreateMatchMethodsAttributeIsApplied,
                    GetFsharpTestProjectReference()));

            //Assert
            Assert.AreEqual(expectedContentAfterRefactoring, actualContentAfterRefactoring);
        }

        [Test]
        public void TestCreateMatchMethodsForClosedGenericFSharpDiscriminatedUnion()
        {
            //Arrange
            var methodsClassCode =
@"[CreateMatchMethods(typeof(DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<System.String>))]
public static class Methods
{

}";

            var expectedMethodsClassCodeAfterRefactoring =
@"[CreateMatchMethods(typeof (DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<System.String>))]
public static class Methods
{
    public static TResult Match<TResult>(
        this DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<System.String> instance,
        System.Func<DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<System.String>.Option1, TResult> option1Case,
        System.Func<TResult> option2Case)
    {
        if (instance is DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<System.String>.Option1 option1)
            return option1Case(option1);
        if (instance.IsOption2)
            return option2Case();
        throw new System.Exception(""Invalid GenericFSharpDiscriminatedUnion type"");
    }

    public static void Match(
        this DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<System.String> instance,
        System.Action<DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<System.String>.Option1> option1Case,
        System.Action option2Case)
    {
        if (instance is DataObjectHelper.Tests.FSharpProject.Module.GenericFSharpDiscriminatedUnion<System.String>.Option1 option1)
        {
            option1Case(option1);
            return;
        }

        if (instance.IsOption2)
        {
            option2Case();
            return;
        }

        throw new System.Exception(""Invalid GenericFSharpDiscriminatedUnion type"");
    }
}";
            var content =
                MergeParts(createMatchMethodsAttributeCode, methodsClassCode);

            var expectedContentAfterRefactoring =
                NormalizeCode(
                    MergeParts(createMatchMethodsAttributeCode, expectedMethodsClassCodeAfterRefactoring));

            //Act
            var actualContentAfterRefactoring = NormalizeCode(
                ApplyRefactoring(
                    content,
                    SelectSpanWhereCreateMatchMethodsAttributeIsApplied,
                    GetFsharpTestProjectReference()));

            //Assert
            Assert.AreEqual(expectedContentAfterRefactoring, actualContentAfterRefactoring);
        }

        private static PortableExecutableReference GetFsharpTestProjectReference()
        {
            return MetadataReference.CreateFromFile(typeof(Module.FSharpDiscriminatedUnion).Assembly.Location);
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

        private static string ApplyRefactoring(string content, Func<SyntaxNode, TextSpan> spanSelector, params MetadataReference[] additionalReferences)
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
