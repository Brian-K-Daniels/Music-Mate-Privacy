using System.Diagnostics;
using System.Text;
using Microsoft.Maui.Graphics;
using musicmate.Models;
#if ANDROID
using Android.Util;
#endif

namespace musicmate.Diagnostics;

/// <summary>
/// Temporary device diagnostics for C4 / C#4 / D4 (MIDI 60–62) through layout and draw.
/// </summary>
public static class ChromaticMidi61Diagnostics
{
    public const int MidiC4 = 60;
    public const int MidiCs4 = 61;
    public const int MidiD4 = 62;

    private static readonly int[] TracedMidis = [MidiC4, MidiCs4, MidiD4];

    public static bool IsEnabled =>
#if DEBUG
        true;
#else
        false;
#endif

    public static string LastRendererInputSummary { get; private set; } = string.Empty;
    public static string LastDrawTableSummary { get; private set; } = string.Empty;
    public static string LastPlaybackSummary { get; private set; } = string.Empty;
    public static string? FirstDivergenceForMidi61 { get; private set; }

    private static readonly Dictionary<int, TraceEntry> _entries = new();
    private static string _currentStaff = string.Empty;

    public sealed class TraceEntry
    {
        public int Midi;
        public string SpelledName = string.Empty;
        public string Staff = string.Empty;
        public int ListIndex = -1;
        public int? Measure;
        public double? Beat;
        public float LayoutX;
        public float LayoutY;
        public float ScreenX;
        public float ScreenY;
        public float NoteHeadLeft;
        public float NoteHeadTop;
        public float NoteHeadW;
        public float NoteHeadH;
        public float AccidentalLeft;
        public float AccidentalTop;
        public float AccidentalW;
        public float AccidentalH;
        public bool InRendererInput;
        public bool DrawLoopReached;
        public bool LayoutFinite = true;
        public bool DrawHeadFlag = true;
        public bool DrawAccidentalFlag;
        public string AccidentalType = "none";
        public string ClipBounds = string.Empty;
        public bool Culled;
        public bool DrawNoteHeadCalled;
        public bool DrawAccidentalCalled;
        public bool MarkerDrawn;
        public string? SkipBranch;
        public string? PlaybackLog;
    }

    public static void ResetDrawPass(string staffLabel)
    {
        if (!IsEnabled)
            return;

        _currentStaff = staffLabel;
        foreach (int midi in TracedMidis)
        {
            if (!_entries.TryGetValue(midi, out var entry))
            {
                entry = new TraceEntry { Midi = midi };
                _entries[midi] = entry;
            }

            entry.Staff = staffLabel;
            entry.DrawLoopReached = false;
            entry.DrawNoteHeadCalled = false;
            entry.DrawAccidentalCalled = false;
            entry.MarkerDrawn = false;
            entry.Culled = false;
            entry.SkipBranch = null;
        }
    }

    public static void LogRendererInput(string staffLabel, IReadOnlyList<GeneratedNote> notes)
    {
        if (!IsEnabled)
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"[RendererInput] staff={staffLabel} count={notes.Count}");
        foreach (int midi in TracedMidis)
        {
            int idx = FindIndex(notes, midi);
            var entry = GetOrCreate(midi);
            entry.InRendererInput = idx >= 0;
            entry.Staff = staffLabel;
            if (idx >= 0)
            {
                var n = notes[idx];
                entry.ListIndex = idx;
                entry.SpelledName = n.SpelledName;
                entry.Measure = n.MeasureIndex;
                entry.Beat = n.BeatPosition;
                sb.AppendLine($"  idx={idx} midi={midi} name={n.SpelledName} meas={n.MeasureIndex} beat={n.BeatPosition}");
            }
            else
            {
                entry.ListIndex = -1;
                sb.AppendLine($"  midi={midi} ABSENT");
                RecordFirstDivergence(midi, $"Renderer input ({staffLabel}): MIDI {midi} absent");
            }
        }

        LastRendererInputSummary = sb.ToString().TrimEnd();
        Emit(LastRendererInputSummary);
    }

    public static TraceEntry GetOrCreate(int midi) =>
        _entries.TryGetValue(midi, out var e)
            ? e
            : (_entries[midi] = new TraceEntry { Midi = midi });

    public static void RecordSkip(int midi, string branch)
    {
        if (!IsEnabled || !IsTraced(midi))
            return;

        var entry = GetOrCreate(midi);
        entry.SkipBranch ??= branch;
        RecordFirstDivergence(midi, branch);
        Emit($"[DrawSkip] staff={_currentStaff} midi={midi} branch={branch}");
    }

    public static void RecordDrawLoop(
        GeneratedNote note,
        int index,
        float layoutX,
        float ny,
        RectF dirtyRect,
        float safeLeft,
        float layoutRightLimit,
        float headLeft,
        float headTop,
        float headW,
        float headH,
        bool drawAccidental,
        string accidentalType)
    {
        if (!IsEnabled || !IsTraced(note.MidiNumber))
            return;

        var entry = GetOrCreate(note.MidiNumber);
        entry.DrawLoopReached = true;
        entry.ListIndex = index;
        entry.SpelledName = note.SpelledName;
        entry.Measure = note.MeasureIndex;
        entry.Beat = note.BeatPosition;
        entry.LayoutX = layoutX;
        entry.LayoutY = ny;
        entry.ScreenX = layoutX;
        entry.ScreenY = ny;
        entry.LayoutFinite = float.IsFinite(layoutX);
        entry.NoteHeadLeft = headLeft;
        entry.NoteHeadTop = headTop;
        entry.NoteHeadW = headW;
        entry.NoteHeadH = headH;
        entry.DrawHeadFlag = !note.IsRest;
        entry.DrawAccidentalFlag = drawAccidental;
        entry.AccidentalType = accidentalType;
        entry.ClipBounds = $"dirty=({dirtyRect.X:F1},{dirtyRect.Y:F1},{dirtyRect.Width:F1}x{dirtyRect.Height:F1}) safeL={safeLeft:F1} layoutR={layoutRightLimit:F1}";

        var headRect = new RectF(headLeft, headTop, headW, headH);
        bool inDirty = dirtyRect.Width <= 0 || dirtyRect.Height <= 0 || dirtyRect.IntersectsWith(headRect);
        bool xInBounds = layoutX >= safeLeft - 1f && layoutX <= layoutRightLimit + 1f;
        entry.Culled = !inDirty || !xInBounds;

        if (entry.Culled)
            RecordFirstDivergence(note.MidiNumber, $"Culled/in-bounds ({_currentStaff}): inDirty={inDirty} xInBounds={xInBounds}");
    }

    public static void RecordDrawNoteHead(int midi)
    {
        if (!IsEnabled || !IsTraced(midi))
            return;
        GetOrCreate(midi).DrawNoteHeadCalled = true;
    }

    public static void RecordDrawAccidental(int midi, bool called, string accidentalType)
    {
        if (!IsEnabled || !IsTraced(midi))
            return;
        var entry = GetOrCreate(midi);
        entry.DrawAccidentalCalled = called;
        if (!string.IsNullOrEmpty(accidentalType))
            entry.AccidentalType = accidentalType;
    }

    public static void RecordMarker(int midi)
    {
        if (!IsEnabled || !IsTraced(midi))
            return;
        GetOrCreate(midi).MarkerDrawn = true;
    }

    public static void RecordPlayback(int midi, string spelledName)
    {
        if (!IsEnabled || !IsTraced(midi))
            return;

        var line = $"PLAY {midi} {spelledName}";
        GetOrCreate(midi).PlaybackLog = line;
        LastPlaybackSummary = string.Join(" | ", TracedMidis.Select(m => _entries.TryGetValue(m, out var e) ? e.PlaybackLog ?? $"PLAY {m} (not yet)" : $"PLAY {m} (not yet)"));
        Emit(line);
    }

    public static void FinalizeDrawPass()
    {
        if (!IsEnabled)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("Draw trace (C4 / C#4 / D4):");
        sb.AppendLine(FormatTableRow("MIDI", e => e.Midi.ToString()));
        sb.AppendLine(FormatTableRow("name", e => e.SpelledName));
        sb.AppendLine(FormatTableRow("staff", e => e.Staff));
        sb.AppendLine(FormatTableRow("local X", e => e.LayoutX.ToString("F1")));
        sb.AppendLine(FormatTableRow("screen X", e => e.ScreenX.ToString("F1")));
        sb.AppendLine(FormatTableRow("screen Y", e => e.ScreenY.ToString("F1")));
        sb.AppendLine(FormatTableRow("draw head", e => e.DrawNoteHeadCalled.ToString().ToLowerInvariant()));
        sb.AppendLine(FormatTableRow("accidental", e => e.AccidentalType));
        sb.AppendLine(FormatTableRow("culled", e => e.Culled.ToString().ToLowerInvariant()));
        sb.AppendLine(FormatTableRow("draw called", e => e.DrawNoteHeadCalled.ToString().ToLowerInvariant()));
        sb.AppendLine(FormatTableRow("marker", e => e.MarkerDrawn.ToString().ToLowerInvariant()));
        sb.AppendLine(FormatTableRow("skip branch", e => e.SkipBranch ?? "-"));

        foreach (int midi in TracedMidis)
        {
            if (!_entries.TryGetValue(midi, out var entry))
                continue;
            Emit($"[DrawTrace] midi={midi} staff={entry.Staff} idx={entry.ListIndex} " +
                 $"layout=({entry.LayoutX:F1},{entry.LayoutY:F1}) screen=({entry.ScreenX:F1},{entry.ScreenY:F1}) " +
                 $"head=({entry.NoteHeadLeft:F1},{entry.NoteHeadTop:F1},{entry.NoteHeadW:F1}x{entry.NoteHeadH:F1}) " +
                 $"drawHead={entry.DrawNoteHeadCalled} acc={entry.AccidentalType} accDraw={entry.DrawAccidentalCalled} " +
                 $"culled={entry.Culled} marker={entry.MarkerDrawn} skip={entry.SkipBranch ?? "-"}");
        }

        LastDrawTableSummary = sb.ToString().TrimEnd();
        Emit(LastDrawTableSummary);

        DetectFirstBehavioralDivergence();
    }

    public static string CompactStatusLine()
    {
        if (!IsEnabled)
            return string.Empty;

        static string Cell(int midi, Func<TraceEntry, string> pick)
        {
            return _entries.TryGetValue(midi, out var e) ? pick(e) : "?";
        }

        return $"61 trace: in={Cell(MidiCs4, e => e.InRendererInput.ToString())} " +
               $"draw={Cell(MidiCs4, e => e.DrawNoteHeadCalled.ToString())} " +
               $"marker={Cell(MidiCs4, e => e.MarkerDrawn.ToString())} " +
               $"play={Cell(MidiCs4, e => e.PlaybackLog ?? "-")} " +
               $"Δ={FirstDivergenceForMidi61 ?? "-"}";
    }

    private static void DetectFirstBehavioralDivergence()
    {
        if (!_entries.TryGetValue(MidiCs4, out var cs))
            return;
        if (!_entries.TryGetValue(MidiC4, out var c) || !_entries.TryGetValue(MidiD4, out var d))
            return;

        if (FirstDivergenceForMidi61 != null)
            return;

        if (c.InRendererInput && !cs.InRendererInput)
        {
            FirstDivergenceForMidi61 = "Renderer input: C4 present, C#4 absent";
            return;
        }

        if (d.InRendererInput && !cs.InRendererInput)
        {
            FirstDivergenceForMidi61 = "Renderer input: D4 present, C#4 absent";
            return;
        }

        if (c.DrawLoopReached && !cs.DrawLoopReached)
        {
            FirstDivergenceForMidi61 = $"Draw loop: C4 reached, C#4 not ({cs.SkipBranch ?? "never entered"})";
            return;
        }

        if (d.DrawLoopReached && !cs.DrawLoopReached)
        {
            FirstDivergenceForMidi61 = $"Draw loop: D4 reached, C#4 not ({cs.SkipBranch ?? "never entered"})";
            return;
        }

        if (c.DrawNoteHeadCalled && !cs.DrawNoteHeadCalled)
        {
            FirstDivergenceForMidi61 = $"DrawNote: C4 called, C#4 not ({cs.SkipBranch ?? cs.Culled.ToString()})";
            return;
        }

        if (d.DrawNoteHeadCalled && !cs.DrawNoteHeadCalled)
        {
            FirstDivergenceForMidi61 = $"DrawNote: D4 called, C#4 not ({cs.SkipBranch ?? cs.Culled.ToString()})";
            return;
        }

        if (!cs.MarkerDrawn && cs.DrawLoopReached && float.IsFinite(cs.LayoutX))
            FirstDivergenceForMidi61 = "Marker: draw loop reached but '61' marker not drawn";
    }

    private static void RecordFirstDivergence(int midi, string branch)
    {
        if (midi != MidiCs4 || FirstDivergenceForMidi61 != null)
            return;
        FirstDivergenceForMidi61 = branch;
    }

    private static string FormatTableRow(string label, Func<TraceEntry, string> pick)
    {
        static string Pad(string s, int w) => s.Length >= w ? s : s.PadRight(w);
        const int col = 9;
        var c4 = _entries.TryGetValue(MidiC4, out var a) ? pick(a) : "?";
        var cs = _entries.TryGetValue(MidiCs4, out var b) ? pick(b) : "?";
        var d4 = _entries.TryGetValue(MidiD4, out var c) ? pick(c) : "?";
        return $"{Pad(label, 12)}{Pad(c4, col)}{Pad(cs, col)}{d4}";
    }

    private static int FindIndex(IReadOnlyList<GeneratedNote> notes, int midi)
    {
        for (int i = 0; i < notes.Count; i++)
        {
            if (!notes[i].IsRest && notes[i].MidiNumber == midi)
                return i;
        }
        return -1;
    }

    private static bool IsTraced(int midi) => midi is MidiC4 or MidiCs4 or MidiD4;

    private static void Emit(string message)
    {
        Debug.WriteLine($"[Midi61Diag] {message}");
#if ANDROID
        Log.Info("MusicMate", $"[Midi61Diag] {message}");
#endif
    }
}
