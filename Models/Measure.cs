namespace musicmate.Models
{
    /// <summary>
    /// A single bar of music.
    /// Knows its governing <see cref="TimeSignature"/> and can report
    /// how full it is relative to the available beats.
    /// </summary>
    public sealed class Measure
    {
        private readonly List<MusicNote> _notes = new();

        // ── v2: GeneratedNote slots (populated by the v2 session engine) ──────────
        private readonly List<GeneratedNote> _generatedNotes = new();

        /// <summary>Read-only view of the v1 <see cref="MusicNote"/> slots in this measure.</summary>
        public IReadOnlyList<MusicNote> Notes => _notes;

        /// <summary>
        /// Read-only view of the v2 <see cref="GeneratedNote"/> slots in this measure.
        /// Populated only when this measure is managed by the v2 session engine.
        /// </summary>
        public IReadOnlyList<GeneratedNote> GeneratedNotes => _generatedNotes;

        /// <summary>Time signature that governs this measure.</summary>
        public TimeSignature TimeSignature { get; }

        /// <summary>Total quarter-beat capacity of this measure (mirrors <see cref="TimeSignature.TotalBeats"/>).</summary>
        public double BeatsAvailable => TimeSignature.TotalBeats;

        /// <summary>
        /// Sum of the quarter-beat values of all notes currently in the measure.
        /// Counts both v1 <see cref="MusicNote"/> and v2 <see cref="GeneratedNote"/> slots.
        /// </summary>
        public double BeatsUsed
        {
            get
            {
                double total = 0;
                foreach (var note in _notes)
                    total += note.Duration.ToBeatValue();
                foreach (var note in _generatedNotes)
                    total += note.Duration.ToBeatValue();
                return total;
            }
        }

        /// <summary>Beats remaining before the measure is full.</summary>
        public double BeatsRemaining => Math.Max(0.0, BeatsAvailable - BeatsUsed);

        /// <summary>True when <see cref="BeatsUsed"/> has reached or exceeded <see cref="BeatsAvailable"/>.</summary>
        public bool IsFull => BeatsUsed >= BeatsAvailable - 1e-9;

        public Measure(TimeSignature timeSignature)
        {
            TimeSignature = timeSignature ?? throw new ArgumentNullException(nameof(timeSignature));
        }

        // ── v1 note management ────────────────────────────────────────────────────

        /// <summary>
        /// Adds a v1 <see cref="MusicNote"/> to the measure without overflow checking.
        /// Partial/pickup measures are allowed — the caller controls fullness.
        /// </summary>
        public void AddNote(MusicNote note)
        {
            if (note is null) throw new ArgumentNullException(nameof(note));
            _notes.Add(note);
        }

        /// <summary>
        /// Returns true when adding <paramref name="note"/> would push <see cref="BeatsUsed"/>
        /// beyond <see cref="BeatsAvailable"/>.
        /// </summary>
        public bool WouldExceed(MusicNote note)
        {
            if (note is null) throw new ArgumentNullException(nameof(note));
            return BeatsUsed + note.Duration.ToBeatValue() > BeatsAvailable + 1e-9;
        }

        // ── v2 note management ────────────────────────────────────────────────────

        /// <summary>
        /// Adds a v2 <see cref="GeneratedNote"/> to the measure without overflow checking.
        /// </summary>
        public void AddNote(GeneratedNote note)
        {
            if (note is null) throw new ArgumentNullException(nameof(note));
            _generatedNotes.Add(note);
        }

        /// <summary>
        /// Returns true when adding <paramref name="note"/> would push <see cref="BeatsUsed"/>
        /// beyond <see cref="BeatsAvailable"/>.
        /// </summary>
        public bool WouldExceed(GeneratedNote note)
        {
            if (note is null) throw new ArgumentNullException(nameof(note));
            return BeatsUsed + note.Duration.ToBeatValue() > BeatsAvailable + 1e-9;
        }

        /// <summary>
        /// Removes trailing rest <see cref="GeneratedNote"/> slots from the end of the measure.
        /// Stops at the last pitched note so the sequence ends cleanly.
        /// </summary>
        public void TrimTrailingRests()
        {
            int lastPitched = -1;
            for (int i = 0; i < _generatedNotes.Count; i++)
                if (!_generatedNotes[i].IsRest) lastPitched = i;
            if (lastPitched >= 0 && lastPitched < _generatedNotes.Count - 1)
                _generatedNotes.RemoveRange(lastPitched + 1, _generatedNotes.Count - lastPitched - 1);
        }

        public override string ToString() =>
            $"Measure [{TimeSignature}  {BeatsUsed:0.##}/{BeatsAvailable} beats, " +
            $"{_notes.Count + _generatedNotes.Count} note(s), Full={IsFull}]";
    }
}
