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
open System.Threading

type RUnit = System.Reactive.Streams.Unit

[<AutoOpen>]
module Utils =
    let (=>) (src: Source<_,_>) flow = src.Via(flow)
    let (=>|) (src: Source<_,_>) sink = src.To(sink)
    let print fmt = kprintf (printf "%O %s" DateTime.Now) fmt

let callService url input =
    async {
        Console.WriteLine (print "Calling %s with %s..." url input)
        do! Async.Sleep 5000
        return input
    }

let process' machine input =
    let url = sprintf "http://%s/webservice/endpoint" machine
    async {
        return! callService url input
    }

let distribute (workers: ('a -> Async<'b>)[]) =
    GraphDsl.Create(fun b ->
        let balancer = b.Add(Balance<_>(workers.Length))
        let merger = b.Add(Merge<_>(workers.Length))
        
        workers |> Array.iteri (fun i worker ->
            b.From(balancer.Out i).Via(Flow.Create<_>().MapAsync(1, fun x -> Async.StartAsTask(worker x))).To(merger.In i) |> ignore)
        
        FlowShape(balancer.In, merger.Out)
    )

//let parallel' (workerCount: int) (flow: Flow<'a, 'b, RUnit>) =
//    Array.init workerCount (fun _ -> flow) |> distribute

let system = System.create "test" (Configuration.defaultConfig())
let materializer = ActorMaterializer.Create system

let renditions (machines: string list) (inputs: string[]) =
    let workerCount = 4

    let flows =
        [| for machine in machines do
             for _ in 1..workerCount do
                 yield process' machine |]
    
    Source
        .From(inputs)
        .Via(distribute flows)
        .RunFold([], (fun acc x -> x :: acc), materializer)

renditions ["m1"; "m2"] (Array.init 20 string)