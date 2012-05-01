﻿namespace SharpXml

module Reflection =

    open System
    open System.Collections.Generic
    open System.Reflection
    open System.Reflection.Emit
    open System.Runtime.Serialization

    open SharpXml.Extensions

    type EmptyConstructor = delegate of unit -> obj
    type SetValueFunc = delegate of obj * obj -> unit
    type ParseFunc = delegate of string -> obj

    let publicFlags =
        BindingFlags.FlattenHierarchy |||
        BindingFlags.Public |||
        BindingFlags.Instance

    let getProps (t : Type) = t.GetProperties(publicFlags)

    let getInterfaceProperties (t : Type) =
        if not t.IsInterface then failwithf "Type '%s' is no interface type" t.FullName
        let map = HashSet<PropertyInfo>(getProps t)
        t.GetInterfaces()
        |> Array.map getProps
        |> Array.concat
        |> Array.iter (map.Add >> ignore)
        Seq.toArray map

    let getPublicProperties (t : Type) =
        if t.IsInterface then getInterfaceProperties t else getProps t

    let getSerializableProperties (t : Type) =
        if t.IsDTO() then
            getPublicProperties t
            |> Array.filter (fun p -> p.IsDataMember())
            |> Seq.ofArray
        else
            getPublicProperties t
            |> Array.filter (fun p ->
                p.GetGetMethod() <> null &&
                hasAttribute p "IgnoreDataMemberAttribute" |> not)
            |> Seq.ofArray

    let getEmptyConstructor (t : Type) =
        let ctor = t.GetConstructor(Type.EmptyTypes)
        if ctor <> null then
            let dm = DynamicMethod("CustomCtor", t, Type.EmptyTypes, t.Module, true)
            let il = dm.GetILGenerator()

            il.Emit(OpCodes.Nop)
            il.Emit(OpCodes.Newobj, ctor)
            il.Emit(OpCodes.Ret)

            dm.CreateDelegate(typeof<EmptyConstructor>) :?> EmptyConstructor
        else
            // this one is for types that do not have an empty constructor
            EmptyConstructor(fun () -> FormatterServices.GetUninitializedObject(t))

    let constructorCache = ref (Dictionary<Type, EmptyConstructor>())

    let getConstructorMethod (t : Type) =
        match (!constructorCache).TryGetValue t with
        | true, ctor -> ctor
        | _ ->
            let ctor = getEmptyConstructor t
            if ctor <> null then Atom.updateAtomDict constructorCache t ctor else null

    let constructorNameCache = ref (Dictionary<string, EmptyConstructor>())

    let getConstructorMethodByName (name : string) =
        match (!constructorNameCache).TryGetValue name with
        | true, ctor -> ctor
        | _ ->
            let ctor =
                match Assembly.findType name with
                | Some t -> getEmptyConstructor t
                | _ -> null
            if ctor <> null then Atom.updateAtomDict constructorNameCache name ctor else null

    let areStringOrValueTypes types =
        Seq.forall (fun t -> t = typeof<string> || t.IsValueType) types

    let defaultValueCache = ref (Dictionary<Type, obj>())

    let determineDefaultValue (t : Type) =
        if not t.IsValueType then null
        elif t.IsEnum then Enum.ToObject(t, 0) else
        match Type.GetTypeCode(t) with
        | TypeCode.Empty
        | TypeCode.DBNull
        | TypeCode.String -> null
        | TypeCode.Boolean -> box false
        | TypeCode.Byte -> box 0uy
        | TypeCode.Char -> box '\000'
        | TypeCode.DateTime -> box DateTime.MinValue
        | TypeCode.Decimal -> box 0m
        | TypeCode.Double -> box 0.0
        | TypeCode.Int16 -> box 0s
        | TypeCode.Int32 -> box 0l
        | TypeCode.Int64 -> box 0L
        | TypeCode.SByte -> box 0y
        | TypeCode.Single -> box 0.0f
        | TypeCode.UInt16 -> box 0us
        | TypeCode.UInt32 -> box 0ul
        | TypeCode.UInt64 -> box 0UL
        | TypeCode.Object
        | _ -> Activator.CreateInstance t

    let getDefaultValue (t : Type) =
        if not t.IsValueType then null else
        match (!defaultValueCache).TryGetValue t with
        | true, value -> value
        | _ ->
            let defVal = determineDefaultValue t
            Atom.updateAtomDict defaultValueCache t defVal

