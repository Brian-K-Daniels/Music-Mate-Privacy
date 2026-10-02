namespace musicmate.Services
{
    /// <summary>How the active practice scale is chosen.</summary>
    public enum ScaleSelectionMode
    {
        /// <summary>Level-appropriate default scale from <see cref="ChildLevelProgression"/>.</summary>
        ByLevel,

        /// <summary>Weighted random scale from the level's allowed pool.</summary>
        Random,

        /// <summary>User-selected named scale (Major, Blues, etc.).</summary>
        Named
    }
}
