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

        /// <summary>Read-only view of the notes inside this measure.</summary>
        public IReadOnlyList<MusicNote> Notes => _notes;

        /// <summary>Time signature that governs this measure.</summary>
        public TimeSignature TimeSignature { get; }

        /// <summary>Total beats available in this measure.</summary>
        public double BeatsAvailable => TimeSignature.Beats;

        /// <summary>
        /// Sum of the beat values of all notes currently in the measure.
        /// Each note's beat value is relative to the time signature's beat unit.
        /// </summary>
        public double BeatsUsed
        {
            get
            {
                double total = 0;
                foreach (var note in _notes)
                    total += note.Duration.ToBeatValue() / TimeSignature.BeatUnit.ToBeatValue();
                return total;
            }
        }

        /// <summary>True when <see cref="BeatsUsed"/> has reached or exceeded <see cref="BeatsAvailable"/>.</summary>
        public bool IsFull => BeatsUsed >= BeatsAvailable;

        public Measure(TimeSignature timeSignature)
        {
            TimeSignature = timeSignature ?? throw new ArgumentNullException(nameof(timeSignature));
        }

        /// <summary>
        /// Adds a note to the measure.
        /// The caller is responsible for not overfilling; this method does not throw if the measure is full,
        /// so that partial/pickup measures and editor states can be represented freely.
        /// </summary>
        public void AddNote(MusicNote note)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            _notes.Add(note);
        }

        public override string ToString() =>
            $"Measure [{BeatsUsed:0.##}/{BeatsAvailable} beats, {_notes.Count} note(s), Full={IsFull}]";
    }
}
