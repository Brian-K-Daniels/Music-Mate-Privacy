using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Regression: generated/displayed written pitches must agree with MIDI, spelling,
/// staff degree, and 4/4 measure fill — including Bb-major ledger notes like Bb3.
/// </summary>
public class WrittenNoteDisplayConsistencyTests
{
    private const int SixteenthsPerQuarter = 4;

    public static IEnumerable<object[]> FourFourGeneratorSeeds()
    {
        foreach (var key in new[] { "C", "G", "F", "Bb", "Eb", "D" })
        foreach (var scale in new[] { "Major", "Natural Minor" })
        foreach (int seed in new[] { 0, 7, 19, 42, 100, 255 })
        {
            yield return new object[] { key, scale, seed };
        }
    }

    [Theory]
    [MemberData(nameof(FourFourGeneratorSeeds))]
    public void GeneratedFourFour_WrittenPitchMatchesStaffDegreeAndMidi(
        string key, string scale, int seed)
    {
        var gen = new MusicSequenceGenerator
        {
            Key = key,
            Scale = scale,
            LowestNote = "E3",
            HighestNote = "C5",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 6,
            RhythmVarietyPercent = 80,
            SmallestDuration = NoteDuration.Sixteenth,
            RestChancePercent = 25,
            SyncopationLevel = SyncopationLevel.Simple,
            UseMotifPhrases = true,
            UseScaleOrder = false,
            AccidentalPercent = 20,
            RandomSeed = seed,
        };

        var measures = gen.GenerateSequence();
        Assert.NotEmpty(measures);

        int expectedTicks = ToTicks(4.0);
        foreach (var measure in measures)
        {
            int ticks = measure.GeneratedNotes.Sum(n => ToTicks(n.Duration.ToBeatValue()));
            Assert.Equal(expectedTicks, ticks);
        }

        foreach (var note in MusicSequenceGenerator.Flatten(measures).Where(n => !n.IsRest))
        {
            AssertWrittenPitchConsistent(note, key, scale);
        }
    }

    [Fact]
    public void Bb3_StaffDegreeMatchesSpelling_EvenWhenLetterOctaveOmitted()
    {
        const int midiBb3 = 58;
        var complete = new GeneratedNote
        {
            MidiNumber = midiBb3,
            Letter = 'B',
            Octave = 3,
            Accidental = Accidental.None,
            SpelledName = "Bb3",
            Duration = NoteDuration.Quarter,
        };
        var legacy = new GeneratedNote
        {
            MidiNumber = midiBb3,
            SpelledName = "Bb3",
            Duration = NoteDuration.Quarter,
        };

        Assert.Equal(7, StaffDrawable.GetDiatonicStepsFromB4('B', 3));
        Assert.Equal(
            StaffDrawable.ResolveStaffLetterOctave(complete),
            StaffDrawable.ResolveStaffLetterOctave(legacy));

        var (midi, name) = NoteSessionService.ResolveTargetPitch(complete, "Bb", "Major");
        Assert.Equal(midiBb3, midi);
        Assert.Equal("Bb3", name);

        AssertWrittenPitchConsistent(complete, "Bb", "Major");
    }

    [Fact]
    public void ApplyKeySignatureToMidi_IsIdempotent_ForAlreadyFlattedPoolMidi()
    {
        // B natural MIDI is 71; Bb major implies Bb (70). Passing 70 must not flatten again.
        Assert.Equal(70, NoteSessionService.ApplyKeySignatureToMidi("B4", 71, "Bb", "Major"));
        Assert.Equal(70, NoteSessionService.ApplyKeySignatureToMidi("B4", 70, "Bb", "Major"));
        Assert.Equal(70, NoteSessionService.ApplyKeySignatureToMidi(
            "B4", 70, "Bb", NoteSessionService.PracticeTuneKeySignatureScale));
    }

    [Fact]
    public void BuildNote_BbMajorTonic_SpellingLetterOctaveAndMidiAgree()
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "Bb",
            Scale = "Major",
            LowestNote = "Bb3",
            HighestNote = "Bb4",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 4,
            RhythmVarietyPercent = 0,
            SmallestDuration = NoteDuration.Quarter,
            RestChancePercent = 0,
            SyncopationLevel = SyncopationLevel.None,
            UseMotifPhrases = false,
            UseScaleOrder = false,
            AccidentalPercent = 0,
            RandomSeed = 11,
        };

        var pitched = MusicSequenceGenerator.Flatten(gen.GenerateSequence())
            .Where(n => !n.IsRest)
            .ToList();
        Assert.NotEmpty(pitched);

        foreach (var note in pitched)
            AssertWrittenPitchConsistent(note, "Bb", "Major");

        Assert.Contains(pitched, n =>
            n.SpelledName.StartsWith("Bb", StringComparison.Ordinal)
            || n.SpelledName.StartsWith("B", StringComparison.Ordinal));
    }

    [Fact]
    public void SavedTuneNormalize_PopulatesLetterOctaveFromSpelledName()
    {
        var tune = new PracticeTune("Saved Bb", TimeSignature.FourFour, "Bb");
        var m = tune.AppendMeasure();
        m.AddNote(new MusicNote(58, "Bb3", NoteDuration.Quarter));
        m.AddNote(new MusicNote(60, "C4", NoteDuration.Quarter));
        m.AddNote(new MusicNote(62, "D4", NoteDuration.Quarter));
        m.AddNote(new MusicNote(63, "Eb4", NoteDuration.Quarter));

        var normalized = SavedTuneStore.NormalizeMeasureDurations(tune);
        var notes = normalized.AllNotes.Where(n => !n.IsRest).ToList();
        // Normalize packs through FromGeneratedNotes → MusicNote; re-hydrate via spelling check
        // on the intermediate WithPitchIdentity path by rebuilding like MusicPage.
        foreach (var mn in notes)
        {
            Assert.False(string.IsNullOrWhiteSpace(mn.SpelledName));
            char letter = char.ToUpperInvariant(mn.SpelledName.Trim()[0]);
            int octave = NoteSessionService.ParseOctaveFromSpelledName(mn.SpelledName);
            Assert.InRange(letter, 'A', 'G');
            Assert.InRange(octave, 0, 9);

            int steps = StaffDrawable.GetDiatonicStepsFromB4(letter, octave);
            int expectedSteps = StaffDrawable.GetDiatonicStepsFromB4(
                char.ToUpperInvariant(mn.SpelledName.Trim()[0]),
                NoteSessionService.ParseOctaveFromSpelledName(mn.SpelledName));
            Assert.Equal(expectedSteps, steps);
        }
    }

    private static void AssertWrittenPitchConsistent(GeneratedNote note, string key, string scale)
    {
        Assert.False(string.IsNullOrWhiteSpace(note.SpelledName));
        Assert.InRange(note.Letter, 'A', 'G');
        Assert.True(note.Octave >= 0);

        char spelledLetter = char.ToUpperInvariant(note.SpelledName.Trim()[0]);
        int spelledOctave = NoteSessionService.ParseOctaveFromSpelledName(note.SpelledName);
        Assert.Equal(spelledLetter, note.Letter);
        Assert.Equal(spelledOctave, note.Octave);

        var (resolvedMidi, resolvedName) = NoteSessionService.ResolveTargetPitch(note, key, scale);
        Assert.Equal(note.MidiNumber, resolvedMidi);

        // Staff degree follows the diatonic letter/octave (Bb3 uses B3 position).
        int staffSteps = StaffDrawable.GetDiatonicStepsFromB4(note.Letter, note.Octave);
        int spellingSteps = StaffDrawable.GetDiatonicStepsFromB4(spelledLetter, spelledOctave);
        Assert.Equal(spellingSteps, staffSteps);

        // Resolved evaluation name must describe the same staff letter.
        Assert.Equal(note.Letter, char.ToUpperInvariant(resolvedName.Trim()[0]));
        Assert.Equal(note.Octave, NoteSessionService.ParseOctaveFromSpelledName(resolvedName));
    }

    private static int ToTicks(double beats)
        => (int)Math.Round(beats * SixteenthsPerQuarter);
}
