using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Pixel 10 Pro report against Music Mate 1.32: Bb clarinet, Major, L31,
/// Make It Easy off, selected key E Major — staff showed Concert F♯, first
/// note G, and a one-sharp signature. These tests lock written-vs-concert
/// meaning, E major generation, and the Level 31 key gate that can substitute
/// G major on Go.
/// </summary>
[Collection("SessionPreferences")]
public class BbClarinetEMajorMusicPageRegressionTests : IDisposable
{
    public BbClarinetEMajorMusicPageRegressionTests()
    {
        SessionPreferences.TestStore = new Dictionary<string, object?>();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void KeySelector_StoresWrittenKey_NotConcertKey()
    {
        var session = CreateClarinetSession();
        session.Key = "E";

        Assert.Equal("E", session.Key);
        Assert.Equal(-2, session.InstrumentTransposeOffset);
        Assert.NotEqual(session.Key, session.GetConcertKey());
    }

    [Theory]
    [InlineData("E", "D")]
    [InlineData("G", "F")]
    [InlineData("D", "C")]
    [InlineData("F#", "E")]
    [InlineData("A", "G")]
    [InlineData("C", "A#")] // TransposeKey keeps the written key's sharp/flat preference; C is not a flat key.
    public void BbClarinet_WrittenKey_TransposesDownAMajorSecond(string written, string expectedConcert)
    {
        var session = CreateClarinetSession();
        session.Key = written;

        Assert.Equal(expectedConcert, session.GetConcertKey());
        Assert.Equal(expectedConcert, NoteSessionService.TransposeKey(written, -2));
    }

    [Fact]
    public void BbClarinet_WrittenEMajor_ConcertIsD_NotFSharp()
    {
        var session = CreateClarinetSession();
        session.Key = "E";

        Assert.Equal("D", session.GetConcertKey());
        Assert.NotEqual("F#", session.GetConcertKey());
        Assert.NotEqual("Gb", session.GetConcertKey());

        // Inverse direction: concert E → written F#. A concert label of F#
        // for selected E means the UI applied ToWrittenKey instead of GetConcertKey.
        Assert.Equal("F#", NoteSessionService.ToWrittenKey("E", -2));
        Assert.Equal("E", NoteSessionService.ToWrittenKey("D", -2));
    }

    [Fact]
    public void BbClarinet_WrittenE4_SoundsConcertD4()
    {
        var session = CreateClarinetSession();
        var note = new NoteInfo { Name = "E4", Midi = 64, TargetFreq = Hz(64) };
        var domains = session.DescribePitchDomains(note, Hz(62));

        Assert.Equal("E4", domains.ExpectedWrittenPitch);
        Assert.Equal(64, domains.ExpectedWrittenMidi);
        Assert.Equal(62, domains.ExpectedConcertMidi);
        Assert.Equal("D4", domains.ExpectedConcertPitch);
        Assert.Equal(-2, domains.ExpectedConcertMidi - domains.ExpectedWrittenMidi);
    }

    [Fact]
    public void BbClarinet_WrittenG4_SoundsConcertF4_NotFSharp()
    {
        var session = CreateClarinetSession();
        var note = new NoteInfo { Name = "G4", Midi = 67, TargetFreq = Hz(67) };
        var domains = session.DescribePitchDomains(note, Hz(65));

        Assert.Equal("G4", domains.ExpectedWrittenPitch);
        Assert.Equal("F4", domains.ExpectedConcertPitch);
        Assert.NotEqual("F#4", domains.ExpectedConcertPitch);
    }

    [Fact]
    public void BbClarinet_WrittenGSharp4_SoundsConcertFSharp()
    {
        var session = CreateClarinetSession();
        var note = new NoteInfo { Name = "G#4", Midi = 68, TargetFreq = Hz(68) };
        var domains = session.DescribePitchDomains(note, Hz(66));

        Assert.Equal("G#4", domains.ExpectedWrittenPitch);
        Assert.Equal("F#4", domains.ExpectedConcertPitch);
    }

    [Fact]
    public void EMajor_KeySignature_IsFourSharps()
    {
        Assert.Equal(4, KeySignatureRules.GetAccidentalCount("E", "Major"));
        Assert.False(KeySignatureRules.KeySignatureUsesFlats("E", "Major"));
        Assert.Equal("#", KeySignatureRules.GetSignatureAccidentalForLetter('F', "E", "Major"));
        Assert.Equal("#", KeySignatureRules.GetSignatureAccidentalForLetter('C', "E", "Major"));
        Assert.Equal("#", KeySignatureRules.GetSignatureAccidentalForLetter('G', "E", "Major"));
        Assert.Equal("#", KeySignatureRules.GetSignatureAccidentalForLetter('D', "E", "Major"));
        Assert.Null(KeySignatureRules.GetSignatureAccidentalForLetter('A', "E", "Major"));
        Assert.Equal(new[] { "F#", "C#", "G#", "D#" }, SignatureAccidentals("E", "Major"));
    }

    [Fact]
    public void GMajor_KeySignature_IsOneSharp_FSharp()
    {
        Assert.Equal(1, KeySignatureRules.GetAccidentalCount("G", "Major"));
        Assert.Equal(new[] { "F#" }, SignatureAccidentals("G", "Major"));
        Assert.Equal("G", KeySignatureRules.RelativeMajorOf("E"));
        Assert.Equal(1, KeySignatureRules.GetAccidentalCount("E", "Natural Minor"));
    }

    [Fact]
    public void EMajor_IsIllegalAtLevel31_BecauseFourSharpsExceedCapOfTwo()
    {
        Assert.Equal(2, KeyDifficultyRules.GetMaxKeySignatureDifficulty(31));
        Assert.Equal(4, KeyDifficultyRules.GetKeySignatureDifficulty("E", "Major"));
        Assert.Equal(51, KeyDifficultyRules.GetMinimumLevelForKey("E", "Major"));
        Assert.False(KeyDifficultyRules.IsKeyAllowedAtLevel("E", "Major", 31));
        Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel("G", "Major", 31));
        Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel("D", "Major", 31));
        Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel("F", "Major", 31));
        Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel("Bb", "Major", 31));
        Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel("C", "Major", 31));
    }

    [Fact]
    public void Level31_AllowedMajorKeys_AreOnlyZeroToTwoAccidentals()
    {
        var allowed = KeyDifficultyRules.GetAllowedKeyNamesForScale(31, "Major");
        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "C", "G", "F", "D", "Bb" }, allowed);
        Assert.DoesNotContain("E", allowed);
        Assert.DoesNotContain("A", allowed);
        Assert.DoesNotContain("F#", allowed);
    }

    [Fact]
    public void EnsureKeyAllowedAtLevel_ReplacesEMajorAtLevel31()
    {
        for (int seed = 0; seed < 40; seed++)
        {
            string replacement = KeyDifficultyRules.EnsureKeyAllowedAtLevel(
                "E", "Major", 31, new Random(seed));

            Assert.NotEqual("E", replacement);
            Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel(replacement, "Major", 31));
            Assert.True(KeySignatureRules.GetAccidentalCount(replacement, "Major") <= 2);
        }
    }

    [Fact]
    public void NamedSelectedScale_GoAtLevel31_KeepsUserSelectedEMajor()
    {
        var session = CreateClarinetSession();
        session.ChildLevel = 31;
        session.ScaleSelectionMode = ScaleSelectionMode.Named;
        session.Tune = "Selected Scale";
        session.IsRandomMode = false;
        session.SelectedScale = "Major";
        session.Key = "E";
        session.PrepareEffectiveScaleForGeneration(0);

        for (int seed = 1; seed <= 30; seed++)
        {
            session.Key = "E";
            session.PrepareFreshScaleAndKeyForGeneration("GoButton", repeatSame: false, seed);
            Assert.Equal("Major", session.EffectiveScale);
            Assert.Equal("E", session.Key);
            Assert.Equal("D", session.GetConcertKey());
            Assert.Equal(4, KeySignatureRules.GetAccidentalCount(session.Key, session.EffectiveScale));
        }
    }

    [Fact]
    public void ByLevel_GoAtLevel31_NeverKeepsIllegalMajorE()
    {
        var session = CreateClarinetSession();
        session.ChildLevel = 31;
        session.ScaleSelectionMode = ScaleSelectionMode.ByLevel;
        session.Tune = "Selected Scale";
        session.IsRandomMode = false;
        session.SelectedScale = "Major";
        session.PrepareEffectiveScaleForGeneration(0);

        for (int seed = 1; seed <= 30; seed++)
        {
            session.Key = "E";
            session.SelectedScale = "Major";
            session.PrepareFreshScaleAndKeyForGeneration("GoButton", repeatSame: false, seed);
            Assert.True(
                KeyDifficultyRules.IsKeyAllowedAtLevel(session.Key, session.EffectiveScale, 31));
            if (string.Equals(session.EffectiveScale, "Major", StringComparison.Ordinal))
                Assert.NotEqual("E", session.Key);
        }
    }

    [Fact]
    public void NamedSelectedScale_RepeatSame_KeepsEMajor()
    {
        var session = CreateClarinetSession();
        session.ChildLevel = 31;
        session.ScaleSelectionMode = ScaleSelectionMode.Named;
        session.Tune = "Selected Scale";
        session.SelectedScale = "Major";
        session.Key = "E";
        session.PrepareEffectiveScaleForGeneration(0);

        session.PrepareFreshScaleAndKeyForGeneration("GoButton", repeatSame: true, 7);

        Assert.Equal("E", session.Key);
        Assert.Equal("Major", session.EffectiveScale);
        Assert.Equal("D", session.GetConcertKey());
        Assert.Equal(4, KeySignatureRules.GetAccidentalCount(session.Key, session.EffectiveScale));
    }

    [Fact]
    public void EMajorScaleWalk_Level31Range_StartsOnWrittenE_NotG()
    {
        var notes = GenerateScaleWalk("E", "Major", "A3", "E5");
        var first = Assert.Single(notes.Take(1));

        Assert.Equal('E', first.Letter);
        Assert.Equal(64, first.MidiNumber);
        Assert.StartsWith("E", first.SpelledName);
        Assert.DoesNotContain(notes.Take(8), n => n.Letter == 'G' && n.Accidental == Accidental.None);
        Assert.Contains(notes, n => n.Letter == 'G' && n.Accidental == Accidental.Sharp);
    }

    [Fact]
    public void GMajorScaleWalk_Level31Range_StartsOnWrittenG_WithOnlyFSharp()
    {
        var notes = GenerateScaleWalk("G", "Major", "A3", "E5");
        var first = Assert.Single(notes.Take(1));

        Assert.Equal('G', first.Letter);
        Assert.Equal(67, first.MidiNumber);
        Assert.Equal(new[] { "F#" }, SignatureAccidentals("G", "Major"));
    }

    [Fact]
    public void SessionNotation_EMajor_UsesFourSharpsAndConcertD()
    {
        var session = CreateClarinetSession();
        session.ChildLevel = 31;
        session.ScaleSelectionMode = ScaleSelectionMode.Named;
        session.Tune = "Selected Scale";
        session.SelectedScale = "Major";
        session.Key = "E";
        session.PrepareEffectiveScaleForGeneration(0);

        var (key, scale) = session.GetNotationKeyAndScale();
        Assert.Equal("E", key);
        Assert.Equal("Major", scale);
        Assert.Equal("D", session.GetConcertKey());
        Assert.Equal(4, KeySignatureRules.GetAccidentalCount(key, scale));

        var generatedE = new GeneratedNote
        {
            MidiNumber = 64,
            Letter = 'E',
            Octave = 4,
            SpelledName = "E4",
            Accidental = Accidental.None,
        };
        Assert.Equal("E4", session.WrittenNameForStaffLabel(generatedE));

        var generatedGSharp = new GeneratedNote
        {
            MidiNumber = 68,
            Letter = 'G',
            Octave = 4,
            SpelledName = "G4",
            Accidental = Accidental.None,
        };
        Assert.Equal("G#4", session.WrittenNameForStaffLabel(generatedGSharp));
    }

    [Fact]
    public void PremiumEntitlement_DoesNotChangeWrittenKeyOrConcert()
    {
        bool previous = StatusService.Instance.IsPremiumUser;
        try
        {
            var session = CreateClarinetSession();
            session.Key = "E";
            StatusService.Instance.IsPremiumUser = false;
            Assert.Equal("E", session.Key);
            Assert.Equal("D", session.GetConcertKey());

            StatusService.Instance.IsPremiumUser = true;
            Assert.Equal("E", session.Key);
            Assert.Equal("D", session.GetConcertKey());
            Assert.Equal(4, KeySignatureRules.GetAccidentalCount(session.Key, "Major"));
        }
        finally
        {
            StatusService.Instance.IsPremiumUser = previous;
        }
    }

    [Fact]
    public void ObservedOneSharpGStaff_MatchesSubstitutedGMajor_NotWrittenEMajor()
    {
        // The reported staff (first note G, only F# in the signature) is G major.
        // Written E major cannot produce that signature or tonic.
        Assert.NotEqual(
            KeySignatureRules.GetAccidentalCount("E", "Major"),
            KeySignatureRules.GetAccidentalCount("G", "Major"));
        Assert.Equal(1, KeySignatureRules.GetAccidentalCount("G", "Major"));
        Assert.Equal(4, KeySignatureRules.GetAccidentalCount("E", "Major"));

        var session = CreateClarinetSession();
        session.Key = "G";
        Assert.Equal("F", session.GetConcertKey());
        session.Key = "E";
        Assert.Equal("D", session.GetConcertKey());
    }

    private static NoteSessionService CreateClarinetSession()
    {
        var session = new NoteSessionService
        {
            Instrument = "bb-clarinet",
            Tune = "Selected Scale",
            SelectedScale = "Major",
        };
        Assert.Equal(-2, session.InstrumentTransposeOffset);
        return session;
    }

    private static List<GeneratedNote> GenerateScaleWalk(
        string key, string scale, string lowest, string highest)
    {
        var gen = new MusicSequenceGenerator
        {
            Key = key,
            Scale = scale,
            LowestNote = lowest,
            HighestNote = highest,
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 2,
            RhythmVarietyPercent = 0,
            SmallestDuration = NoteDuration.Quarter,
            RestChancePercent = 0,
            UseScaleOrder = true,
            UseMotifPhrases = false,
            AccidentalPercent = 0,
            RandomSeed = 1,
        };

        return MusicSequenceGenerator.Flatten(gen.GenerateSequence())
            .Where(n => !n.IsRest)
            .ToList();
    }

    private static string[] SignatureAccidentals(string key, string scale)
    {
        int count = KeySignatureRules.GetAccidentalCount(key, scale);
        bool flats = KeySignatureRules.KeySignatureUsesFlats(key, scale);
        var letters = flats
            ? KeySignatureRules.FlatLetterOrder.Take(count)
            : KeySignatureRules.SharpLetterOrder.Take(count);
        return letters.Select(letter => flats ? $"{letter}b" : $"{letter}#").ToArray();
    }

    private static double Hz(int midi) => NoteSessionService.MidiToFreqPublic(midi);
}
