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
open System.Text.RegularExpressions 
open System.IO
open System.Threading.Tasks
open System.Threading

let regex = Regex(@"\{.*(?<name>Microsoft.*)\|\]", RegexOptions.ExplicitCapture ||| RegexOptions.Compiled)
let normalizeName (name: string) = String.Join(".", name.Split([|';'; '"'; ' '|], StringSplitOptions.RemoveEmptyEntries))

let materializer = Configuration.defaultConfig() |> System.create "test" |> ActorMaterializer.Create

let searchWithAkka (file: string) =
    use reader = new StreamReader(file)
    Source
        .UnfoldAsync(0, fun _ -> reader.ReadLineAsync().ContinueWith(fun (x: Task<string>) -> 0, x.Result))
        .Take(1000000L)
        .MapAsyncUnordered(4, fun line ->
            Task.Factory.StartNew(fun _ ->
                match regex.Match line with
                | m when m.Success -> m.Groups.["name"].Value
                | _ -> ""))
        .Filter(fun x -> x <> "")
        .MapAsyncUnordered(4, fun (name: string) -> Task.Factory.StartNew(fun _ -> normalizeName name))
        .RunFold(ResizeArray(), (fun acc x -> acc.Add x; acc), materializer)
        .Result
        .Count

searchWithAkka @"d:\downloads\big.txt"

// Real: 00:00:17.407, CPU: 00:01:45.640, GC gen0: 361, gen1: 66, gen2: 1
// val it : int = 24540

let searchWithAkkaOpt (file: string) =
    let completion = TaskCompletionSource<string>()
    let task = Source.FromTask completion.Task
    use reader = new StreamReader(file)
    let unfold = Source.UnfoldAsync(0, fun _ -> reader.ReadLineAsync().ContinueWith(fun (x: Task<string>) -> 0, x.Result))
    let source = task.Concat unfold
    let materialized = source.Take 1000000L
    Thread.Sleep 500
    completion.SetResult ""
    
    materialized
        .MapAsyncUnordered(4, fun line ->
            Task.Factory.StartNew(fun _ ->
                match regex.Match line with
                | m when m.Success -> m.Groups.["name"].Value
                | _ -> ""))
        .Filter(fun x -> x <> "")
        .MapAsyncUnordered(4, fun (name: string) -> Task.Factory.StartNew(fun _ -> normalizeName name))
        .RunFold(ResizeArray(), (fun acc x -> acc.Add x; acc), materializer)
        .Result
        .Count

searchWithAkkaOpt @"d:\downloads\big.txt"

// Real: 00:00:14.033, CPU: 00:01:29.250, GC gen0: 318, gen1: 69, gen2: 0
// val it : int = 25275


#r @"packages\FsPickler\lib\net45\FsPickler.dll"
#r @"packages\Hopac\lib\net45\Hopac.Platform.dll"
#r @"packages\Hopac\lib\net45\Hopac.Core.dll"
#r @"packages\Hopac\lib\net45\Hopac.dll"

open System.IO
open Hopac
open Hopac.Infixes
open Hopac.Stream

module Stream =
    let inline (>=>.) a b = a >=> fun _ -> b

    let rec fromUntil ch event =
             ch ^-> fun x -> Cons (x, fromUntil ch event)
        <|>* event

    let conMapFun degree x2y xs =
        let inn = Ch()
        let finish = IVar()
        let allDone = Latch.Now.create 1
        let dec = Latch.decrement allDone
        let out = Ch()
        
        xs
        |> Stream.iterJob (fun x ->
              Latch.Now.increment allDone
              Ch.give inn x)
        >>=. dec
        |> start
        
        Job.tryWith
            (inn >>= (x2y >> Ch.give out >=>. dec) |> Job.forever)
            (IVar.fillFailure finish >=> Job.abort)
        |> Job.server
        |> Job.forN degree
        |> start
        
        allDone ^=>. IVar.tryFill finish Nil |> start
        fromUntil out finish

let searchWithHopac (file: string) =
    Job.using <| new StreamReader(file) <| fun reader ->
        Stream.indefinitely <| job { return! reader.ReadLineAsync() }
        |> Stream.take 1000000L
        |> Stream.conMapFun 4
            (regex.Match >> function
              | m when m.Success -> Some m.Groups.["name"].Value
              | _ -> None)
        |> Stream.choose
        |> Stream.conMapFun 4 normalizeName
        |> Stream.count

searchWithHopac @"d:\downloads\big.txt" |> run

// Real: 00:00:10.870, CPU: 00:00:59.687, GC gen0: 360, gen1: 65, gen2: 4
// val it : int64 = 24540L

#r @"packages\FSharp.Control.AsyncSeq\lib\net45\FSharp.Control.AsyncSeq.dll"

open FSharp.Control

let searchWithAsyncSeq (file: string) =
    use reader = new StreamReader(file)
    AsyncSeq.unfoldAsync (fun _ -> 
        async {
            let! line = reader.ReadLineAsync() |> Async.AwaitTask
            return Some (line, ())
        }) ()
    |> AsyncSeq.take 3000
    |> AsyncSeq.chooseAsync (fun line ->
         async {
            match regex.Match line with
            | m when m.Success -> return Some m.Groups.["name"].Value
            | _ -> return None
         })
    |> AsyncSeq.mapAsync (fun (name: string) -> async { return normalizeName name })
    |> AsyncSeq.length
    |> Async.RunSynchronously

searchWithAsyncSeq @"d:\downloads\big.txt"
