using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class PracticeTuneKeyTests
{
    [Fact]
    public void OdeToJoyF4_StaysNaturalInTuneKey_NotSessionKey()
    {
        const int fNatural = 65;

        int inTuneKey = NoteSessionService.ApplyKeySignatureToMidi(
            "F4", fNatural, "C", NoteSessionService.PracticeTuneKeySignatureScale);
        int inSessionKey = NoteSessionService.ApplyKeySignatureToMidi(
            "F4", fNatural, "G", NoteSessionService.PracticeTuneKeySignatureScale);

        Assert.Equal(fNatural, inTuneKey);
        Assert.Equal(fNatural + 1, inSessionKey);
    }

    [Fact]
    public void ResolveKeyForFreshGeneration_PreservesPracticeTuneKeyAtLevel24()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            string key = NoteSessionService.ResolveKeyForFreshGeneration(
                "Practice Tune",
                TuneLibrary.OdeToJoy,
                "Major",
                keyPoolLevel: 24,
                new Random(seed));

            Assert.Equal("C", key);
        }
    }

    [Fact]
    public void ResolveKeyForFreshGeneration_PreservesArpeggioKeyAtLevel24()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            string key = NoteSessionService.ResolveKeyForFreshGeneration(
                "Arpeggio",
                currentTune: null,
                "Natural Minor",
                keyPoolLevel: 24,
                new Random(seed),
                preservedKey: "E");

            Assert.Equal("E", key);
        }
    }

    [Fact]
    public void ResolveKeyForFreshGeneration_StillRandomizesForByLevelWithoutTune()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int seed = 0; seed < 30; seed++)
        {
            keys.Add(NoteSessionService.ResolveKeyForFreshGeneration(
                "Selected Scale",
                currentTune: null,
                "Major",
                keyPoolLevel: 24,
                new Random(seed)));
        }

        Assert.True(keys.Count > 1);
    }

    [Fact]
    public void ResolvePracticeTuneNotation_AllBuiltInTunesUseCMajor()
    {
        foreach (var tune in TuneLibrary.All)
        {
            var (key, scale) = NoteSessionService.ResolvePracticeTuneNotation(tune);
            Assert.Equal("C", key);
            Assert.Equal(NoteSessionService.PracticeTuneKeySignatureScale, scale);
        }
    }
}
