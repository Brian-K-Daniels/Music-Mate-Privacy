using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class IntervalEarTrainingNotationTests
{
    [Fact]
    public void BuildDisplayNotes_UsesExactStoredMidiPitches()
    {
        var pitches = new IntervalEarTrainingLogic.IntervalPitches(
            StartWrittenMidi: 60,
            EndWrittenMidi: 67,
            Semitones: 7,
            IsAscending: true);

        var notes = IntervalEarTrainingNotation.BuildDisplayNotes(pitches, "C", "Major");

        Assert.Equal(2, notes.Count);
        Assert.Equal(60, notes[0].MidiNumber);
        Assert.Equal(67, notes[1].MidiNumber);
        Assert.False(notes[0].IsRest);
        Assert.False(notes[1].IsRest);
        Assert.Equal(0, notes[0].BeatPosition);
        Assert.Equal(1, notes[1].BeatPosition);
    }

    [Fact]
    public void BuildDisplayNotes_Descending_KeepsStartThenEndMidi()
    {
        var pitches = new IntervalEarTrainingLogic.IntervalPitches(
            StartWrittenMidi: 67,
            EndWrittenMidi: 60,
            Semitones: 7,
            IsAscending: false);

        var notes = IntervalEarTrainingNotation.BuildDisplayNotes(pitches, "C", "Major");

        Assert.Equal(67, notes[0].MidiNumber);
        Assert.Equal(60, notes[1].MidiNumber);
    }

    [Fact]
    public void BuildBarBeats_IsSingleTwoBeatMeasure()
    {
        var bars = IntervalEarTrainingNotation.BuildBarBeats();
        Assert.Single(bars);
        Assert.Equal(2.0, bars[0]);
    }

    [Theory]
    [InlineData(65, 66, 1)] // F → F♯ minor second
    [InlineData(65, 68, 3)] // F → A♭ / G♯ minor third
    [InlineData(60, 61, 1)] // C → C♯
    public void BuildDisplayNotes_ChromaticIntervals_CarryExplicitAccidentals(
        int startMidi, int endMidi, int semitones)
    {
        var pitches = new IntervalEarTrainingLogic.IntervalPitches(
            startMidi, endMidi, semitones, IsAscending: true);
        var notes = IntervalEarTrainingNotation.BuildDisplayNotes(pitches, "C", "Major");

        Assert.Equal(2, notes.Count);
        Assert.Equal(startMidi, notes[0].MidiNumber);
        Assert.Equal(endMidi, notes[1].MidiNumber);
        Assert.NotEqual(notes[0].MidiNumber, notes[1].MidiNumber);
        Assert.True(
            notes[1].Accidental is Accidental.Sharp or Accidental.Flat
                or Accidental.DoubleSharp or Accidental.DoubleFlat,
            $"Expected chromatic accidental on end note, got {notes[1].SpelledName}/{notes[1].Accidental}");
    }
}
