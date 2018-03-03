using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace DataObjectHelper.Tests
{
    [TestFixture]
    public class CreateWithMethodsTests
    {
        private static string createWithMethodsAttributeCode;
        private static string productTypeClassCode;
        private static string genericProductTypeClassCode;

        static CreateWithMethodsTests()
        {
            createWithMethodsAttributeCode =
@"public class CreateWithMethodsAttribute : Attribute
{
    public CreateWithMethodsAttribute(params Type[] types)
    {
        Types = types;
    }

    public Type[] Types { get; }
}";
            productTypeClassCode =
@"public class ProductType
{
    public int Age {get;}

    public string Name {get;}

    public ProductType(int age, string name)
    {
        Age = age;
        Name = name;
    }
}";

            genericProductTypeClassCode =
@"public class GenericProductType<TName>
{
    public int Age {get;}

    public TName Name {get;}

    public GenericProductType(int age, TName name)
    {
        Age = age;
        Name = name;
    }
}";
        }

        [Test]
        public void TestCreateWithMethodsWhereClassIsInsideANamespace()
        {
            //Arrange
            var methodsClassCode =
                @"[CreateWithMethods(typeof(ProductType))]
public static class Methods
{

}";

            var expectedMethodsClassCodeAfterRefactoring =
@"[CreateWithMethods(typeof(ProductType))]
public static class Methods
{
    public static Namespace1.ProductType WithAge(this Namespace1.ProductType instance, System.Int32 newValue)
    {
        return new Namespace1.ProductType(age: newValue, name: instance.Name);
    }

    public static Namespace1.ProductType WithName(this Namespace1.ProductType instance, System.String newValue)
    {
        return new Namespace1.ProductType(age: instance.Age, name: newValue);
    }
}";
            var content =
                Utilities.InNamespace(
                    Utilities.MergeParts(
                        createWithMethodsAttributeCode, productTypeClassCode, methodsClassCode),
                    "Namespace1");

            var expectedContentAfterRefactoring =
                Utilities.NormalizeCode(
                    Utilities.InNamespace(
                        Utilities.MergeParts(
                            createWithMethodsAttributeCode,
                            productTypeClassCode,
                            expectedMethodsClassCodeAfterRefactoring),
                        "Namespace1"));

            //Act
            var actualContentAfterRefactoring =
                Utilities.NormalizeCode(
                    Utilities.ApplyRefactoring(
                        content,
                        SelectSpanWhereCreateWithMethodsAttributeIsApplied));

            //Assert
            Assert.AreEqual(expectedContentAfterRefactoring, actualContentAfterRefactoring);
        }

        [Test]
        public void TestCreateWithMethodsWhereClassIsInTheGlobalNamespace()
        {
            //Arrange
            var methodsClassCode =
                @"[CreateWithMethods(typeof(ProductType))]
public static class Methods
{

}";

            var expectedMethodsClassCodeAfterRefactoring =
@"[CreateWithMethods(typeof(ProductType))]
public static class Methods
{
    public static ProductType WithAge(this ProductType instance, System.Int32 newValue)
    {
        return new ProductType(age: newValue, name: instance.Name);
    }

    public static ProductType WithName(this ProductType instance, System.String newValue)
    {
        return new ProductType(age: instance.Age, name: newValue);
    }
}";
            var content =
                Utilities.MergeParts(
                    createWithMethodsAttributeCode, productTypeClassCode, methodsClassCode);

            var expectedContentAfterRefactoring =
                Utilities.NormalizeCode(
                    Utilities.MergeParts(
                        createWithMethodsAttributeCode,
                        productTypeClassCode,
                        expectedMethodsClassCodeAfterRefactoring));

            //Act
            var actualContentAfterRefactoring =
                Utilities.NormalizeCode(
                    Utilities.ApplyRefactoring(
                        content,
                        SelectSpanWhereCreateWithMethodsAttributeIsApplied));

            //Assert
            Assert.AreEqual(expectedContentAfterRefactoring, actualContentAfterRefactoring);
        }


        [Test]
        public void TestCreateWithMethodsForOpenGenericDataObject()
        {
            //Arrange
            var methodsClassCode =
@"[CreateWithMethods(typeof(GenericProductType<>))]
public static class Methods
{

}";

            var expectedMethodsClassCodeAfterRefactoring =
@"[CreateWithMethods(typeof(GenericProductType<>))]
public static class Methods
{
    public static GenericProductType<TName> WithAge<TName>(this GenericProductType<TName> instance, System.Int32 newValue)
    {
        return new GenericProductType<TName>(age: newValue, name: instance.Name);
    }

    public static GenericProductType<TName> WithName<TName>(this GenericProductType<TName> instance, TName newValue)
    {
        return new GenericProductType<TName>(age: instance.Age, name: newValue);
    }
}";
            var content =
                Utilities.MergeParts(
                    createWithMethodsAttributeCode, genericProductTypeClassCode, methodsClassCode);

            var expectedContentAfterRefactoring =
                Utilities.NormalizeCode(
                    Utilities.MergeParts(
                        createWithMethodsAttributeCode,
                        genericProductTypeClassCode,
                        expectedMethodsClassCodeAfterRefactoring));

            //Act
            var actualContentAfterRefactoring =
                Utilities.NormalizeCode(
                    Utilities.ApplyRefactoring(
                        content,
                        SelectSpanWhereCreateWithMethodsAttributeIsApplied));

            //Assert
            Assert.AreEqual(expectedContentAfterRefactoring, actualContentAfterRefactoring);
        }


        private static TextSpan SelectSpanWhereCreateWithMethodsAttributeIsApplied(SyntaxNode rootNode)
        {
            return rootNode.DescendantNodes()
                .OfType<AttributeSyntax>()
                .Single(x => x.Name is SimpleNameSyntax name && name.Identifier.Text == "CreateWithMethods")
                .Span;
        }
    }
}
