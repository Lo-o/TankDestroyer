using TankDestroyer.API;

namespace Luc.Bot;

[Bot("LucBot", "Luc Oostdijk", "F527A6")]
public class LucBot : IPlayerBot
{
    private Random _random = new();

    public void DoTurn(ITurnContext turnContext)
    {
        var enumValues = Enum.GetValues<TurretDirection>();
        var enumDirectionValues = Enum.GetValues<Direction>();

        turnContext.MoveTank(enumDirectionValues[_random.Next(0, enumDirectionValues.Length)]);
        turnContext.RotateTurret(enumValues[_random.Next(0, enumValues.Length)]);

        turnContext.Fire();
    }
}