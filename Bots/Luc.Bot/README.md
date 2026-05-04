# Officer K — LucBot

A tank bot for TankDestroyer, trained via a genetic algorithm.

## Goal

Instead of hand-coding decision logic, the bot's behaviour is driven by a set of **weights** (a genome). A genetic algorithm evolves those weights by running thousands of headless games and selecting for win rate.

## How it works

### Genome
A fixed-length `float[]` where each value is a weight for one heuristic signal, for example:

| Weight | What it measures |
|---|---|
| `AimBonus` | How well the turret is aimed at the nearest enemy |
| `DistancePenalty` | How close we are to an enemy (aggression vs. safety) |
| `CoverBonus` | Value of terrain under our tank (tree/building = cover) |
| `BulletThreat` | How directly an incoming bullet is headed toward us |
| `HealthFactor` | Scales aggressiveness based on remaining health |

### Decision making
Each turn, the bot scores all legal actions using the weighted formula and picks the best one. No randomness — same genome always produces the same decision in the same state.

### Training loop
1. Initialise a random population of genomes.
2. For each genome, run N headless games vs. a reference opponent and record win rate (= fitness).
3. Keep the top 50% of genomes.
4. Produce the next generation via crossover (mix two parents) and mutation (nudge random weights).
5. Repeat for a fixed number of generations.
6. Serialise the best genome to `best_genome.json` next to the bot DLL.

The trainer lives in `TankDestroyer.Trainer` and calls `GameRunner` directly in-process — no plugin loading overhead.

## Moving forward

### Step 1 — design the heuristic scoring function
Replace the random logic in `LucBot.cs` with a deterministic scorer. Define a `Genome` struct and a `ScoreAction` method that takes an action + game state + genome and returns a float.

### Step 2 — build `TankDestroyer.Trainer`
New console project that:
- Holds a `Population` of genomes
- Runs the GA loop calling `GameRunner` in-process
- Prints generation stats (best/avg fitness, diversity)
- Writes `best_genome.json` on completion or Ctrl+C

### Step 3 — load genome at runtime
At startup, LucBot checks for `best_genome.json` beside its DLL. If present, loads it; otherwise falls back to default weights (so the bot always works even without training).

### Step 4 — iterate
- Tune population size, mutation rate, and number of games per fitness evaluation.
- Try training against multiple opponent bots (not just one) to avoid overfitting.
- Visualise results in the console game to sanity-check learned behaviour.
