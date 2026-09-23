using musicmate.Models;
using musicmate.Services;
using musicmate.ViewModels;

namespace musicmate.Drawables;

/// <summary>
/// Draws a static written-pitch catalog on one or more staff systems for Note Mastery.
/// Quarter-note heads only; tap hit-testing via <see cref="HitTest"/>.
/// </summary>
public sealed class MasteryStaffDrawable : IDrawable
{
    private readonly List<(RectF Bounds, NoteMasteryItemViewModel Note)> _hitRegions = new();

    public IReadOnlyList<NoteMasteryItemViewModel> Notes { get; set; } =
        Array.Empty<NoteMasteryItemViewModel>();

    public bool PreferBassClef { get; set; }
    public Color StaffInk { get; set; } = Colors.Black;
    public Color MasteredColor { get; set; } = Color.FromArgb("#1B7A3A");
    public Color ImprovingColor { get; set; } = Color.FromArgb("#0D47A1");
    public Color NeedsPracticeColor { get; set; } = Color.FromArgb("#C43E00");
    public Color NotAttemptedColor { get; set; } = Color.FromArgb("#5F6368");
    public Color SelectedHaloColor { get; set; } = Color.FromArgb("#FFD54F");

    public float DesiredHeight { get; private set; } = 120f;

    public float ComputeDesiredHeight(float width)
    {
        if (Notes.Count == 0)
        {
            DesiredHeight = 80f;
            return DesiredHeight;
        }

        const float leftPad = 8f;
        const float rightPad = 8f;
        const float clefWidth = 36f;
        const float noteSpacing = 28f;
        const float staffLineSpacing = 10f;
        const float staffHeight = staffLineSpacing * 4f;
        const float systemHeight = staffHeight + 56f;

        float usable = Math.Max(80f, width - leftPad - rightPad - clefWidth);
        int perSystem = Math.Max(4, (int)(usable / noteSpacing));
        int systemCount = (int)Math.Ceiling(Notes.Count / (double)perSystem);
        DesiredHeight = systemCount * systemHeight + 24f;
        return DesiredHeight;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        _hitRegions.Clear();
        canvas.SaveState();
        try
        {
            canvas.FillColor = Colors.Transparent;
            canvas.FillRectangle(dirtyRect);

            if (Notes.Count == 0)
            {
                canvas.FontColor = StaffInk;
                canvas.FontSize = 14;
                canvas.DrawString(
                    "No notes in the current range",
                    dirtyRect,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Center);
                DesiredHeight = 80f;
                return;
            }

            const float leftPad = 8f;
            const float rightPad = 8f;
            const float clefWidth = 36f;
            const float noteSpacing = 28f;
            const float staffLineSpacing = 10f;
            const float staffHeight = staffLineSpacing * 4f;
            const float systemHeight = staffHeight + 100f;  //  2026.07.18 1637  56f;

            float usable = Math.Max(80f, dirtyRect.Width - leftPad - rightPad - clefWidth);
            int perSystem = Math.Max(4, (int)(usable / noteSpacing));
            int systemCount = (int)Math.Ceiling(Notes.Count / (double)perSystem);
            DesiredHeight = systemCount * systemHeight + 24f;

            for (int s = 0; s < systemCount; s++)
            {
                float top = dirtyRect.Y + 16f + s * systemHeight;
                float staffTop = top;
                float staffBot = staffTop + staffHeight;
                float staffMid = staffTop + staffLineSpacing * 2f;

                canvas.StrokeColor = StaffInk;
                canvas.StrokeSize = 1.25f;
                for (int line = 0; line < 5; line++)
                {
                    float y = staffTop + line * staffLineSpacing;
                    canvas.DrawLine(leftPad, y, dirtyRect.Width - rightPad, y);
                }

                canvas.FontColor = StaffInk;
                canvas.FontSize = PreferBassClef ? 36 : 40;
                string clef = PreferBassClef ? "𝄢" : "𝄞";
                canvas.DrawString(
                    clef,
                    leftPad,
                    staffTop - (PreferBassClef ? 4f : 18f),
                    clefWidth,
                    staffHeight + 24f,
                    HorizontalAlignment.Left,
                    VerticalAlignment.Center);

                int start = s * perSystem;
                int end = Math.Min(Notes.Count, start + perSystem);
                for (int i = start; i < end; i++)
                {
                    var note = Notes[i];
                    float x = leftPad + clefWidth + (i - start) * noteSpacing + noteSpacing * 0.5f;
                    float ny = NoteY(note, staffTop, staffLineSpacing);
                    DrawLedgers(canvas, x, ny, staffTop, staffBot, staffLineSpacing);
                    DrawNoteHead(canvas, note, x, ny, staffLineSpacing);
                    float eToMarkerY = ny > staffBot ? staffTop - 23f : staffBot + 4f;  //  2026.07.18 1651  10f 30f hides symbol

                    DrawMarker(canvas, note, x, eToMarkerY);

                    float hit = Math.Max(24f, noteSpacing);
                    _hitRegions.Add((new RectF(x - hit * 0.5f, staffTop - 12f, hit, systemHeight - 8f), note));
                }
            }
        }
        finally
        {
            canvas.RestoreState();
        }
    }

    public NoteMasteryItemViewModel? HitTest(float x, float y)
    {
        foreach (var (bounds, note) in _hitRegions)
        {
            if (bounds.Contains(x, y))
                return note;
        }

        return null;
    }

    private void DrawNoteHead(
        ICanvas canvas, NoteMasteryItemViewModel note, float x, float ny, float sls)
    {
        var fill = ColorFor(note.MasteryState);
        float r = sls * 0.55f;

        if (note.IsSelected)
        {
            canvas.FillColor = SelectedHaloColor.WithAlpha(0.55f);
            canvas.FillCircle(x, ny, r * 1.85f);
        }

        // Filled vs outlined distinguishes states beyond color.
        bool filled = note.MasteryState is NoteMasteryState.Mastered or NoteMasteryState.Improving;
        canvas.StrokeColor = fill;
        canvas.StrokeSize = note.MasteryState == NoteMasteryState.NeedsPractice ? 2.4f : 1.6f;
        canvas.FillColor = filled ? fill : Colors.Transparent;

        canvas.SaveState();
        canvas.Rotate(-20, x, ny);
        var oval = new RectF(x - r * 1.25f, ny - r * 0.85f, r * 2.5f, r * 1.7f);
        if (filled)
            canvas.FillEllipse(oval);
        else
            canvas.DrawEllipse(oval);

        if (note.MasteryState == NoteMasteryState.NeedsPractice)
        {
            // Dashed-style second ring for Practice Next
            canvas.StrokeSize = 1.2f;
            canvas.DrawEllipse(new RectF(
                oval.X - 2f, oval.Y - 2f, oval.Width + 4f, oval.Height + 4f));
        }

        canvas.RestoreState();

        // Stem (quarter note) — always up for catalog readability
        canvas.StrokeColor = fill;
        canvas.StrokeSize = 1.5f;
        float stemX = x + r * 1.1f;
        canvas.DrawLine(stemX, ny, stemX, ny - sls * 3.2f);

        // Accidental / letter hint under marker area is drawn separately as marker.
        string accidental = AccidentalGlyph(note.WrittenNoteName);
        if (!string.IsNullOrEmpty(accidental))
        {
            canvas.FontColor = fill;
            canvas.FontSize = 14;
            canvas.DrawString(
                accidental,
                x - r * 3.2f,
                ny - sls,
                r * 2.2f,
                sls * 2f,
                HorizontalAlignment.Right,
                VerticalAlignment.Center);
        }
    }

    private void DrawMarker(ICanvas canvas, NoteMasteryItemViewModel note, float x, float y)
    {
        var color = ColorFor(note.MasteryState);
        canvas.FontColor = color;
        canvas.FontSize = 11;
        canvas.DrawString(
            note.StateMarker,
            x - 10f,
            y,
            30f,  //  2026.07.18 1713  20f
            21f,      //  2026.07.18 1713  14f
            HorizontalAlignment.Center,
            VerticalAlignment.Top);

        // Written pitch name uses the same status color as the marker symbol.
        canvas.FontSize = 9;
        canvas.DrawString(
            note.WrittenNoteName,
            x - 18f,
            y + 13f,
            36f,
            12f,
            HorizontalAlignment.Center,
            VerticalAlignment.Top);
    }

    private void DrawLedgers(
        ICanvas canvas, float x, float ny, float staffTop, float staffBot, float sls)
    {
        canvas.StrokeColor = StaffInk;
        canvas.StrokeSize = 1.25f;
        float hw = sls * 1.2f;

        if (ny < staffTop - 1f)
        {
            float cur = staffTop - sls;
            while (cur >= ny - 1f)
            {
                canvas.DrawLine(x - hw, cur, x + hw, cur);
                cur -= sls;
            }
        }

        if (ny > staffBot + 1f)
        {
            float cur = staffBot + sls;
            while (cur <= ny + 1f)
            {
                canvas.DrawLine(x - hw, cur, x + hw, cur);
                cur += sls;
            }
        }
    }

    private Color ColorFor(NoteMasteryState state) => state switch
    {
        NoteMasteryState.Mastered => MasteredColor,
        NoteMasteryState.Improving => ImprovingColor,
        NoteMasteryState.NeedsPractice => NeedsPracticeColor,
        _ => NotAttemptedColor,
    };

    /// <summary>
    /// Positions by written letter/octave (Music-page spelling), not chromatic MIDI mapping,
    /// so E#/Cb/Fb sit on the correct staff degree.
    /// Treble: E4 = bottom line; Bass: G2 = bottom line.
    /// </summary>
    private float NoteY(NoteMasteryItemViewModel note, float staffTop, float sls)
    {
        string name = note.WrittenNoteName;
        char letter = string.IsNullOrWhiteSpace(name) ? 'C' : char.ToUpperInvariant(name[0]);
        int octave = NoteSessionService.ParseOctaveFromSpelledName(name);
        int diatonic = LetterOctaveToDiatonicSteps(letter, octave);
        int reference = PreferBassClef
            ? LetterOctaveToDiatonicSteps('G', 2)
            : LetterOctaveToDiatonicSteps('E', 4);
        return staffTop + 4 * sls - (diatonic - reference) * (sls * 0.5f);
    }

    private static int LetterOctaveToDiatonicSteps(char letter, int octave)
    {
        int noteVal = char.ToUpperInvariant(letter) switch
        {
            'C' => 0,
            'D' => 1,
            'E' => 2,
            'F' => 3,
            'G' => 4,
            'A' => 5,
            'B' => 6,
            _ => 0
        };
        return noteVal + octave * 7;
    }

    private static string AccidentalGlyph(string writtenName)
    {
        if (writtenName.Contains("##", StringComparison.Ordinal)) return "𝄪";
        if (writtenName.Contains("bb", StringComparison.Ordinal)) return "𝄫";
        if (writtenName.Contains('#')) return "♯";
        if (writtenName.Contains('b') && writtenName.Length > 2) return "♭";
        return string.Empty;
    }
}
