using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Written spelling stays on the staff. Grading compares the sounding chromatic pitch.
/// </summary>
[Collection("SessionPreferences")]
public class EnharmonicPitchMatchTests : IDisposable
{
    public EnharmonicPitchMatchTests()
    {
        SessionPreferences.TestStore = new Dictionary<string, object?>();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Theory]
    [InlineData("F##4", "G4")]
    [InlineData("G##4", "A4")]
    [InlineData("C##4", "D4")]
    [InlineData("D##4", "E4")]
    [InlineData("E##4", "F#4")]
    [InlineData("A##4", "B4")]
    [InlineData("Gbb4", "F4")]
    [InlineData("Abb4", "G4")]
    [InlineData("Dbb4", "C4")]
    [InlineData("Ebb4", "D4")]
    [InlineData("Fbb4", "Eb4")]
    [InlineData("Bbb4", "A4")]
    [InlineData("E#4", "F4")]
    [InlineData("Fb4", "E4")]
    [InlineData("B#4", "C5")]
    [InlineData("Cb4", "B3")]
    [InlineData("B##4", "C#5")]
    [InlineData("Cbb4", "Bb3")]
    [InlineData("B#3", "C4")]
    [InlineData("Cb5", "B4")]
    public void SoundingMidi_MatchesEnharmonicSpelling(string written, string sounding)
    {
        Assert.Equal(
            NoteSessionService.NoteNameToMidi(sounding),
            NoteSessionService.NoteNameToMidi(written));
    }

    [Theory]
    [InlineData("F##4", "G4")]
    [InlineData("G##4", "A4")]
    [InlineData("B#4", "C5")]
    [InlineData("Cb4", "B3")]
    [InlineData("E#4", "F4")]
    [InlineData("Fb4", "E4")]
    [InlineData("Cbb4", "Bb3")]
    [InlineData("B##4", "C#5")]
    [InlineData("Abb4", "G4")]
    public void Evaluate_AcceptsEnharmonicHeardPitch(string written, string heard)
    {
        int sounding = NoteSessionService.NoteNameToMidi(heard);
        var session = new NoteSessionService
        {
            Tolerance = 15,
            Instrument = "concert-pitch",
        };
        session.NotesToDraw.Add(new NoteInfo
        {
            Name = written,
            Midi = 60,
            TargetFreq = NoteSessionService.MidiToFreqPublic(sounding),
        });

        var (correct, cents) = session.Evaluate(NoteSessionService.MidiToFreqPublic(sounding));

        Assert.True(correct, $"{written} should accept heard {heard}");
        Assert.InRange(cents, -15, 15);
        Assert.Equal(written, session.ResolveWrittenEvaluationName(session.NotesToDraw[0]));
    }

    [Fact]
    public void Evaluate_RejectsDifferentSoundingPitch()
    {
        var session = new NoteSessionService
        {
            Tolerance = 15,
            Instrument = "concert-pitch",
        };
        session.NotesToDraw.Add(new NoteInfo { Name = "F##4", Midi = 67 });

        var (correct, _) = session.Evaluate(NoteSessionService.MidiToFreqPublic(
            NoteSessionService.NoteNameToMidi("A4")));

        Assert.False(correct);
    }
}
