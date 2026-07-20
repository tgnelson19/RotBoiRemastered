using RotBoiRemastered.Entities;

namespace RotBoiRemastered.Systems;

/// <summary>
/// Per-path New Game Plus progression. Tier zero is the normal path; clearing
/// tier N unlocks tier N+1 only for that path. Combat scales geometrically,
/// while completion rewards double at every tier.
/// </summary>
public static class NewGamePlus
{
    public const int MaxLevel = 7;
    public const double EnemyScalePerLevel = 1.5;

    public static int ClampLevel(int level) => Math.Clamp(level, 0, MaxLevel);

    public static double EnemyMultiplier(int level) =>
        Math.Pow(EnemyScalePerLevel, ClampLevel(level));

    public static int RewardMultiplier(int level) => 1 << ClampLevel(level);

    public static int UnlockedLevel(string pathKey) => ClampLevel(
        GameProfile.Profile.NewGamePlusUnlocked.GetValueOrDefault(pathKey));

    public static int SelectedLevel(string pathKey) => Math.Min(UnlockedLevel(pathKey), ClampLevel(
        GameProfile.Profile.SelectedNewGamePlus.GetValueOrDefault(pathKey)));

    public static bool TrySelect(string pathKey, int level, bool save = true)
    {
        if (level < 0 || level > UnlockedLevel(pathKey) || level > MaxLevel)
            return false;
        GameProfile.Profile.SelectedNewGamePlus[pathKey] = level;
        if (save)
            GameProfile.SaveProfile();
        return true;
    }

    public static bool AdjustSelection(string pathKey, int direction, bool save = true)
    {
        int current = SelectedLevel(pathKey);
        int requested = Math.Clamp(current + Math.Sign(direction), 0, UnlockedLevel(pathKey));
        if (requested == current)
            return false;
        return TrySelect(pathKey, requested, save);
    }

    public static void RecordCompletion(string pathKey, int completedLevel)
    {
        int next = Math.Min(MaxLevel, ClampLevel(completedLevel) + 1);
        if (next > UnlockedLevel(pathKey))
            GameProfile.Profile.NewGamePlusUnlocked[pathKey] = next;
    }

    /// <summary>Scales a newly spawned enemy's current and maximum health exactly once.</summary>
    public static void ApplyEnemyHealth(Enemy enemy, int level)
    {
        int target = ClampLevel(level);
        int applied = ClampLevel(enemy.NewGamePlusLevelApplied);
        if (target <= applied)
            return;
        double ratio = EnemyMultiplier(target) / EnemyMultiplier(applied);
        enemy.MaxHp = Math.Max(1, (int)Math.Round(enemy.MaxHp * ratio));
        enemy.Hp = Math.Max(1, (int)Math.Round(enemy.Hp * ratio));
        enemy.NewGamePlusLevelApplied = target;
        foreach (var child in enemy.SpawnedEnemies)
            ApplyEnemyHealth(child, target);
    }

    public static double ScaleEnemyDamage(double damage, int level) =>
        damage * EnemyMultiplier(level);
}
