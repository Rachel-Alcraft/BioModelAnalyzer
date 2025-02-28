// Copyright (c) Microsoft Research 2016
// License: MIT. See LICENSE
module CheckOperations

open System.IO
open System.Diagnostics
open FSharp.Collections.ParallelSeq
open Newtonsoft.Json.Linq

let bothContains (item1 : JToken, item2 : JToken) prop =
    item1.[prop] <> null && item2.[prop] <> null && item1.[prop].Type <> JTokenType.Null && item2.[prop].Type <> JTokenType.Null

let neitherContains (item1 : JToken, item2 : JToken) prop =
    (item1.[prop] = null || item1.[prop].Type = JTokenType.Null) || 
    (item2.[prop] = null || item2.[prop].Type = JTokenType.Null)

let equalOrMissing (item1 : JToken, item2 : JToken) prop =
    neitherContains (item1,item2) prop ||
    bothContains (item1,item2) prop && item1.[prop] = item2.[prop]

let compareLTLSimulationResults (exp:JToken) (act:JToken) =
    equalOrMissing (exp, act) "Status" &&
    equalOrMissing (exp, act) "Error"

let comparePolarityResults (exp:JToken) (act:JToken) =
    let both = bothContains (exp, act)
    let neither = neitherContains (exp, act)
    (neither "Item1" ||
        both "Item1" &&
        equalOrMissing (exp.["Item1"], act.["Item1"]) "Status" &&
        equalOrMissing (exp.["Item1"], act.["Item1"]) "Error")
    &&
    (neither "Item2" ||
        both "Item2" &&
        equalOrMissing (exp.["Item2"], act.["Item2"]) "Status" &&
        equalOrMissing (exp.["Item2"], act.["Item2"]) "Error")

let compareSimulationResults (exp:JToken) (act:JToken) =
    JToken.DeepEquals(exp.["Variables"], act.["Variables"]) &&
    JToken.DeepEquals(exp.["ErrorMessages"], act.["ErrorMessages"])

let compareAnalysisResults (exp:JToken) (act:JToken) =
    JToken.DeepEquals(exp.["Status"], act.["Status"]) &&
    JToken.DeepEquals(exp.["Error"], act.["Error"])

let compareFurtherTestingResults (exp:JToken) (act:JToken) =
    JToken.DeepEquals(exp.["CounterExamples"], act.["CounterExamples"]) &&
    JToken.DeepEquals(exp.["Error"], act.["Error"])


let checkSomeJobs (doJob : string -> string) (compare : JToken -> JToken -> bool) (responseSuffix : string) (jobs:string seq) =
    let outcome =
        jobs
        |> PSeq.map(fun fileName ->
            let dir = Path.GetDirectoryName(fileName)
            let file = Path.GetFileNameWithoutExtension(fileName)
            let jobName = file.Substring(0, file.Length - ".request".Length)
            Trace.WriteLine(sprintf "Starting job %s..." jobName)
            try
                let resp_json = doJob fileName
                Trace.WriteLine(sprintf "Job %s is done." jobName)

                let resp = JObject.Parse(resp_json)
                let expected = JObject.Parse(File.ReadAllText(Path.Combine(dir, sprintf "%s%s.response.json" jobName responseSuffix)))               
                
                if compare expected resp then None
                else failwithf "Response status for the job %s differs from expected" jobName
            with 
            | exn ->
                Trace.WriteLine(sprintf "Job %s failed: %A" jobName exn)
                Some (System.Exception(sprintf "Job %s failed" jobName,  exn)))

    match outcome |> Seq.choose id |> Seq.toList with
    | [] -> () // ok
    | failed -> 
        raise (System.AggregateException("Some jobs have failed", failed))

let checkJob (folder : string) (doJob : string -> string) (compare : JToken -> JToken -> bool) (responseSuffix : string) =
    Directory.EnumerateFiles(folder, "*.request.json")
    |> checkSomeJobs doJob compare responseSuffix


module Folders = 
    let LTLQueries = "LTLQueries"
    let Simulation = "Simulation"
    let Analysis = "Analysis"
    let CounterExamples = "CounterExamples"
