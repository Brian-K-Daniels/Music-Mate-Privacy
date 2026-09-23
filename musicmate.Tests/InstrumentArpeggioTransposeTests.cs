using musicmate.Services;

namespace musicmate.Tests;

public class InstrumentArpeggioTransposeTests
{
    // Bb clarinet / trumpet / soprano sax: concert = written + (-2)
    private const int BbOffset = -2;

    // Eb alto sax: concert = written + (-9)
    private const int EbAltoOffset = -9;

    [Theory]
    [InlineData("D", BbOffset, "E")]
    [InlineData("C", BbOffset, "D")]
    [InlineData("Eb", BbOffset, "F")]
    [InlineData("D", EbAltoOffset, "B")]
    public void ToWrittenKey_ConcertMajor_ForTransposingInstruments(
        string concertKey, int offset, string expectedWritten)
    {
        Assert.Equal(expectedWritten, NoteSessionService.ToWrittenKey(concertKey, offset));
    }

    [Theory]
    [InlineData("D4", BbOffset, "E4")]
    [InlineData("C4", BbOffset, "D4")]
    [InlineData("Eb4", BbOffset, "F4")]
    [InlineData("D4", EbAltoOffset, "B4")]
    public void ToWrittenNoteName_ConcertRoot_ForTransposingInstruments(
        string concertRoot, int offset, string expectedWritten)
    {
        Assert.Equal(expectedWritten, NoteSessionService.ToWrittenNoteName(concertRoot, offset));
    }

    [Theory]
    [InlineData("D", BbOffset, "E")]
    [InlineData("C", BbOffset, "D")]
    [InlineData("Eb", BbOffset, "F")]
    [InlineData("D", EbAltoOffset, "B")]
    public void ResolveArpeggioWrittenKeySignature_MajorTriad_UsesInstrumentOffset(
        string concertRoot, int offset, string expectedWrittenKey)
    {
        string written = NoteSessionService.ResolveArpeggioWrittenKeySignature(
            ArpeggioCatalog.MajorTriad, $"{concertRoot}4", offset);

        Assert.Equal(expectedWrittenKey, written);
    }

    [Fact]
    public void ResolveArpeggioWrittenKeySignature_MajorTriad_DoesNotUseRelativeMajor()
    {
        // Relative major of D minor is F — must not appear for a major triad on Bb clarinet.
        string written = NoteSessionService.ResolveArpeggioWrittenKeySignature(
            ArpeggioCatalog.MajorTriad, "D4", BbOffset);

        Assert.Equal("E", written);
        Assert.NotEqual("F", written);
    }

    [Fact]
    public void ResolveArpeggioConcertKeySignature_MinorTriad_UsesRelativeMajor()
    {
        Assert.Equal("F", NoteSessionService.ResolveArpeggioConcertKeySignature(
            ArpeggioCatalog.MinorTriad, "D4"));
    }

    [Fact]
    public void ResolveArpeggioWrittenKeySignature_MinorTriad_TransposesRelativeMajor()
    {
        // Concert D minor → concert key sig F → written G for Bb (up a major 2nd).
        Assert.Equal("G", NoteSessionService.ResolveArpeggioWrittenKeySignature(
            ArpeggioCatalog.MinorTriad, "D4", BbOffset));
    }

    [Fact]
    public void ResolveKeyForFreshGeneration_PreservesArpeggioWrittenKey()
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
    public void ToWrittenKey_IsInverseOfConcertFromWritten()
    {
        foreach (var (written, offset) in new[] { ("E", BbOffset), ("D", BbOffset), ("F", BbOffset), ("B", EbAltoOffset) })
        {
            string concert = NoteSessionService.TransposeKey(written, offset);
            Assert.Equal(written, NoteSessionService.ToWrittenKey(concert, offset));
        }
    }
}
