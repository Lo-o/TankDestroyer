using System.Collections;
using System.Numerics;
using System.Reflection;
using TankDestroyer.API;

namespace TankDestroyer.Engine;

public class GameRunner
{
    private Game _game;
    public bool Finished { get; set; }
    public bool RecordHistory { get; set; } = true;

    public GameRunner(World world, IPlayerBot[] playerBots)
    {
        _game = new Game(world, playerBots);
        GameTurn turn = new GameTurn();
        turn.World = _game.World;
        turn.Tanks = _game.Tanks.Select(c => c.Clone()).ToArray();
        turn.Actions = Array.Empty<TankAction>();
        turn.Bullets = Array.Empty<Bullet>();
        _game.Turns.Add(turn);
    }

    public bool DoTurn()
    {
        if (Finished)
        {
            return false;
        }

        _game.Bullets.RemoveAll(b => b.Destroyed);

        foreach (var tank in _game.Tanks)
        {
            tank.Fired = false;
        }

        List<TankAction> turnActions = new List<TankAction>();
        foreach (var player in _game.Players)
        {
            if (GetTanks().Any(c => c.OwnerId == player.Id && !c.Destroyed))
            {
                PlayerTurnContext turnContext = new(player, _game, turnActions);
                player.PlayerImplementation.DoTurn(turnContext);
            }
        }


        turnActions = PreventMultipleActionsSameTurn(turnActions);
        foreach (var tankAction in turnActions.OrderBy(c => c.Priority))
        {
            if (GetTanks().Any(c => c.OwnerId == tankAction.OwnerId && !c.Destroyed))
            {
                tankAction.Execute(_game);
            }
        }

        var currentBullets = GetBullets();
        foreach (var bullet in GetBullets())
        {
            ProcessBullet(bullet);
        }

        if (RecordHistory)
        {
            GameTurn turn = new GameTurn();
            turn.World = _game.World;
            turn.Tanks = _game.Tanks.Select(c => c.Clone()).ToArray();
            turn.Actions = turnActions.ToArray();
            turn.Bullets = _game.Bullets.Select(c => c.Clone()).ToArray();
            turn.Turn = _game.Turns.Last().Turn + 1;
            _game.Turns.Add(turn);
        }

        Finished = _game.Tanks.Length > 1 && _game.Tanks.Count(c => c.Destroyed == false) <= 1;
        return Finished;
    }

    private void ProcessBullet(Bullet bullet)
    {
        var direction = new Vector2(0, 0);
        if (bullet.Direction.HasFlag(TurretDirection.North))
        {
            direction += new Vector2(0, 1);
        }

        if (bullet.Direction.HasFlag(TurretDirection.South))
        {
            direction += new Vector2(0, -1);
        }

        if (bullet.Direction.HasFlag(TurretDirection.West))
        {
            direction += new Vector2(1, 0);
        }

        if (bullet.Direction.HasFlag(TurretDirection.East))
        {
            direction += new Vector2(-1, 0);
        }

        var normalized = Vector2.Normalize(direction);
        var movement = normalized * 5f;
        for (int i = 0; i <= 6; i++)
        {
            var cell = normalized * i;
            var cellX = Math.Clamp((int)cell.X + bullet.X, 0, _game.World.Width - 1);
            var cellY = Math.Clamp((int)cell.Y + bullet.Y, 0, _game.World.Height - 1);
            var tankAtCell = _game.Tanks.FirstOrDefault(t =>
                t.X == cellX && t.Y == cellY && t.OwnerId != bullet.OwnerId);
            if (tankAtCell != null)
            {
                if (tankAtCell.Health > 0)
                {
                    var cellType = _game.World.GetTile(cellX, cellY).TileType;

                    switch (cellType)
                    {
                        case TileType.Tree:
                            tankAtCell.TakeDamage(25);
                            break;
                        case TileType.Building:
                            tankAtCell.TakeDamage(50);
                            break;
                        default:
                            tankAtCell.TakeDamage(75);
                            break;
                    }
                }

                bullet.Explode = true;
                RemoveBulletAt(bullet, cellX, cellY);
                if (tankAtCell.Health <= 0)
                {
                    DestroyTank(tankAtCell);
                }

                break;
            }

            var cellTypeAtTile = _game.World.GetTile(cellX, cellY);
            if (cellTypeAtTile.TileType == TileType.Tree)
            {
                bullet.Explode = true;
                RemoveBulletAt(bullet, cellX, cellY);
                break;
            }

            if (cellTypeAtTile.TileType == TileType.Building &&
                (cellX != bullet.StartingX || cellY != bullet.StartingY))
            {
                bullet.Explode = true;
                RemoveBulletAt(bullet, cellX, cellY);
                break;
            }
        }

        if (bullet.Destroyed)
        {
            bullet.X = bullet.EndedAtY;
            bullet.Y = bullet.EndedAtX;
        }
        else
        {
            bullet.X += (int)movement.X;
            bullet.Y += (int)movement.Y;
        }


        RemoveBulletIfOutsideWorld(bullet);
    }

    private void DestroyTank(Tank tankAtCell)
    {
        tankAtCell.Destroyed = true;
    }

    private void RemoveBulletIfOutsideWorld(Bullet bullet)
    {
        if (bullet.Y > _game.World.Height - 1)
        {
            RemoveBulletAt(bullet, bullet.X, bullet.Y);
        }

        if (bullet.Y < 0)
        {
            RemoveBulletAt(bullet, bullet.X, bullet.Y);
        }

        if (bullet.X > _game.World.Width - 1)
        {
            RemoveBulletAt(bullet, bullet.X, bullet.Y);
        }

        if (bullet.X < 0)
        {
            RemoveBulletAt(bullet, bullet.X, bullet.Y);
        }
    }

    private void RemoveBulletAt(Bullet bullet, int x, int y)
    {
        bullet.EndedAtX = x;
        bullet.EndedAtY = y;
        bullet.Destroyed = true;
    }

    private static List<TankAction> PreventMultipleActionsSameTurn(List<TankAction> turnActions)
    {
        return turnActions.DistinctBy(c => new
        {
            Type = c.GetType(),
            c.OwnerId
        }).ToList();
    }

    public World GetWorld() => _game.World;

    public Tank[] GetTanks() => _game.Tanks;

    public Bullet[] GetBullets() => _game.Bullets.ToArray();

    public GameTurn[] GetTurns() => _game.Turns.ToArray();

    public string GetPlayerName(Tank tank)
    {
        return tank.OwnerId + " - " + _game.Players.Single(c => c.Id == tank.OwnerId).PlayerImplementation.GetType()
            .GetCustomAttribute<BotAttribute>()?.Name ?? "<no name>";
    }

    public string GetCreatorName(Tank tank)
    {
        return _game.Players.Single(c => c.Id == tank.OwnerId).PlayerImplementation.GetType()
            .GetCustomAttribute<BotAttribute>()?.Creator ?? "<no creator>";
    }

    public string GetPlayerColor(Tank tank)
    {
        return _game.Players.Single(c => c.Id == tank.OwnerId).PlayerImplementation.GetType()
            .GetCustomAttribute<BotAttribute>()?.Color ?? "";
    }

    public GameTurn GetNextTurn(GameTurn currentTurn)
    {
        var currentIndex = _game.Turns.IndexOf(currentTurn);
        if (currentIndex == -1 || currentIndex >= _game.Turns.Count - 1)
        {
            return currentTurn;
        }

        return _game.Turns[currentIndex + 1];
    }
}