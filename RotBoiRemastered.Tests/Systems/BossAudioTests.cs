using RotBoiRemastered.Systems;

namespace RotBoiRemastered.Tests.Systems;

public sealed class BossAudioTests
{
    [Fact]
    public void ProceduralGrammar_CoversEveryRequiredBossCue()
    {
        Assert.Equal(
            Enum.GetValues<BossAudioCueKind>().Order(),
            BossAudio.Profiles.Keys.Order());
        Assert.All(BossAudio.Profiles.Values, profile =>
        {
            Assert.InRange(profile.DurationSeconds, .1, .8);
            Assert.InRange(profile.Volume, .2f, .6f);
            Assert.True(profile.Pulses >= 2);
        });
        Assert.True(
            BossAudio.Profiles[BossAudioCueKind.Death].DurationSeconds
            > BossAudio.Profiles[BossAudioCueKind.Declaration].DurationSeconds);
    }

    [Fact]
    public void SenseVoices_UseDistinctPitchCenters()
    {
        int[] frequencies =
        [
            BossAudio.BaseFrequency("sound"),
            BossAudio.BaseFrequency("touch"),
            BossAudio.BaseFrequency("sight"),
            BossAudio.BaseFrequency("chemesthesis"),
            BossAudio.BaseFrequency("phantasia"),
        ];

        Assert.Equal(5, frequencies.Distinct().Count());
        Assert.All(frequencies, frequency =>
            Assert.InRange(frequency, 120, 520));
    }
}
