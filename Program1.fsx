#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.TestKit" 

open System
open System.Threading
open Akka.Actor
open Akka.Configuration
open Akka.FSharp 
open System.Security.Cryptography

let system = ActorSystem.Create("Master")

let mutable count = 0L //to keep track of the workers
let inputZero = fsi.CommandLineArgs.[1] |> int64
let mutable Zerostr = ""

for j in 1L .. inputZero do
    Zerostr <- Zerostr + "0"  

let mutable workers = 30L // No of workers to be created

if inputZero >5L then
    workers <- 100L

let hashfunc (ebs1: string) = 
    (System.Text.Encoding.ASCII.GetBytes(ebs1)) |> (new SHA256Managed()).ComputeHash |> Array.map (fun (x : byte) -> System.String.Format("{0:x2}", x)) |> String.concat String.Empty

let randomStr (len:int, wNo:int): string= 
    let chars = "ABCDEFGHIJKLMNOPQRSTUVWUXYZ0123456789"
    let charsLen = chars.Length
    let random = System.Random()
    let mutable index = 0
    if wNo > 36 then
        index <- wNo % 36
    else
        index <- wNo

    let randomChars = [|for i in 1..len -> chars.[random.Next(charsLen)]|]
    Array.set randomChars 0 (chars.[index])
    String(randomChars)

let compute (x:int64, workerNo:int64)=
    let ebs= randomStr(x|>int, workerNo|>int)
    let id="itrivedi" + ebs     
    let z= hashfunc(id)
    let m = inputZero - 1L
    let firstN = z.[0 .. m|>int]
    if firstN.Equals(Zerostr) then
        printfn "%s \t %s" id z

//Discriminated union of messages to an actor
type ActorMsg =
    | WorkerMsg of int64
    | BossMsg of int64
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

//boss actor- it distrbutes the tasks to workers
let Boss (mailbox:Actor<_>) =
    let rec loop()=actor{
        let! msg = mailbox.Receive()
        match msg with 
        | BossMsg(N) ->
                        let workersList=[for a in 1L .. workers do yield(spawn system ("Job" + (string a)) bitCoinMiners)] //creating workers
                        for i in 0L .. (workers-1L) do //distributing work to the workers
                            workersList.Item(i|>int) <! WorkerMsg(i) //sending message to worker
                                            
        | EndMsg(workerid) ->   count <- count+1L //recieves end msg from worker
                                if count = workers then //checking if all workers have already sent the end message
                                        mailbox.Context.System.Terminate() |> ignore //terminating the actor system
        | _ -> printfn "Boss Received Wrong message"
        return! loop()
    }
    loop()

//creating boss actor
let BossRef = spawn system "Boss" Boss

//sending message to boss actor
BossRef <! BossMsg(inputZero)
//waiting for boss actor to terminate the actor system
system.WhenTerminated.Wait()