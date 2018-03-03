namespace DataObjectHelper.Tests.FSharpProject

module Module =
    type FSharpDiscriminatedUnion = Option1 of Age : int * Name: string | Option2

    type GenericFSharpDiscriminatedUnion<'a> = Option1 of Age : int * Name: 'a | Option2

    type FSharpRecord = {Age : int; Name : string}

    type GenericFSharpRecord<'a> = {Age : int; Name : 'a}