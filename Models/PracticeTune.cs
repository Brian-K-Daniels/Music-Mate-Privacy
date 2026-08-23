namespace musicmate.Models
{
    /// <summary>
    /// A short named piece of music divided into <see cref="Measure"/>s.
    /// This is a pure data container — no audio, no rendering, no session logic.
    /// Future steps will let <c>NoteSessionService</c> optionally load a
    /// <see cref="PracticeTune"/> instead of its current randomly-generated note list.
    /// </summary>
    public sealed class PracticeTune
    {
        private readonly List<Measure> _measures = new();

        /// <summary>Display name of the tune.</summary>
        public string Title { get; set; }

        /// <summary>Time signature that applies to all measures unless overridden per-measure.</summary>
        public TimeSignature TimeSignature { get; }

        /// <summary>Written key signature for this tune (e.g. "C", "G"). Null when not fixed.</summary>
        public string? Key { get; }

        /// <summary>Read-only view of the measures in this tune.</summary>
        public IReadOnlyList<Measure> Measures => _measures;

        /// <summary>Flat ordered list of every note across all measures.</summary>
        public IEnumerable<MusicNote> AllNotes => _measures.SelectMany(m => m.Notes);

        /// <summary>Total number of notes across the whole tune.</summary>
        public int NoteCount => _measures.Sum(m => m.Notes.Count);

        public PracticeTune(string title, TimeSignature? timeSignature = null, string? key = null)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title must not be empty.", nameof(title));

            Title = title;
            TimeSignature = timeSignature ?? TimeSignature.FourFour;
            Key = string.IsNullOrWhiteSpace(key) ? null : key;
        }

        /// <summary>Appends a measure to the tune.</summary>
        public void AddMeasure(Measure measure)
        {
            if (measure == null) throw new ArgumentNullException(nameof(measure));
            _measures.Add(measure);
        }

        /// <summary>
        /// Convenience: creates a new <see cref="Measure"/> using the tune's time signature,
        /// appends it, and returns it so notes can be added fluently.
        /// </summary>
        public Measure AppendMeasure()
        {
            var m = new Measure(TimeSignature);
            _measures.Add(m);
            return m;
        }

        public override string ToString() =>
            $"\"{Title}\" — {TimeSignature}, {_measures.Count} measure(s), {NoteCount} note(s)";
    }
}
