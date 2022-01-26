open System.Threading
#time "on"
#r "nuget: Akka"
#r "nuget: Akka.FSharp"
#r "nuget: Akka.TestKit"
#r "nuget: Akka.Remote"

open System
open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open Akka.TestKit
open System.Threading
open System.Security.Cryptography

let mutable count=0L //to keep track of the workers
let workers = 30L

let inputZero = fsi.CommandLineArgs.[1] |> int64
let s_port = fsi.CommandLineArgs.[2] |>string

let addr = "akka.tcp://RemoteFSharp@localhost:" + s_port + "/user/server"

let mutable Zerostr = ""
for j in 1L .. inputZero do
    Zerostr <- Zerostr + "0"
printfn "%s" Zerostr

let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            log-config-on-start : on
            stdout-loglevel : DEBUG
            loglevel : ERROR
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                
            }
            remote {
                helios.tcp {
                    port = 8778
                    hostname = localhost
                }
            }
        }")

let system = ActorSystem.Create("ClientFsharp", configuration)

let echoServer = 
    spawn system "EchoServer"
    <| fun mailbox ->
        let rec loop() =
            actor {
                let! message = mailbox.Receive()
                let sender = mailbox.Sender()
                match box message with
                | :? string -> 
                        printfn "super!"
                        sender <! sprintf "Hello %s remote" message
                        return! loop()
                | _ ->  failwith "unknown message"
            } 
        loop()

let hashfunc (ebs1: string) = 
    (System.Text.Encoding.ASCII.GetBytes(ebs1)) |> (new SHA256Managed()).ComputeHash |> Array.map (fun (x : byte) -> System.String.Format("{0:x2}", x)) |> String.concat String.Empty

let randomStr (len:int, wNo:int): string= 
    let chars = "ABCDEFGHIJKLMNOPQRSTUVWUXYZ0123456789"
    let charsLen = chars.Length
    let random = System.Random()
    //let str=[|chars.[wNo]|]
    //String(str)
    let randomChars = [|for i in 1..len -> chars.[random.Next(charsLen)]|]
    Array.set randomChars 0 (chars.[wNo])
    String(randomChars)

        
let compute (x:int64, workerNo:int64)=
    let ebs= randomStr(x|>int, workerNo|>int)
    let id="itrivedi" + ebs     
    let z= hashfunc(id)
    let m = inputZero - 1L
    let firstN = z.[0 .. m|>int]
    if firstN.Equals(Zerostr) then
        printfn "Bitcoin found String= %s \t Hash= %s" id z

//union of messages to an actor
type ActorMsg =
    | WorkerMsg of int64
    | BossMsg of int64
    |RemoteMsg of int64*string
    | EndMsg of int64

//worker actor
let bitCoinMiners (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | WorkerMsg(workerNo) -> 
                        for i in 1 .. 10000 do
                            for j in 10L .. 20L do
                                compute (j,workerNo) 
                                                    
                        mailbox.Sender()<! EndMsg(workerNo) //send back the finish message to boss
        | _ -> printfn "Worker Received Wrong message"
    }
    loop()

let mutable localWorkDone = false

//boss actor- it distrbutes the tasks to workers
let Boss (mailbox:Actor<_>) =
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | BossMsg(N) ->
                                let workersList=[for a in 1L .. workers do yield(spawn system ("Job" + (string a)) bitCoinMiners)] //creating workers
                                for i in 0L .. (workers-1L) do //distributing work to the workers
                                    printfn "Assigning work to worker %d" i
                                    workersList.Item(i|>int) <! WorkerMsg(i) //sending message to worker
                                            
        | EndMsg(workerid) ->   count <- count+1L //recieves end msg from worker
                                if count = workers then //checking if all workers have already sent the end message
                                        mailbox.Context.System.Terminate() |> ignore //terminating the actor system
        | _ -> printfn "Boss Received Wrong message"
        return! loop()
    }
    loop()

let mutable remoteWorkDone = false
let commlink = 
    spawn system "client"
    <| fun mailbox ->
        let rec loop() =
            actor {
                let! msg = mailbox.Receive()
                printfn "%s" msg 
                let response =msg|>string
                let command = (response).Split ','
                if command.[0].CompareTo("init")=0 then
                    let echoClient = system.ActorSelection(addr)
                    let msgToServer = "Job,"+ (inputZero|>string)+ ","+ Zerostr
                    echoClient <! msgToServer
                    let BossRef = spawn system "Boss" Boss
                    BossRef <! BossMsg(inputZero)
                if response.CompareTo("ProcessingDone")=0 then
                    system.Terminate() |> ignore
                    remoteWorkDone <- true
                else
                printfn "-%s-" msg

                return! loop() 
            }
        loop()



commlink <! "init"
while (not localWorkDone && not remoteWorkDone) do


system.WhenTerminated.Wait()