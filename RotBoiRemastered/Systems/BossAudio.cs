using Microsoft.Xna.Framework.Audio;

namespace RotBoiRemastered.Systems;

public enum BossAudioCueKind
{
    Declaration,
    Trial,
    Stagger,
    Death,
}

/// <summary>
/// Procedural one-shot description. Short square/triangle-wave cues preserve
/// the game's bit-built presentation and avoid requiring an external audio
/// asset pipeline for boss readability.
/// </summary>
public sealed record BossAudioCueProfile(
    BossAudioCueKind Kind,
    double DurationSeconds,
    float PitchMultiplier,
    float Volume,
    int Pulses);

/// <summary>
/// Best-effort procedural boss SFX bus. Unit tests and headless tools can
/// inspect/emit cue events without initializing audio; the game opts into
/// actual playback from LoadContent and safely falls silent if no device is
/// present.
/// </summary>
public static class BossAudio
{
    private const int SampleRate = 22050;
    private static readonly string[] SenseKeys =
    [
        "sound",
        "touch",
        "sight",
        "chemesthesis",
        "phantasia",
    ];
    private static readonly Dictionary<(BossAudioCueKind Kind, string Sense), SoundEffect> Sounds = new();
    private static bool _runtimeEnabled;

    public static event Action<BossAudioCueKind, string>? CueEmitted;

    public static IReadOnlyDictionary<BossAudioCueKind, BossAudioCueProfile> Profiles { get; } =
        new Dictionary<BossAudioCueKind, BossAudioCueProfile>
        {
            [BossAudioCueKind.Declaration] =
                new(BossAudioCueKind.Declaration, .16, 1.0f, .34f, 2),
            [BossAudioCueKind.Trial] =
                new(BossAudioCueKind.Trial, .48, .82f, .42f, 4),
            [BossAudioCueKind.Stagger] =
                new(BossAudioCueKind.Stagger, .28, .66f, .38f, 3),
            [BossAudioCueKind.Death] =
                new(BossAudioCueKind.Death, .72, .48f, .46f, 5),
        };

    internal static IReadOnlyList<(BossAudioCueKind Kind, string Sense)> WarmupPlan { get; } =
        BuildWarmupPlan();

    public static void Initialize()
    {
        _runtimeEnabled = true;
        try
        {
            // SoundEffect construction can initialize native audio buffers and
            // briefly block the game thread. Do it during LoadContent instead
            // of making a guardian's first attack pay that one-time cost.
            foreach ((BossAudioCueKind kind, string sense) in WarmupPlan)
                GetOrCreateSound(kind, sense);
        }
        catch (Exception)
        {
            // Audio feedback is supplemental. Missing native audio, a device
            // loss, or a headless runtime must never interrupt startup.
            Shutdown();
        }
    }

    public static void Shutdown()
    {
        foreach (SoundEffect sound in Sounds.Values)
            sound.Dispose();
        Sounds.Clear();
        _runtimeEnabled = false;
    }

    public static void Emit(
        BossAudioCueKind kind, string senseKey, float intensity = 1f)
    {
        senseKey = NormalizeSense(senseKey);
        CueEmitted?.Invoke(kind, senseKey);
        if (!_runtimeEnabled)
            return;

        try
        {
            SoundEffect sound = GetOrCreateSound(kind, senseKey);
            BossAudioCueProfile profile = Profiles[kind];
            sound.Play(
                Math.Clamp(profile.Volume * intensity, 0f, 1f),
                0f,
                0f);
        }
        catch (Exception)
        {
            // Audio feedback is supplemental. Missing native audio, a device
            // loss, or a headless runtime must never interrupt simulation.
            Shutdown();
        }
    }

    public static int BaseFrequency(string senseKey) => NormalizeSense(senseKey) switch
    {
        "sound" => 330,
        "touch" => 146,
        "sight" => 494,
        "chemesthesis" => 196,
        "phantasia" => 392,
        _ => 262,
    };

    private static string NormalizeSense(string senseKey) =>
        senseKey is "sound" or "touch" or "sight" or "chemesthesis" or "phantasia"
            ? senseKey
            : "sound";

    private static SoundEffect GetOrCreateSound(
        BossAudioCueKind kind,
        string senseKey)
    {
        var key = (kind, senseKey);
        if (Sounds.TryGetValue(key, out SoundEffect? sound))
            return sound;

        sound = new SoundEffect(
            BuildPcm(kind, senseKey),
            SampleRate,
            AudioChannels.Mono);
        Sounds[key] = sound;
        return sound;
    }

    private static IReadOnlyList<(BossAudioCueKind Kind, string Sense)>
        BuildWarmupPlan()
    {
        BossAudioCueKind[] cueKinds = Enum.GetValues<BossAudioCueKind>();
        var plan = new List<(BossAudioCueKind Kind, string Sense)>(
            cueKinds.Length * SenseKeys.Length);
        foreach (string sense in SenseKeys)
        {
            foreach (BossAudioCueKind kind in cueKinds)
                plan.Add((kind, sense));
        }
        return plan;
    }

    private static byte[] BuildPcm(BossAudioCueKind kind, string senseKey)
    {
        BossAudioCueProfile profile = Profiles[kind];
        int sampleCount = Math.Max(1,
            (int)Math.Round(profile.DurationSeconds * SampleRate));
        var bytes = new byte[sampleCount * sizeof(short)];
        double phase = 0;
        int baseFrequency = BaseFrequency(senseKey);
        for (int index = 0; index < sampleCount; index++)
        {
            double progress = index / (double)Math.Max(1, sampleCount - 1);
            int pulse = Math.Min(
                profile.Pulses - 1,
                (int)(progress * profile.Pulses));
            double pulseProgress = progress * profile.Pulses - pulse;
            double direction = kind switch
            {
                BossAudioCueKind.Declaration => 1.0 + pulse * .25,
                BossAudioCueKind.Trial => 1.0 + (pulse % 2 == 0 ? .5 : 0),
                BossAudioCueKind.Stagger => 1.35 - progress * .72,
                BossAudioCueKind.Death => 1.2 - progress * .88,
                _ => 1.0,
            };
            double frequency = baseFrequency * profile.PitchMultiplier
                * Math.Max(.22, direction);
            phase += Math.Tau * frequency / SampleRate;

            double square = Math.Sin(phase) >= 0 ? 1 : -1;
            double triangle = 2.0 / Math.PI * Math.Asin(Math.Sin(phase * .5));
            double wave = kind is BossAudioCueKind.Trial or BossAudioCueKind.Death
                ? square * .62 + triangle * .38
                : square * .78 + triangle * .22;
            double pulseEnvelope = Math.Sin(Math.PI * Math.Clamp(pulseProgress, 0, 1));
            double tail = Math.Pow(
                1.0 - progress,
                kind == BossAudioCueKind.Death ? .55 : .9);
            short sample = (short)Math.Clamp(
                wave * pulseEnvelope * tail * short.MaxValue * .32,
                short.MinValue,
                short.MaxValue);
            bytes[index * 2] = (byte)(sample & 0xff);
            bytes[index * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }
        return bytes;
    }
}
