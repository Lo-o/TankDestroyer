using TankDestroyer.API;
using TankDestroyer.Engine;
using Luc.Bot;

namespace TankDestroyer.Trainer;

class Program
{
    const int   PopSize          = 50;
    const int   Generations      = 200;
    const int   GamesPerEval     = 30;
    const float MutationRate     = 0.15f;
    const float MutationStrength = 0.3f;
    const int   EliteCount       = 2;
    const int   MaxTurns         = 2000;

    static readonly ThreadLocal<Random> Rng = new(() => new Random(Guid.NewGuid().GetHashCode()));

    static World[]  _maps            = [];
    static Genome?  _bestEver;
    static float    _bestEverFitness = -1f;
    static readonly string GenomePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "best_genome.json");

    static void Main()
    {
        var mapFolder = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Maps"));
        _maps = CollectMapsService.LoadMaps(mapFolder)
            .Where(m => m.SpawnPoints.Length >= 2)
            .ToArray();

        if (_maps.Length == 0) { Console.Error.WriteLine($"No usable maps in {mapFolder}"); return; }

        Console.WriteLine($"Maps: {string.Join(", ", _maps.Select(m => m.Name))}");
        Console.WriteLine($"Population={PopSize}  Generations={Generations}  Games/eval={GamesPerEval}");
        Console.WriteLine($"Saving to: {GenomePath}");
        Console.WriteLine();

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; SaveBest(); Environment.Exit(0); };

        var population = Enumerable.Range(0, PopSize).Select(_ => RandomGenome()).ToArray();

        for (int gen = 1; gen <= Generations; gen++)
        {
            var scores = new float[PopSize];
            Parallel.For(0, PopSize, i => scores[i] = EvaluateFitness(population[i]));

            var ranked = population.Zip(scores, (g, s) => (g, s))
                                   .OrderByDescending(x => x.s)
                                   .ToArray();

            float best = ranked[0].s;
            float avg  = ranked.Average(x => x.s);

            if (best > _bestEverFitness)
            {
                _bestEverFitness = best;
                _bestEver        = ranked[0].g;
                SaveBest();
            }

            Console.WriteLine($"Gen {gen,4}/{Generations}  best={best:P1}  avg={avg:P1}  all-time={_bestEverFitness:P1}");

            var parents = ranked.Take(PopSize / 2).Select(x => x.g).ToArray();
            var next    = new Genome[PopSize];
            for (int i = 0; i < EliteCount; i++)
                next[i] = ranked[i].g;
            for (int i = EliteCount; i < PopSize; i++)
            {
                var p1 = parents[Rng.Value!.Next(parents.Length)];
                var p2 = parents[Rng.Value!.Next(parents.Length)];
                next[i] = Mutate(Crossover(p1, p2));
            }
            population = next;
        }

        SaveBest();
        Console.WriteLine($"\nFinal best fitness: {_bestEverFitness:P1}");
    }

    static float EvaluateFitness(Genome genome)
    {
        var rng  = Rng.Value!;
        int wins = 0;
        for (int g = 0; g < GamesPerEval; g++)
        {
            var map       = _maps[rng.Next(_maps.Length)];
            int lucBotIdx = g % 2;
            IPlayerBot[] bots = lucBotIdx == 0
                ? [new LucBot(genome), new ReferenceBot()]
                : [new ReferenceBot(), new LucBot(genome)];

            var runner = new GameRunner(map, bots);
            int turns  = 0;
            while (!runner.Finished && turns++ < MaxTurns)
                runner.DoTurn();

            var survivors = runner.GetTanks().Where(t => !t.Destroyed).ToArray();
            if (survivors.Length == 1 && survivors[0].OwnerId == lucBotIdx)
                wins++;
        }
        return (float)wins / GamesPerEval;
    }

    static Genome Crossover(Genome a, Genome b)
    {
        var wa  = a.ToArray();
        var wb  = b.ToArray();
        var rng = Rng.Value!;
        var wc  = new float[Genome.Size];
        for (int i = 0; i < Genome.Size; i++)
            wc[i] = rng.NextDouble() < 0.5 ? wa[i] : wb[i];
        return Genome.FromArray(wc);
    }

    static Genome Mutate(Genome g)
    {
        var w   = g.ToArray();
        var rng = Rng.Value!;
        for (int i = 0; i < w.Length; i++)
            if (rng.NextDouble() < MutationRate)
                w[i] += (float)(Gaussian(rng) * MutationStrength);
        return Genome.FromArray(w);
    }

    static Genome RandomGenome()
    {
        var rng = Rng.Value!;
        var w   = new float[Genome.Size];
        for (int i = 0; i < w.Length; i++)
            w[i] = (float)(rng.NextDouble() * 4.0 - 1.0);
        return Genome.FromArray(w);
    }

    static double Gaussian(Random rng)
    {
        // Box-Muller transform
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    static void SaveBest()
    {
        if (_bestEver == null) return;
        _bestEver.Save(GenomePath);
        Console.WriteLine($"  → saved best genome (fitness={_bestEverFitness:P1})");
    }
}

[Bot("Reference Bot", "Trainer", "808080")]
file class ReferenceBot : IPlayerBot
{
    [ThreadStatic] static Random? _rng;
    static Random Rng => _rng ??= new Random(Guid.NewGuid().GetHashCode());

    public void DoTurn(ITurnContext ctx)
    {
        switch (Rng.Next(3))
        {
            case 0: ctx.MoveTank(Enum.GetValues<Direction>()[Rng.Next(4)]); break;
            case 1: ctx.RotateTurret(Enum.GetValues<TurretDirection>()[Rng.Next(8)]); break;
            case 2: ctx.Fire(); break;
        }
    }
}
