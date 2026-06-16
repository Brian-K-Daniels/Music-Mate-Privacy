namespace musicmate.Drawables
{
    /// <summary>Visual state of a single note slot in the V3 staff display.</summary>
    public enum V3NoteState
    {
        /// <summary>Not yet reached — neutral notehead colour.</summary>
        Pending,
        /// <summary>The note the player must play next — highlighted.</summary>
        Current,
        /// <summary>Played correctly — green.</summary>
        Correct,
        /// <summary>At least one wrong attempt has been made (may still be the current target).</summary>
        Wrong
    }
}
