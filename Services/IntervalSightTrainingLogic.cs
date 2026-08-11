using musicmate.Drawables;
using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Within-measure consecutive pitched-note interval quiz for Interval Sight Training.
    /// Answers are absolute semitone magnitude (0–12); direction does not matter.
    /// Pairs never cross bar lines; leaps outside 0…12 are not testable.
    /// </summary>
    public sealed class IntervalSightTrainingLogic
    {
        public const int MinAbsoluteSemitones = 0;
        public const int MaxAbsoluteSemitones = 12;

        /// <summary>Legacy aliases kept for melody/pool helpers.</summary>
        public const int MinSignedSemitones = -MaxAbsoluteSemitones;
        public const int MaxSignedSemitones = MaxAbsoluteSemitones;

        public sealed class SessionStats
        {
            public int IntervalsTested { get; set; }
            public int CorrectOnFirstAttempt { get; set; }
            public int IncorrectAttempts { get; set; }

            public double FirstAttemptPercent
                => IntervalsTested <= 0
                    ? 0
                    : 100.0 * CorrectOnFirstAttempt / IntervalsTested;

            public string FormatSummary()
                => $"Sight Training: {CorrectOnFirstAttempt}/{IntervalsTested} first-try " +
                   $"({FirstAttemptPercent:0.#}%), {IncorrectAttempts} wrong tap(s)";
        }

        /// <summary>Active pair as indices into the flat <see cref="GeneratedNote"/> list.</summary>
        public readonly struct NotePair
        {
            public int AnchorFlatIndex { get; init; }
            public int TargetFlatIndex { get; init; }
            /// <summary>Absolute semitone distance (0–12).</summary>
            public int AbsoluteSemitones { get; init; }
        }

        private readonly List<GeneratedNote> _notes;
        private readonly SessionStats _stats = new();

        private int _measureOrdinal;
        private List<int> _measurePitchedFlatIndices = new();
        private int _anchorPitchedPos;
        private bool _hasPair;
        private NotePair _current;
        private bool _wrongThisPair;
        private bool _sessionComplete;
        private readonly List<int> _completedAnchorFlatIndices = new();
        private int _currentTargetPitchedPos;

        public IntervalSightTrainingLogic(IReadOnlyList<GeneratedNote> notes)
        {
            _notes = notes?.ToList() ?? new List<GeneratedNote>();
            ResetToFirstPair();
        }

        public SessionStats Stats => _stats;
        public bool HasCurrentPair => _hasPair && !_sessionComplete;
        public bool IsSessionComplete => _sessionComplete;
        public NotePair? CurrentPair => _hasPair ? _current : null;
        public bool WrongThisPair => _wrongThisPair;

        public static int SignedSemitones(GeneratedNote a, GeneratedNote b)
            => b.MidiNumber - a.MidiNumber;

        public static int AbsoluteSemitones(GeneratedNote a, GeneratedNote b)
            => Math.Abs(SignedSemitones(a, b));

        public static bool IsTestableAbsoluteInterval(int absoluteSemitones)
            => absoluteSemitones >= MinAbsoluteSemitones
               && absoluteSemitones <= MaxAbsoluteSemitones;

        /// <summary>
        /// Finds the next testable consecutive pitched pair starting at
        /// <paramref name="anchorPitchedPos"/> within one measure's pitched indices.
        /// Untestable leaps end the chain at the first note; search resumes at the next note.
        /// Delta is the absolute semitone magnitude.
        /// </summary>
        public static (int AnchorPos, int TargetPos, int AbsoluteSemitones)? FindNextTestablePairInMeasure(
            IReadOnlyList<GeneratedNote> notes,
            IReadOnlyList<int> pitchedFlatIndices,
            int anchorPitchedPos)
        {
            if (pitchedFlatIndices == null || pitchedFlatIndices.Count < 2)
                return null;

            for (int a = Math.Max(0, anchorPitchedPos); a < pitchedFlatIndices.Count - 1; a++)
            {
                int iA = pitchedFlatIndices[a];
                int iB = pitchedFlatIndices[a + 1];
                int abs = AbsoluteSemitones(notes[iA], notes[iB]);
                if (IsTestableAbsoluteInterval(abs))
                    return (a, a + 1, abs);
            }

            return null;
        }

        public static List<int> PitchedFlatIndicesInMeasure(
            IReadOnlyList<GeneratedNote> notes, int measureIndex)
        {
            var list = new List<int>();
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                if (n.IsRest)
                    continue;
                if ((n.MeasureIndex ?? 0) != measureIndex)
                    continue;
                list.Add(i);
            }
            return list;
        }

        public static IReadOnlyList<int> DistinctMeasureIndexes(IReadOnlyList<GeneratedNote> notes)
        {
            var seen = new HashSet<int>();
            var ordered = new List<int>();
            foreach (var n in notes)
            {
                int m = n.MeasureIndex ?? 0;
                if (seen.Add(m))
                    ordered.Add(m);
            }
            return ordered;
        }

        public void ResetToFirstPair()
        {
            _stats.IntervalsTested = 0;
            _stats.CorrectOnFirstAttempt = 0;
            _stats.IncorrectAttempts = 0;
            _wrongThisPair = false;
            _sessionComplete = false;
            _hasPair = false;
            _measureOrdinal = 0;
            _anchorPitchedPos = 0;
            _completedAnchorFlatIndices.Clear();

            var measures = DistinctMeasureIndexes(_notes);
            for (int mi = 0; mi < measures.Count; mi++)
            {
                _measureOrdinal = mi;
                _measurePitchedFlatIndices = PitchedFlatIndicesInMeasure(_notes, measures[mi]);
                _anchorPitchedPos = 0;
                if (TrySetPairFromCurrentAnchor())
                    return;
            }

            _sessionComplete = true;
        }

        /// <summary>
        /// True if the tap matches the expected absolute semitone magnitude
        /// (direction up/down does not matter).
        /// </summary>
        public bool SubmitAnswer(int absoluteSemitones)
        {
            if (!_hasPair || _sessionComplete)
                return false;

            if (absoluteSemitones != _current.AbsoluteSemitones)
            {
                _wrongThisPair = true;
                _stats.IncorrectAttempts++;
                return false;
            }

            _stats.IntervalsTested++;
            if (!_wrongThisPair)
                _stats.CorrectOnFirstAttempt++;

            AdvanceAfterCorrect();
            return true;
        }

        public StaffNoteState[] BuildNoteStates()
        {
            var states = new StaffNoteState[_notes.Count];
            for (int i = 0; i < states.Length; i++)
                states[i] = StaffNoteState.Pending;

            if (!_hasPair)
                return states;

            foreach (int idx in _completedAnchorFlatIndices)
            {
                if ((uint)idx < (uint)states.Length)
                    states[idx] = StaffNoteState.Correct;
            }

            int a = _current.AnchorFlatIndex;
            int b = _current.TargetFlatIndex;
            if (_wrongThisPair)
            {
                states[a] = StaffNoteState.Wrong;
                states[b] = StaffNoteState.Wrong;
            }
            else
            {
                states[a] = StaffNoteState.Current;
                states[b] = StaffNoteState.Current;
            }

            return states;
        }

        private void AdvanceAfterCorrect()
        {
            _completedAnchorFlatIndices.Add(_current.AnchorFlatIndex);
            int previousTargetFlat = _current.TargetFlatIndex;
            _anchorPitchedPos = _currentTargetPitchedPos;
            _wrongThisPair = false;

            if (TrySetPairFromCurrentAnchor())
                return;

            _completedAnchorFlatIndices.Add(previousTargetFlat);

            var measures = DistinctMeasureIndexes(_notes);
            for (int mi = _measureOrdinal + 1; mi < measures.Count; mi++)
            {
                _measureOrdinal = mi;
                _measurePitchedFlatIndices = PitchedFlatIndicesInMeasure(_notes, measures[mi]);
                _anchorPitchedPos = 0;
                if (TrySetPairFromCurrentAnchor())
                    return;
            }

            _hasPair = false;
            _sessionComplete = true;
        }

        private bool TrySetPairFromCurrentAnchor()
        {
            var found = FindNextTestablePairInMeasure(
                _notes, _measurePitchedFlatIndices, _anchorPitchedPos);
            if (found == null)
            {
                _hasPair = false;
                return false;
            }

            var (aPos, bPos, abs) = found.Value;
            _anchorPitchedPos = aPos;
            _currentTargetPitchedPos = bPos;
            _current = new NotePair
            {
                AnchorFlatIndex = _measurePitchedFlatIndices[aPos],
                TargetFlatIndex = _measurePitchedFlatIndices[bPos],
                AbsoluteSemitones = abs,
            };
            _hasPair = true;
            _wrongThisPair = false;
            return true;
        }
    }
}
