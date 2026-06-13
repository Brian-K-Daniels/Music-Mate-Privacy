using SQLite;
using System;

namespace musicmate.Models
{
    // NoteAttemptThresholds is in NoteAttemptThresholds.cs — update constants there,
    // not here, to adjust outlier-filtering behaviour.
    /// <summary>
    /// Records one note-playing attempt from a single practice session.
    ///
    /// "The same note" is identified by the combination of WrittenNoteName and Instrument
    /// (e.g. "C4" on "Bb Clarinet").  The rolling retention limit (MaxAttemptsPerNote) is
    /// applied per that composite key so each instrument/note pair keeps its own history.
    /// </summary>
    [Table("NoteAttempts")]
    public class NoteAttempt
    {
        [PrimaryKey, AutoIncrement]
        public int AttemptId { get; set; }

        public DateTime DateTime { get; set; }

        /// <summary>GUID identifying the containing practice session, set once per session start.</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Full instrument string from NoteSessionService (e.g. "Bb Clarinet, Bb, ...").</summary>
        public string Instrument { get; set; } = string.Empty;

        /// <summary>Child practice level (0 = adult/free practice, 1–100 = child level).</summary>
        public int Level { get; set; }

        /// <summary>
        /// Written note name including octave, e.g. "C4", "F#5", or "REST".
        /// Together with Instrument this is the grouping key for the rolling limit.
        /// </summary>
        public string WrittenNoteName { get; set; } = string.Empty;

        /// <summary>Expected written note for this attempt (same as WrittenNoteName for pitched notes).</summary>
        public string ExpectedWrittenNoteName { get; set; } = string.Empty;

        /// <summary>Detected note name, or null/empty for silence.</summary>
        public string? ActualDetectedNoteName { get; set; }

        /// <summary>True when the expected slot was a rest.</summary>
        public bool IsRest { get; set; }

        /// <summary>Expected duration label, e.g. Quarter, Half.</summary>
        public string? ExpectedDuration { get; set; }

        /// <summary>Expected beat position in the written sequence.</summary>
        public double? ExpectedBeat { get; set; }

        /// <summary>Expected earliest start time in session milliseconds.</summary>
        public double? ExpectedStartMs { get; set; }

        /// <summary>Actual detection time in session milliseconds.</summary>
        public double? ActualDetectedMs { get; set; }

        /// <summary>
        /// Concert-pitch note name, derived by adding the instrument transpose offset.
        /// Empty when the transpose offset is zero (concert-pitch instrument).
        /// </summary>
        public string ConcertPitchNoteName { get; set; } = string.Empty;

        /// <summary>Written MIDI number for the target note.</summary>
        public int MidiNumber { get; set; }

        /// <summary>
        /// Expected note duration from the music sequence (null for classic single-note mode).
        /// Stored as the beat-value float from NoteDuration, e.g. 1.0 = quarter, 0.5 = eighth.
        /// </summary>
        public double? ExpectedDurationBeats { get; set; }

        /// <summary>Pitch error in cents (positive = sharp, negative = flat). 0 when wrong pitch.</summary>
        public int PitchErrorCents { get; set; }

        /// <summary>Timing error in milliseconds relative to expected start (negative = early).</summary>
        public double? TimingErrorMs { get; set; }

        /// <summary>Timing tolerance in milliseconds when timing was evaluated.</summary>
        public double? TimingToleranceMs { get; set; }

        public bool PitchCorrect { get; set; }

        /// <summary>Null when timing was not evaluated for this attempt.</summary>
        public bool? TimingCorrect { get; set; }

        /// <summary>PitchCorrect && TimingCorrect when timing counts; PitchCorrect only when it does not.</summary>
        public bool OverallCorrect { get; set; }

        /// <summary>WrongReason when OverallCorrect is false, e.g. Early, WrongPitch, SoundDuringRest.</summary>
        public string WrongReason { get; set; } = string.Empty;

        // Legacy aliases for code that still references Was* names.
        public bool WasPitchCorrect
        {
            get => PitchCorrect;
            set => PitchCorrect = value;
        }

        public bool? WasTimingCorrect
        {
            get => TimingCorrect;
            set => TimingCorrect = value;
        }

        public bool WasOverallCorrect
        {
            get => OverallCorrect;
            set => OverallCorrect = value;
        }

        [Ignore]
        public bool IsReliableForStats
        {
            get
            {
                if (PitchCorrect
                    && Math.Abs(PitchErrorCents) > NoteAttemptThresholds.MaxPitchErrorCentsForCorrectNote)
                {
                    return false;
                }

                return true;
            }
        }

        [Ignore]
        public bool IsReliableForTimingStats
        {
            get
            {
                if (TimingErrorMs == null) return false;
                if (Math.Abs(TimingErrorMs.Value) > NoteAttemptThresholds.MaxTimingErrorMs)
                    return false;
                return true;
            }
        }
    }
}
