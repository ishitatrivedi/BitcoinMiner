#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.TestKit" 
#r "nuget: Akka.Remote"


open System
open System.Threading
open Akka.Actor
open Akka.Configuration
open Akka.FSharp 
open System.Security.Cryptography

let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            log-config-on-start : on
            stdout-loglevel : DEBUG
            loglevel : ERROR
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                debug : {
                    receive : on
                    autoreceive : on
                    lifecycle : on
                    event-stream : on
                    unhandled : on
                }
            }
            remote {
                helios.tcp {
                    port = 8777
                    hostname = localhost
                }
            }
        }")

let mutable count=0L //to keep track of the workers
let workers = 30L

let system = ActorSystem.Create("RemoteFSharp", configuration)
let mutable ref = null

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

        
let compute (x:int64, workerNo:int64, inputval:int64,zerostr:string)=
    let ebs= randomStr(x|>int, workerNo|>int)
    let id="itrivedi" + ebs     
    let z= hashfunc(id)
    let m =  inputval- 1L
    let firstN = z.[0 .. m|>int]
    if firstN.Equals(zerostr) then
        printfn "Bitcoin found String= %s \t Hash= %s" id z

//union of messages to an actor
type ActorMsg =
    | WorkerMsg of int64*int64*string
    | BossMsg of int64*string
    | EndMsg of int64

let bitCoinMiners (mailbox:Actor<_>)=
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | WorkerMsg(workerNo,inputval,zerostr) -> 
                        for i in 1 .. 10000 do
                            for j in 10L .. 20L do
                                compute (j,workerNo,inputval,zerostr) 
                                                    
                        mailbox.Sender()<! EndMsg(workerNo) //send back the finish message to boss
        | _ -> printfn "Worker Received Wrong message"
    }
    loop()

let workersList=[for a in 1L .. workers do yield(spawn system ("Job" + (string a)) bitCoinMiners)]

let Boss (mailbox:Actor<_>) =
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | BossMsg(inputval,zerostr) ->
                                printfn"Message received from Boss"
                                for i in 0L .. (workers-1L) do //distributing work to the workers
                                    printfn "Assigning work to worker %d" i
                                    workersList.Item(i|>int) <! WorkerMsg(i,inputval,zerostr) //sending message to worker
                                            
        | EndMsg(workerid) ->   count <- count+1L //recieves end msg from worker
                                if count = workers then //checking if all workers have already sent the end message
                                        mailbox.Context.System.Terminate() |> ignore //terminating the actor system
        | _ -> printfn "Boss Received Wrong message"
        return! loop()
    }
    loop()
let localBossRef = spawn system "Boss" Boss

//BossRef <! BossMsg(inputZero)
let commlink = 
    spawn system "server"
    <| fun mailbox ->
        let rec loop() =
            actor {
                let! msg = mailbox.Receive()
                printfn "%s" msg 
                let command = (msg|>string).Split ','
                if command.[0].CompareTo("Job")=0 then
                    
                    localBossRef <! BossMsg(command.[1]|>int64,command.[2])
                    ref <- mailbox.Sender()

                return! loop() 
            }
        loop()


system.WhenTerminated.Wait()

//system.Terminate()