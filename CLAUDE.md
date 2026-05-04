# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

TankDestroyer is a turn-based tank battle game where AI bots compete on grid maps. It has three client modes (console, Godot 3D, headless benchmark) and a pluggable bot system. The primary development target is the bot AI.

## Commands

```bash
# Build everything
dotnet build

# Run the interactive console game (bot + map selection menu)
cd TankDestroyer.Game.Console && dotnet run

# Run the headless benchmark runner
cd TankDestroyer.Headless && dotnet run -- --games 100 --map SimpleMap

# Add a new bot project to the solution
./AddProjectsToSolution.sh
```

## Architecture

```
TankDestroyer.API          ← Public interfaces bots code against
TankDestroyer.Engine       ← Game logic (no UI dependencies)
TankDestroyer.Game.Console ← Interactive console client
TankDestroyer.Headless     ← Headless benchmark/training runner
Game/                      ← Godot 3D client (.NET module)
Bots/*/                    ← Bot implementations (one project each)
Maps/                      ← .map files (text format)
```

**Engine ↔ API separation:** The intended design is that bots only reference `TankDestroyer.API`. In practice, `Luc.Bot` also references `TankDestroyer.Engine` directly and hard-casts to concrete types (`Tank`, `Tile`, `Bullet`, `World`, `PlayerTurnContext`) to avoid the interface indirection. This is intentional for that project.

**GetTile coordinate order gotcha:** `World.GetTile(int x, int y)` takes x first. `ITurnContext.GetTile(int y, int x)` (and `PlayerTurnContext`) has the parameters named in the opposite order — calling through the interface with `(y, x)` ends up swapping coordinates in the implementation. In `Luc.Bot`, tile lookups go through `(World)ctx.World` directly using `world.GetTile(x, y)` to avoid this.

**Turn loop:** `GameRunner` calls each bot's `DoTurn(ITurnContext)` per turn. Bots call at most one action (`MoveTank`, `RotateTurret`, `Fire`) via `ITurnContext`. Multiple actions of the same type in one turn are silently discarded by `PreventMultipleActionsSameTurn`.

**Clients** (`Console`, `Headless`) discover bots and maps at runtime via reflection — any `IPlayerBot` implementation with a `[Bot]` attribute in a loaded assembly is available.

## Creating a bot

1. Create a new class library project under `Bots/YourName.Bot/`
2. Reference `TankDestroyer.API`
3. Implement:

```csharp
[Bot("Display Name", "Creator Name", "RRGGBB")]
public class MyBot : IPlayerBot
{
    public void DoTurn(ITurnContext ctx)
    {
        // ctx.Tank, ctx.GetTanks(), ctx.GetBullets()
        // ctx.MoveTank(Direction), ctx.RotateTurret(TurretDirection), ctx.Fire()
    }
}
```

4. Run `./AddProjectsToSolution.sh` to wire the project into the solution.

## Game rules (relevant for bot logic)

- **One action per turn**: move OR rotate OR fire (extras are dropped).
- **Movement**: blocked by water, buildings, other tanks. Sand and grass are passable. Trees are passable.
- **Firing**: blocked when standing on a tree. Allowed from buildings.
- **Bullet travel**: up to 6 cells in the turret's direction; stops on tree, building (not the origin tile), or tank.
- **Damage on hit**: 75% from grass/sand/water, 50% from building, 25% from tree (terrain under the *target* tank).
- **Win condition**: last tank standing; game ends immediately when ≤1 tank remains.
- `TurretDirection` has 8 values (N, NE, E, SE, S, SW, W, NW); `Direction` has 4 (Up, Down, Left, Right).

## LucBot training strategy (genetic algorithm)

LucBot (`Bots/Luc.Bot/`) is being evolved via a genetic algorithm using the headless runner as the fitness function. The overall flow:

1. **Genome** — a fixed-length `float[]` of heuristic weights (e.g. how much to value aim-on-target, cover, bullet evasion, closing distance, aggression).
2. **Heuristic bot** — LucBot scores every legal action each turn using a weighted formula driven by the genome, then picks the highest-scoring action.
3. **Fitness** — run N headless games vs. a reference opponent; win rate = fitness score.
4. **GA loop** (lives in a new `TankDestroyer.Trainer` project or extends Headless):
   - Initialise random population of genomes
   - Evaluate fitness for each genome
   - Keep top 50%, breed next generation via crossover + mutation
   - Repeat until convergence or iteration budget
5. **Export** — best genome is serialised to a JSON file and loaded by LucBot at runtime.

The trainer does **not** go through the bot plugin system; it instantiates `LucBot` directly with injected weights and calls `GameRunner` in-process.

## Map format

```
Name,Height,Width,SpawnX1;SpawnY1,SpawnX2;SpawnY2,...
<Height rows of Width tile codes separated by spaces>
```

Tile codes: `0` = Grass, `1` = Sand, `2` = Building, `3` = Tree, `4` = Water. Maps live in `Maps/*.map`.
