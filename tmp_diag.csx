using System.Linq;
using System.Text;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

var pattern = ArpeggioCatalog.DiminishedSeventh;
var page = new ArpeggioSequenceBuilder {
    Key = "F", Scale = "Major", LowestNote = "A3", HighestNote = "C6"
}.Build(pattern, "F4");

static string DumpList(string label, IReadOnlyList<GeneratedNote> notes) {
    var sb = new StringBuilder();
    sb.AppendLine(label + ":");
    int p = 0;
    for (int i = 0; i < notes.Count; i++) {
        var n = notes[i];
        if (n.IsRest) continue;
        sb.AppendLine($"  {p} {n.SpelledName} (M{n.MeasureIndex})");
        p++;
    }
    sb.AppendLine($"  count={p}");
    return sb.ToString();
}

Console.WriteLine($"Generated total events={page.Count} pitched={page.Count(n=>!n.IsRest)}");
Console.WriteLine(DumpList("Generated", page));

// Legacy hardcoded 4+4
const int arpeggioUpperMeasureCount = 4;
var upperLegacy = page.Where(n => (n.MeasureIndex ?? 0) < arpeggioUpperMeasureCount).ToList();
var lowerLegacy = page.Where(n => (n.MeasureIndex ?? 0) >= arpeggioUpperMeasureCount).ToList();
Console.WriteLine(DumpList("Upper staff (legacy 4+4)", upperLegacy));
Console.WriteLine(DumpList("Lower staff (legacy 4+4)", lowerLegacy));

var drawable = new StaffDrawable {
    NotationKeyOverride = "F", NotationScaleOverride = "Major",
    ChildLevel = 31, Instrument = "Concert Pitch"
};
var bars = new List<double>();
double mb = 4;
var existing = new HashSet<double>();
foreach (var g in page.GroupBy(n => n.MeasureIndex ?? 0).OrderBy(x => x.Key)) {
    double pos = 0;
    foreach (var n in g.OrderBy(n => n.BeatPosition ?? 0)) {
        bars.Add(pos);
        pos += n.BeatDuration;
    }
    while (bars.Count % 4 != 0 && bars.Count > 0) { }
}
// simpler bar beats like tests
bars.Clear();
for (int m = 0; m < 8; m++) {
    var mn = page.Where(n => (n.MeasureIndex ?? 0) == m).OrderBy(n => n.BeatPosition ?? 0).ToList();
    double pos = 0;
    foreach (var n in mn) { bars.Add(pos); pos += n.BeatDuration; }
}

foreach (float w in new[] { 835f, 700f, 520f }) {
    var split = StaffPageWidthPolicy.SplitCachedPage(drawable, new StaffPagePackState {
        PageNotes = page, PageBarBeats = bars, MeasureBeats = 4, IsTwoOctaveScaleCut = false,
        PackedCanvasWidth = w, PackedCanvasHeight = 480f
    }, w, 480f);
    var playback = split.UpperNotes.Concat(split.LowerNotes).ToList();
    var unplaced = split.UnplacedNotes;
    Console.WriteLine($"--- width {w}: U={split.UpperMeasureCount} L={split.LowerMeasureCount} unplaced={split.UnplacedMeasureCount}");
    Console.WriteLine($"  placed pitched={playback.Count(n=>!n.IsRest)} unplaced pitched={unplaced.Count(n=>!n.IsRest)}");
    Console.WriteLine(DumpList($"  Upper placed", split.UpperNotes));
    Console.WriteLine(DumpList($"  Lower placed", split.LowerNotes));
    Console.WriteLine(DumpList($"  Unplaced", unplaced));
    
    // mismatch vs legacy playback
    var legacyPlayback = upperLegacy.Concat(lowerLegacy).Where(n => !n.IsRest).Select(n => n.SpelledName).ToList();
    var packedPlayback = playback.Where(n => !n.IsRest).Select(n => n.SpelledName).ToList();
    var extra = packedPlayback.Count < legacyPlayback.Count 
        ? legacyPlayback.Skip(packedPlayback.Count).ToList() 
        : new List<string>();
    if (extra.Count > 0)
        Console.WriteLine($"  LEGACY would play {extra.Count} extra pitched events not in width-packed display: {string.Join(", ", extra)}");
}
