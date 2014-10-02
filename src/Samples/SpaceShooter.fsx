#I "../../bin"
#r "FSharp.Actor.dll"
open System
open FSharp.Actor

ActorHost.Start().SubscribeEvents(fun (evnt:ActorEvent) -> printfn "%A" evnt) 

type Weapon = 
    | Cannon
    | Missile
    with 
        member x.Damage
            with get() = 
                match x with
                | Cannon -> 4
                | Missile -> 10

type SpaceshipAction = 
    | MoveLeft 
    | MoveRight
    | MoveUp 
    | MoveDown
    | Fire of Weapon
    | TakeHit of Weapon

type Position = {
    X: int; Y: int
}

type Spaceship = {
    Id : Guid
    Position : Position
    Arsenal : Map<Weapon, int>
    Armour : int
    Shield : int
}
with
    static member Empty
        with get() =
            {
                Id = Guid.NewGuid()
                Position = { X = 0; Y = 0 } 
                Arsenal = Map [Cannon, 500;  Missile, 3] 
                Armour = 100
                Shield = 100
            }
    member x.IsDestroyed = x.Armour <= 0

type ItemClass = 
    | Ship
    | Projectile

type UniverseAction =
    | ItemUpdate of Guid * (ItemClass * Position)
    | ItemDestroyed of Guid

type UniverseState = {
    Items : Map<Guid, (ItemClass * Position) ref> 
}
with 
    static member Empty with get() = { Items = Map.empty }

let universe = 
    actor { 
        name "universe"
        body (
            let rec loop (state:UniverseState) = messageHandler {
                let! msg = Message.receive None
                let state =
                    match msg with
                    | ItemUpdate(id, x) ->
                        match Map.tryFind id state.Items with
                        | Some(v) ->  v := x; state
                        | None -> { state with Items = Map.add id (ref x) state.Items}
                    | ItemDestroyed(id) -> 
                        { state with Items = Map.remove id state.Items }    
                printfn "%A" state.Items
                return! loop state
            }
            loop UniverseState.Empty
        )
    } |> Actor.spawn

let spaceship = 
    actor {
        name "spaceship"
        body (
            let universe = !~"universe"
            let rec loop (state:Spaceship) = messageHandler {
                let! msg = Message.receive None
                let state =
                    match msg with
                    | MoveLeft -> { state with Position = { state.Position with X = (max 0 (state.Position.X - 1)) }}
                    | MoveRight -> { state with Position = { state.Position with X = (max 0 (state.Position.X + 1)) }}
                    | MoveUp -> { state with Position = { state.Position with Y = (max 0 (state.Position.Y + 1)) }}
                    | MoveDown -> { state with Position = { state.Position with Y = (max 0 (state.Position.Y - 1)) }}
                    | Fire weapon -> 
                        match state.Arsenal.[weapon] with
                        | 0 -> state
                        | count -> { state with Arsenal = Map.add weapon (count - 1) state.Arsenal }
                    | TakeHit weapon ->
                        if state.Shield = 0
                        then { state with Armour = max 0 (state.Armour - weapon.Damage) }
                        else { state with Shield = max 0 (state.Shield - weapon.Damage) }
                  
                if state.IsDestroyed
                then failwithf "Ship destroyed"
                else do! Message.post universe (ItemUpdate(state.Id, (Ship, state.Position)))
                return! loop state
            }
            loop Spaceship.Empty)
    } |> Actor.spawn

let rnd = new System.Random()
let cts = new System.Threading.CancellationTokenSource()
let rec gameLoop() = async {
    let action =
        match rnd.Next(0, 7) with
        | 0 -> MoveLeft 
        | 1 -> MoveRight
        | 2 -> MoveUp 
        | 3 -> MoveDown
        | 4 -> Fire Missile
        | 5 -> Fire Cannon
        | 6 -> TakeHit Cannon
        | 7 -> TakeHit Missile
        | _ -> failwithf "WTF!!"
    spaceship <-- action
    do! Async.Sleep(150)
    return! gameLoop()
}

let start() = 
    Async.Start(gameLoop(), cts.Token)

let stop() = 
    cts.Cancel()