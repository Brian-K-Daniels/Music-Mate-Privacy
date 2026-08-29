using System.Linq;
using musicmate.Models;
using musicmate.Services;
using musicmate.Drawables;

var gen = new MusicSequenceGenerator {
    Key = "C", Scale = "Chromatic", LowestNote = "A3", HighestNote = "C6",
    TimeSignature = TimeSignature.FourFour, MeasureCount = 24,
    RhythmVarietyPercent = 0, SmallestDuration = NoteDuration.Quarter,
    RestChancePercent = 0, UseScaleOrder = true, ChildLevel = 31, RandomSeed = 19
};
var measures = gen.GenerateSequence();
var flat = MusicSequenceGenerator.Flatten(measures);
Console.WriteLine("=== GENERATED ===");
for (int i = 0; i < Math.Min(20, flat.Count); i++) {
    var n = flat[i];
    Console.WriteLine($"{i} mi={n.MeasureIndex} beat={n.BeatPosition:0.##} midi={n.MidiNumber} {n.SpelledName} acc={n.Accidental} letter={n.Letter} oct={n.Octave}");
}
// Check C4-C#4-D4 region
Console.WriteLine("\n=== C region (midi 60-62) ===");
foreach (var n in flat.Where(n => n.MidiNumber >= 59 && n.MidiNumber <= 63 && !n.IsRest))
    Console.WriteLine($"midi={n.MidiNumber} {n.SpelledName} mi={n.MeasureIndex} beat={n.BeatPosition}");

// gaps in semitones
var midis = flat.Where(n => !n.IsRest).Select(n => n.MidiNumber).ToList();
var gaps = new List<(int i, int gap)>();
for (int i = 1; i < midis.Count; i++) {
    int d = midis[i] - midis[i-1];
    if (d != 1 && d != -1) gaps.Add((i, d));
}
Console.WriteLine($"\nNon-semitone gaps: {gaps.Count}");
foreach (var (i,g) in gaps.Take(10))
    Console.WriteLine($"  index {i}: {midis[i-1]} -> {midis[i]} gap={g}");

// Try A3 start like screenshot - key C, but walk might start at tonic C not A3
// User screenshot starts A3 - maybe one octave mode not two octave?
var gen2 = new MusicSequenceGenerator {
    Key = "A", Scale = "Chromatic", LowestNote = "A3", HighestNote = "C5",
    TimeSignature = TimeSignature.FourFour, MeasureCount = 8,
    RhythmVarietyPercent = 0, SmallestDuration = NoteDuration.Quarter,
    RestChancePercent = 0, UseScaleOrder = true, ChildLevel = 31, RandomSeed = 19
};
var flat2 = MusicSequenceGenerator.Flatten(gen2.GenerateSequence());
Console.WriteLine("\n=== A key A3-C5 first 16 ===");
foreach (var n in flat2.Take(16).Where(n => !n.IsRest))
    Console.WriteLine($"midi={n.MidiNumber} {n.SpelledName} acc={n.Accidental}");
