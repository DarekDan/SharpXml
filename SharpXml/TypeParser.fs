﻿namespace SharpXml

open System
open System.Collections.Generic

type ParserFunc = delegate of string -> obj

module TypeParser =

    type XmlElem =
        | SingleElem of string
        | ContentElem of string * string
        | GroupElem of string * XmlElem list

    type ParseState =
        | Start
        | TagStart
        | TagName of int
        | InTag of string * bool

    let whitespaceChars =
        let whitespace = [| ' '; '\t'; '\r'; '\n' |]
        let max =  Array.max whitespace |> int
        Array.init (max+1) (fun c -> Array.exists ((=) (char c)) whitespace)

    let parseRawString (input : string) =
        box input

    let isWhitespace (c : char) =
        let i = int c
        i < whitespaceChars.Length && whitespaceChars.[i]

    let rec skipWhitespace (input : string) index =
        if index >= input.Length || not (isWhitespace input.[index]) then index
        else skipWhitespace input (index + 1)

    let eatTag (input : string) index =
        let start = skipWhitespace input index
        let len = input.Length
        let rec inner i state =
            let next = i + 1
            if next > len then 0, null, false else
                match state with
                | Start ->
                    if input.[i] = '<' then inner next TagStart else inner next state
                | TagStart ->
                    if isWhitespace input.[i] then
                        inner next state
                    else
                        inner next (TagName i)
                | TagName s ->
                    if isWhitespace input.[i] then
                        let tag = input.Substring(s, (i-s))
                        inner next (InTag (tag, false))
                    elif input.[i] = '/' then
                        let tag = input.Substring(s, (i-s))
                        inner next (InTag (tag, true))
                    elif input.[i] = '>' then
                        let tag = input.Substring(s, (i-s))
                        i, tag, false
                    else
                        inner next state
                | InTag (tag, isSingle) ->
                    if input.[i] = '>' then
                        i, tag, isSingle
                    elif input.[i] = '/' then
                        inner next (InTag(tag, true))
                    else inner next state
        inner start Start

    let eatContent (input : string) index =
        let start = index
        let len = input.Length
        let replace (f : string) (t : string) (i : string) =
            i.Replace(f, t)
        let rec inner i =
            let next = i + 1
            // end of string, this is probably an error
            if next > len then input.Substring(start), len
            elif input.[i] = '<' then
                let endIndex = i - start
                input.Substring(start, endIndex), endIndex
            else inner next
        // TODO: this replacements probably could be done more performant,
        // like while doing the search for the end tag
        let result, index = inner index
        result |> replace "&gt;" ">" |> replace "&lt;" "<", index

    let parseAST (input : string) index =
        let db = System.Diagnostics.Debug.WriteLine
        let len = input.Length
        let rec inner i level elements =
            sprintf "Inner %d %d %A" i level elements |> db
            let next = i + 1
            if level = 0 || next >= len then elements, next
            else
                let current = skipWhitespace input i
                match eatTag input current with
                | x, _, _ when (x+1) >= len -> elements, x
                | _, null, _ -> failwith "Unable to read XML tag"
                | x, name, false ->
                    if input.[x+1] = '<' then
                        let elems, endIndex = inner (x+1) 1 []
                        inner endIndex (level + 1) (GroupElem(name, elems) :: elements)
                    else
                        // start of plain content
                        let content, index = eatContent input (x+1)
                        let endIndex, _, _ = eatTag input index
                        inner endIndex (level - 1) (ContentElem(name, content) :: elements)
                | x, name, true ->
                    inner x level (SingleElem name :: elements)
        if input.[index] <> '<' then failwith "XML content does not start with '<'"
        inner index 1 []

