#r @"akka\Akka\System.Collections.Immutable.dll"
#r @"packages\Newtonsoft.Json\lib\net45\Newtonsoft.Json.dll"
#r @"packages\FsPickler\lib\net45\FsPickler.dll"
#r @"akka\Akka.Streams\System.Reactive.Streams.dll"
#r @"akka\Akka.Streams\Akka.dll"
#r @"akka\Akka.Streams\Akka.Streams.dll"
#r @"akka\Akka.FSharp\Akka.FSharp.dll"

open Akka.FSharp
open Akka.Streams
open Akka.Streams.Dsl
open System
open Microsoft.FSharp.Core.Printf
open System.Collections.Concurrent

[<AutoOpen>]
module Utils =
    let (=>) (src: Source<_,_>) flow = src.Via(flow)
    let (=>|) (src: Source<_,_>) sink = src.To(sink)
    let print fmt = kprintf (printf "%O %s" DateTime.Now) fmt

let serviceState = ConcurrentDictionary<string, int>()
let totalCount = ref 0

let callService url input =
    async {
        incr totalCount
        if !totalCount = 30 then failwith "Failing as it's 30s call."
        let _ = serviceState.AddOrUpdate(url, (fun _ -> 1), (fun _ count -> count + 1))
        Console.WriteLine (print "Calling %s with %s. State: %A" url input (serviceState |> Seq.toList))
        do! match url with
            | "http://m1" -> Async.Sleep 100
            | _ -> Async.Sleep 1000
        let _ = serviceState.AddOrUpdate(url, (fun _ -> 1), (fun _ count -> count - 1))
        return input
    }

let process' machine input =
    let url = sprintf "http://%s" machine
    async {
        return! callService url input
    }

let distribute (flows: Flow<_, _, _>[]) =
    GraphDsl.Create(fun builder ->
        let balancer = builder.Add(Balance<_>(flows.Length))
        let merger = builder.Add(Merge<_>(flows.Length))
        
        flows |> Array.iteri (fun i flow ->
            builder.From(balancer.Out i).Via(flow).To(merger.In i) |> ignore)
        
        FlowShape(balancer.In, merger.Out)
    )

let system = Configuration.defaultConfig() |> System.create "test"
let materializer = 
    ActorMaterializer.Create(
        system, 
        ActorMaterializerSettings.Create(system).WithSupervisionStrategy(Supervision.Deciders.ResumingDecider))

let renditions (machines: string[]) (inputs: string[]) =
    let workerCount = 4

    let machineFlows =
        [| for machine in machines do
             yield Flow.Create<_>().MapAsyncUnordered(
                     workerCount, 
                     fun x -> Async.StartAsTask(process' machine x)) |]
    
    Source
        .From(inputs)
        .Via(distribute machineFlows)
        .RunFold([], (fun acc x -> x :: acc), materializer)
        .Result

renditions [|"m1"; "m2"|] (Array.init 200 string)
|> List.rev
|> List.iter (printfn "%A")