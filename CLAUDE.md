# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

TankDestroyer is a turn-based tank battle game where AI bots compete on grid maps. It has three client modes (console, Godot 3D, headless benchmark) and a pluggable bot system. The primary development target is the bot AI — specifically evolving `LucBot` via a genetic algorithm.

## Commands

```bash
# Build everything
dotnet build

# Run the interactive console game (bot + map selection menu)
cd TankDestroyer.Game.Console && dotnet run

# Run the headless benchmark runner
cd TankDestroyer.Headless && dotnet run -- --games 100 --map SimpleMap

# Run the GA trainer (release mode for speed)
cd TankDestroyer.Trainer && dotnet run -c Release

# Add a new bot project to the solution
./AddProjectsToSolution.sh
```

## Architecture

```
TankDestroyer.API          ← Public interfaces bots code against
TankDestroyer.Engine       ← Game logic (no UI dependencies)
TankDestroyer.Game.Console ← Interactive console client
TankDestroyer.Headless     ← Headless benchmark/training runner
TankDestroyer.Trainer      ← Genetic algorithm trainer for LucBot
Game/                      ← Godot 3D client (.NET module)
Bots/*/                    ← Bot implementations (one project each)
Maps/                      ← .map files (text format)
```

**Engine ↔ API separation:** The intended design is that bots only reference `TankDestroyer.API`. In practice, `Luc.Bot` also references `TankDestroyer.Engine` directly and hard-casts to concrete types (`Tank`, `Tile`, `Bullet`, `World`, `PlayerTurnContext`) to avoid the interface indirection. This is intentional for that project.

**GetTile coordinate order gotcha:** `World.GetTile(int x, int y)` takes x first. `ITurnContext.GetTile(int y, int x)` (and `PlayerTurnContext`) has the parameters named in the opposite order — calling through the interface with `(y, x)` ends up swapping coordinates. In `Luc.Bot`, tile lookups go through `(World)ctx.World` directly using `world.GetTile(x, y)`.

**Turn loop:** `GameRunner` calls each bot's `DoTurn(ITurnContext)` per turn. Bots call at most one action (`MoveTank`, `RotateTurret`, `Fire`) via `ITurnContext`. Multiple actions of the same type in one turn are silently discarded by `PreventMultipleActionsSameTurn`.

**`GameRunner.RecordHistory`** (default `true`): when `false`, skips per-turn `GameTurn`/`Tank`/`Bullet` clone allocations. The trainer sets this to `false` for a significant reduction in GC pressure. Console/Godot clients leave it `true`.

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

LucBot (`Bots/Luc.Bot/`) is evolved via a genetic algorithm in `TankDestroyer.Trainer`. Key files:

- `Bots/Luc.Bot/Genome.cs` — 8 float weights serialised to/from `best_genome.json`
- `Bots/Luc.Bot/HeuristicScorer.cs` — scores all 13 candidate actions per turn (4 moves, 8 rotations, fire) using the genome weights; picks the highest
- `TankDestroyer.Trainer/Program.cs` — GA loop

**GA loop:**
1. Population of 50 genomes evaluated in parallel (60 games each)
2. Games split: first 30 vs random bot (stable baseline), last 30 vs a genome from the historical pool (self-play pressure)
3. Fitness = win rate across all 60 games
4. Top 50% become parents; next generation bred via uniform crossover + Gaussian mutation; top 2 pass through unchanged (elitism)
5. If no improvement for 20 generations, inject fresh random genomes to restore diversity
6. Best genome saved to `Build/best_genome.json` whenever all-time fitness improves

**Warm start:** on launch the trainer checks for `Build/best_genome.json`. If found, seeds the population with mutated copies of it rather than random genomes — training compounds across runs.

**Fitness regime reset:** gen 1 is always evaluated vs random only (pool is empty). When the pool becomes non-empty (gen 2+), the fitness baseline resets because random-only scores and mixed scores are not comparable.

**Self-play pool:** stores up to 10 past best genomes. Each gen's second half of games is played against a randomly drawn pool member. Pool grows as new all-time bests are found.

LucBot loads `best_genome.json` from `AppDomain.CurrentDomain.BaseDirectory` at startup (falls back to hard-coded defaults if absent). The trainer saves to `Build/best_genome.json`, which is the same directory the console game runs from — so the trained genome is picked up automatically.

## Map format

```
Name,Height,Width,SpawnX1;SpawnY1,SpawnX2;SpawnY2,...
<Height rows of Width tile codes separated by spaces>
```

Tile codes: `0` = Grass, `1` = Sand, `2` = Building, `3` = Tree, `4` = Water. Maps live in `Maps/*.map`.
