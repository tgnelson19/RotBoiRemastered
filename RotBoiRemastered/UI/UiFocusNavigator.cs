using Microsoft.Xna.Framework;

namespace RotBoiRemastered.UI;

public readonly record struct UiFocusTarget(string Id, Rectangle Rect, bool Enabled);

/// <summary>Small spatial focus registry shared by the core menu surfaces.</summary>
public sealed class UiFocusNavigator
{
    private readonly List<UiFocusTarget> _targets = new();
    public string? FocusedId { get; private set; }
    public IReadOnlyList<UiFocusTarget> Targets => _targets;

    public void BeginFrame() => _targets.Clear();

    public void Register(string id, Rectangle rect, bool enabled = true)
    {
        _targets.Add(new UiFocusTarget(id, rect, enabled));
        if (FocusedId is null && enabled)
            FocusedId = id;
    }

    public bool IsFocused(string id) => FocusedId == id;

    public void Focus(string? id)
    {
        if (id is not null && _targets.Any(target => target.Id == id && target.Enabled))
            FocusedId = id;
    }

    public string? At(Point point) =>
        _targets.LastOrDefault(target => target.Enabled && target.Rect.Contains(point)).Id;

    public string? Move(int horizontal, int vertical)
    {
        var enabled = _targets.Where(target => target.Enabled).ToArray();
        if (enabled.Length == 0)
        {
            FocusedId = null;
            return null;
        }
        UiFocusTarget current = enabled.FirstOrDefault(target => target.Id == FocusedId);
        if (current.Id is null)
        {
            FocusedId = enabled[0].Id;
            return FocusedId;
        }
        Vector2 origin = current.Rect.Center.ToVector2();
        UiFocusTarget? best = null;
        float bestScore = float.MaxValue;
        foreach (UiFocusTarget candidate in enabled)
        {
            if (candidate.Id == current.Id)
                continue;
            Vector2 delta = candidate.Rect.Center.ToVector2() - origin;
            if (horizontal < 0 && delta.X >= 0 || horizontal > 0 && delta.X <= 0
                || vertical < 0 && delta.Y >= 0 || vertical > 0 && delta.Y <= 0)
            {
                continue;
            }
            float primary = horizontal != 0 ? Math.Abs(delta.X) : Math.Abs(delta.Y);
            float secondary = horizontal != 0 ? Math.Abs(delta.Y) : Math.Abs(delta.X);
            float score = primary + secondary * 2.6f;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        if (best.HasValue)
            FocusedId = best.Value.Id;
        return FocusedId;
    }
}
