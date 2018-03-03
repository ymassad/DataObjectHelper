namespace DataObjectHelper.Tests.FSharpProject

module Module =
    type FSharpDiscriminatedUnion = Option1 of param : int | Option2

    type GenericFSharpDiscriminatedUnion<'a> = Option1 of param : 'a | Option2
