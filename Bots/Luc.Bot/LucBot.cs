using TankDestroyer.API;
using TankDestroyer.Engine;

namespace Luc.Bot;

[Bot("Officer K", "Luc Oostdijk", "FF8C00")]
public class LucBot : IPlayerBot
{
    private readonly Genome _genome;

    public LucBot()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "best_genome.json");
        _genome = Genome.TryLoad(path) ?? Genome.Default();
    }

    // Used by the trainer to inject a genome directly without file I/O
    public LucBot(Genome genome) => _genome = genome;

    public void DoTurn(ITurnContext ctx) => HeuristicScorer.ExecuteBestAction((PlayerTurnContext)ctx, _genome);
}