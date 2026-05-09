namespace musicmate.Models
{
    /// <summary>
    /// A single note inside a <see cref="PracticeTune"/>.
    /// Carries pitch information and rhythmic duration.
    /// X-position for rendering is computed later by the drawing layer.
    /// </summary>
    public sealed class MusicNote
    {
        /// <summary>MIDI note number (0-127). Middle C = 60.</summary>
        public int MidiNumber { get; }

        /// <summary>
        /// Spelled note name including octave, e.g. "C4", "F#5", "Bb3".
        /// Matches the convention used by <c>NoteSessionService.MidiToNoteName</c>.
        /// </summary>
        public string SpelledName { get; }

        /// <summary>Rhythmic duration of this note.</summary>
        public NoteDuration Duration { get; }

        public MusicNote(int midiNumber, string spelledName, NoteDuration duration, bool isRest = false)
        {
            if (!isRest && string.IsNullOrWhiteSpace(spelledName))
                throw new ArgumentException("SpelledName must not be empty.", nameof(spelledName));

            MidiNumber  = midiNumber;
            SpelledName = spelledName;
            Duration    = duration;
            IsRest      = isRest;
        }

        /// <summary>When true this note slot is a rest — no pitch, just duration.</summary>
        public bool IsRest { get; }

        /// <summary>Creates a rest of the given duration.</summary>
        public static MusicNote Rest(NoteDuration duration) => new(0, "rest", duration, isRest: true);

        public override string ToString() => IsRest ? $"Rest ({Duration})" : $"{SpelledName} ({Duration})";
    }
}
