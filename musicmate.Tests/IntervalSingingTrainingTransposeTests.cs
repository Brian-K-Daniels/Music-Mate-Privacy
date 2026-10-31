using musicmate.Services;

namespace musicmate.Tests;

public class IntervalSingingTrainingTransposeTests
{
    private const int BbOffset = -2;
    private const int EbAltoOffset = -9;
    private const int FHornOffset = -7;
    private const int EbClarinetOffset = 3;

    [Theory]
    [InlineData(0, BbOffset, 62, 66)]
    [InlineData(0, EbAltoOffset, 69, 73)]
    [InlineData(0, FHornOffset, 67, 71)]
    [InlineData(0, EbClarinetOffset, 57, 61)]
    public void RetransposePreservingConcert_CMajorThird_ChangesWritten_KeepsConcert(
        int fromOffset,
        int toOffset,
        int expectedStartWritten,
        int expectedEndWritten)
    {
        var original = new IntervalEarTrainingLogic.IntervalPitches(60, 64, 4, IsAscending: true);
        var transposed = IntervalEarTrainingLogic.RetransposePreservingConcert(
            original, fromOffset, toOffset);

        Assert.Equal(expectedStartWritten, transposed.StartWrittenMidi);
        Assert.Equal(expectedEndWritten, transposed.EndWrittenMidi);
        Assert.Equal(4, transposed.Semitones);
        Assert.True(transposed.IsAscending);

        Assert.Equal(
            TunerReferenceNoteCatalog.ToConcertMidi(original.StartWrittenMidi, fromOffset),
            TunerReferenceNoteCatalog.ToConcertMidi(transposed.StartWrittenMidi, toOffset));
        Assert.Equal(
            TunerReferenceNoteCatalog.ToConcertMidi(original.EndWrittenMidi, fromOffset),
            TunerReferenceNoteCatalog.ToConcertMidi(transposed.EndWrittenMidi, toOffset));
    }

    [Fact]
    public void RetransposePreservingConcert_SameOffset_IsIdentity()
    {
        var original = new IntervalEarTrainingLogic.IntervalPitches(60, 64, 4, IsAscending: true);
        var same = IntervalEarTrainingLogic.RetransposePreservingConcert(original, BbOffset, BbOffset);
        Assert.Equal(original, same);
    }

    [Fact]
    public void BuildDisplayNotes_AfterBbTransposition_UsesWrittenSpellings()
    {
        var concert = new IntervalEarTrainingLogic.IntervalPitches(60, 64, 4, IsAscending: true);
        var written = IntervalEarTrainingLogic.RetransposePreservingConcert(concert, 0, BbOffset);
        var notes = IntervalEarTrainingNotation.BuildDisplayNotes(
            written,
            IntervalEarTrainingNotation.StaffDisplayKey,
            IntervalEarTrainingNotation.StaffDisplayScale);

        Assert.Equal(2, notes.Count);
        Assert.Equal("D4", notes[0].SpelledName);
        Assert.Equal("F#4", notes[1].SpelledName);
        Assert.Equal(62, notes[0].MidiNumber);
        Assert.Equal(66, notes[1].MidiNumber);
    }

    [Fact]
    public void VoiceInstrument_ConcertPitch_WrittenMatchesConcertMidi()
    {
        var original = new IntervalEarTrainingLogic.IntervalPitches(60, 67, 7, IsAscending: true);
        var voice = IntervalEarTrainingLogic.RetransposePreservingConcert(original, 0, 0);
        Assert.Equal(original, voice);
    }
}
