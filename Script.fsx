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
open System.Diagnostics
open System.Net
open System
open Microsoft.FSharp.Core.Printf
open System.Threading.Tasks

type RUnit = System.Reactive.Streams.Unit

let rec fib n = if n < 2 then n else fib (n - 1) + fib (n - 2)
let tc f =
    let sw = Stopwatch.StartNew()
    f() |> ignore
    sw.Stop()
    printfn "Elapsed %O" sw.Elapsed

module Source =
    let inline ofSeq x = Source.From x
    let inline buffer size strategy (src: Source<_,_>) = src.Buffer(size, strategy)
    let inline map (mapper: 'a -> 'b) (src: Source<_,_>) = src.Map(fun x -> mapper x)

    let inline mapAsync parallelism (mapper: 'a -> Async<'b>) (src: Source<_,_>) = 
        src.MapAsync(parallelism, fun x -> Async.StartAsTask (mapper x))
    
    let inline runForeach materializer (action: 'a -> unit) (src: Source<_,_>) =
        src.RunForeach(System.Action<_> action, materializer) |> Async.AwaitTask

    let inline named name (src: Source<_,_>) = src.Named name

[<AutoOpen>]
module Utils =
    let (=>) (src: Source<_,_>) flow = src.Via(flow)
    let (=>|) (src: Source<_,_>) sink = src.To(sink)
    let print fmt = kprintf (printfn "%O %s" DateTime.Now) fmt

let system = System.create "test" (Configuration.defaultConfig())
let materializer = ActorMaterializer.Create system

let source =
    ["google.com"; "ya.ru"; "microsoft.com"; "jet.com"; "ycombinator.com"]
    |> Source.ofSeq
    |> Source.map (fun x -> Uri ("http://" + x))
   
let download (uri: Uri) : Task<Uri * string> =
    async {
        use client = new WebClient()
        let! content = client.AsyncDownloadString uri
        return uri, content
    } |> Async.StartAsTask

let graph = 
    source
        => Flow.Create<_>().MapAsyncUnordered (10, fun x -> download x)
        => Flow.Create<_>().Map(fun (uri, content) -> print "%O, %d" uri content.Length; uri)
        =>| Sink.Ignore()

//source.RunForeach((fun uri -> print "%O" uri), materializer)

graph.Run(materializer)

//["google.com"; "ya.ru"; "microsoft.com"; "jet.com"; "ycombinator.com"]
//|> Source.ofSeq
//|> Source.map (fun x -> Uri ("http://" + x))
//|> Source.mapAsync 10 (fun url -> 
//    async {
//        use client = new WebClient()
//        let! content = client.AsyncDownloadString url
//        return url, content
//    })
//|> Source.map (fun x -> printfn "%O %O downloaded" DateTime.Now (fst x); x)
//|> Source.buffer 3 OverflowStrategy.Backpressure
//|> Source.runForeach materializer (fun (url, content) ->
//    printfn "%O %O => %d bytes" DateTime.Now url content.Length
//    Thread.Sleep 5000)
//|> Async.RunSynchronously

//tc (fun _ -> 
//    [0..42]
//    |> Source.ofSeq
//    |> Source.buffer 10 OverflowStrategy.Backpressure
//    |> Source.mapAsync 4 (fun x -> async { return x, fib x })
//    |> Source.runForeach materializer (fun (x, fib) -> printfn "fib %d = %d" x fib)
//    |> Async.RunSynchronously
//    )
