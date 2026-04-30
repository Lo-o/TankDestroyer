using System.Diagnostics;
using System.Reflection;
using TankDestroyer.API;
using TankDestroyer.Engine;

namespace TankDestroyer.Headless;

class Program
{
    const int MaxTurnsPerGame = 5000;

    static void Main(string[] args)
    {
        var botFolder = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Build", "Bots"));
        var mapFolder = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Maps"));

        int games = 100;
        string? mapFilter = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--games" && i + 1 < args.Length)
                int.TryParse(args[++i], out games);
            else if (args[i] == "--map" && i + 1 < args.Length)
                mapFilter = args[++i];
        }

        var botTypes = CollectBotsServices.LoadBots(botFolder);
        if (botTypes.Length < 2)
        {
            Console.Error.WriteLine($"Need at least 2 bots in {botFolder}");
            return;
        }

        var maps = CollectMapsService.LoadMaps(mapFolder);
        if (maps.Length == 0)
        {
            Console.Error.WriteLine($"No maps found in {mapFolder}");
            return;
        }

        if (mapFilter != null)
            maps = maps.Where(m => m.Name.Contains(mapFilter, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (maps.Length == 0)
        {
            Console.Error.WriteLine($"No maps match filter '{mapFilter}'");
            return;
        }

        Console.WriteLine($"Bots loaded: {string.Join(", ", botTypes.Select(BotName))}");
        Console.WriteLine($"Maps: {string.Join(", ", maps.Select(m => m.Name))}");
        Console.WriteLine($"Running {games} games per map...");
        Console.WriteLine();

        var totalSw = Stopwatch.StartNew();

        foreach (var map in maps)
        {
            var eligible = botTypes.Where(t => true).ToArray();
            if (eligible.Length < 2)
            {
                Console.WriteLine($"[{map.Name}] Not enough bots, skipping.");
                continue;
            }

            // Use first two bots; all spawn points on the map are filled with those two alternating
            var pair = eligible.Take(2).ToArray();
            var wins = new Dictionary<string, int>();
            var draws = 0;
            var totalTurns = 0L;

            foreach (var t in pair)
                wins[BotName(t)] = 0;

            var mapSw = Stopwatch.StartNew();

            for (int g = 0; g < games; g++)
            {
                // Alternate spawn order each game to avoid positional bias
                var ordered = g % 2 == 0 ? pair : pair.Reverse().ToArray();
                var bots = ordered.Select(t => (IPlayerBot)Activator.CreateInstance(t)!).ToArray();

                var runner = new GameRunner(map, bots);
                int turns = 0;

                while (!runner.Finished && turns < MaxTurnsPerGame)
                {
                    runner.DoTurn();
                    turns++;
                }

                totalTurns += turns;

                var survivors = runner.GetTanks().Where(t => !t.Destroyed).ToArray();
                if (survivors.Length == 1)
                {
                    var winnerBot = ordered[survivors[0].OwnerId];
                    wins[BotName(winnerBot)]++;
                }
                else
                {
                    draws++;
                }
            }

            mapSw.Stop();

            Console.WriteLine($"=== {map.Name} ({map.Width}x{map.Height}) ===");
            foreach (var (name, count) in wins.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"  {name,-30} {count,4} wins  ({count * 100.0 / games:F1}%)");
            if (draws > 0)
                Console.WriteLine($"  {"Draws",-30} {draws,4}       ({draws * 100.0 / games:F1}%)");
            Console.WriteLine($"  Avg turns/game: {totalTurns / (double)games:F1}");
            Console.WriteLine($"  Time: {mapSw.ElapsedMilliseconds}ms for {games} games  ({mapSw.ElapsedMilliseconds / (double)games:F2}ms/game)");
            Console.WriteLine();
        }

        totalSw.Stop();
        Console.WriteLine($"Total elapsed: {totalSw.ElapsedMilliseconds}ms");
    }

    static string BotName(Type t) =>
        t.GetCustomAttribute<BotAttribute>()?.Name ?? t.Name;
}
