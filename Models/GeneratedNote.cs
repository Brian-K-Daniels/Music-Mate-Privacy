namespace musicmate.Models
{
    /// <summary>
    /// Version 2 runtime note model.
    /// Represents a single note (or rest) as it will be presented to the player during a
    /// practice session.  This class is deliberately separate from the v1 <c>NoteInfo</c>
    /// class that lives in <c>NoteSessionService</c>, so v1 logic remains unchanged.
    /// </summary>
    public sealed class GeneratedNote
    {
        // ── Pitch identity ────────────────────────────────────────────────────────

        /// <summary>
        /// MIDI note number (0-127). Middle C = 60.
        /// 0 when this slot is a rest.
        /// </summary>
        public int MidiNumber { get; init; }

        /// <summary>
        /// Diatonic pitch letter: 'C', 'D', 'E', 'F', 'G', 'A', or 'B'.
        /// Empty when this slot is a rest.
        /// </summary>
        public char Letter { get; init; }

        /// <summary>
        /// Octave number in scientific pitch notation (e.g. middle C is octave 4).
        /// 0 when this slot is a rest.
        /// </summary>
        public int Octave { get; init; }

        /// <summary>
        /// Accidental applied to the note.
        /// None = natural, Sharp = ♯, Flat = ♭, DoubleSharp = 𝄪, DoubleFlat = 𝄫.
        /// </summary>
        public Accidental Accidental { get; init; } = Accidental.None;

        /// <summary>
        /// Full spelled name including accidental and octave, e.g. "F#5", "Bb3", "C4".
        /// Derived automatically; settable for legacy compatibility.
        /// </summary>
        public string SpelledName { get; init; } = string.Empty;

        /// <summary>Target frequency in Hz for pitch detection matching.</summary>
        public double TargetFrequency { get; init; }

        // ── Rhythm ────────────────────────────────────────────────────────────────

        /// <summary>Rhythmic duration (whole, half, quarter, eighth, sixteenth).</summary>
        public NoteDuration Duration { get; init; } = NoteDuration.Quarter;

        /// <summary>When true this slot is a rest — no pitch is expected from the player.</summary>
        public bool IsRest { get; init; }

        // ── Measure position ──────────────────────────────────────────────────────

        /// <summary>
        /// Zero-based index of the measure this note belongs to.
        /// Null when measure context is not applicable (e.g. Random mode).
        /// </summary>
        public int? MeasureIndex { get; init; }

        /// <summary>
        /// Beat position within the measure, expressed as a beat count from beat 1.
        /// E.g. beat 1 = 0.0, beat 2 = 1.0, the "and" of beat 2 = 1.5.
        /// Null when timing context is not applicable.
        /// </summary>
        public double? BeatPosition { get; init; }

        // ── Session state ─────────────────────────────────────────────────────────

        /// <summary>
        /// True once the player has successfully played this note within the
        /// acceptable pitch tolerance.  Rests are marked correct automatically.
        /// </summary>
        public bool IsPlayedCorrectly { get; set; }

        /// <summary>
        /// Cents deviation from <see cref="TargetFrequency"/> when the note was
        /// accepted as correct.  Zero for rests or not-yet-played notes.
        /// </summary>
        public int CentsDeviation { get; set; }

        // ── Rendering hint ────────────────────────────────────────────────────────

        /// <summary>
        /// Horizontal canvas position (in device-independent units) assigned by the
        /// drawing layer.  Set to 0 until layout has been performed.
        /// </summary>
        public float RenderX { get; set; }

        // ── Factory helpers ───────────────────────────────────────────────────────

        // ── Derived helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Duration of this note expressed as a beat count (quarter note = 1.0).
        /// Convenience alias for <c>Duration.ToBeatValue()</c>.
        /// Useful for layout calculations that need both position and width.
        /// </summary>
        public double BeatDuration => Duration.ToBeatValue();

        /// <summary>Creates a rest slot of the given duration.</summary>
        public static GeneratedNote Rest(NoteDuration duration, int? measureIndex = null, double? beatPosition = null)
            => new()
            {
                IsRest = true,
                Duration = duration,
                SpelledName = "rest",
                MeasureIndex = measureIndex,
                BeatPosition = beatPosition,
                IsPlayedCorrectly = true   // rests require no player action
            };

        public override string ToString()
            => IsRest ? $"Rest ({Duration})" : $"{SpelledName} ({Duration})";
    }

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Accidental modifier for a diatonic pitch letter.
    /// </summary>
    public enum Accidental
    {
        /// <summary>No accidental — natural pitch.</summary>
        None,

        /// <summary>Single sharp (♯): raises pitch by one semitone.</summary>
        Sharp,

        /// <summary>Single flat (♭): lowers pitch by one semitone.</summary>
        Flat,

        /// <summary>Double sharp (𝄪): raises pitch by two semitones.</summary>
        DoubleSharp,

        /// <summary>Double flat (𝄫): lowers pitch by two semitones.</summary>
        DoubleFlat,

        /// <summary>Natural sign (♮): explicitly cancels a key-signature accidental.</summary>
        Natural
    }
}
