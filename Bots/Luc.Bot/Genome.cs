using System.Text.Json;

namespace Luc.Bot;

public sealed class Genome
{
    public const int Size = 8;

    public float ApproachEnemy { get; set; }  // reward for closing distance to nearest enemy
    public float SeekCover     { get; set; }  // reward for moving onto a covered tile
    public float EvadeBullets  { get; set; }  // penalty for moving into a bullet path
    public float AimAtEnemy    { get; set; }  // reward for rotating turret toward enemy
    public float FireWhenAimed { get; set; }  // fire score scales with aim quality
    public float FireBase      { get; set; }  // constant added to fire score
    public float PreferMove    { get; set; }  // constant added to all move scores
    public float PreferRotate  { get; set; }  // constant added to all rotate scores

    public float[] ToArray() =>
        [ApproachEnemy, SeekCover, EvadeBullets, AimAtEnemy, FireWhenAimed, FireBase, PreferMove, PreferRotate];

    public static Genome FromArray(float[] w) => new()
    {
        ApproachEnemy = w[0],
        SeekCover     = w[1],
        EvadeBullets  = w[2],
        AimAtEnemy    = w[3],
        FireWhenAimed = w[4],
        FireBase      = w[5],
        PreferMove    = w[6],
        PreferRotate  = w[7],
    };

    public static Genome Default() => new()
    {
        ApproachEnemy = 1.0f,
        SeekCover     = 0.3f,
        EvadeBullets  = 2.0f,
        AimAtEnemy    = 1.5f,
        FireWhenAimed = 2.0f,
        FireBase      = 0.3f,
        PreferMove    = 0.0f,
        PreferRotate  = 0.0f,
    };

    public static Genome? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Genome>(File.ReadAllText(path)); }
        catch { return null; }
    }

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
