namespace musicmate.Services
{
    public readonly record struct WeightedScaleOption(string Scale, int Weight);

    public readonly record struct WeightedKeyOption(string Key, int Weight);

    /// <summary>
    /// Inspectable difficulty profile for a child level: weighted scale/key pools plus
    /// fixed range and accidental settings.
    /// </summary>
    public sealed class ChildLevelDifficultyProfile
    {
        public required int Level { get; init; }
        public required IReadOnlyList<WeightedScaleOption> ScalePool { get; init; }
        public required IReadOnlyList<WeightedKeyOption> KeyPool { get; init; }
        public required string LowestNote { get; init; }
        public required string HighestNote { get; init; }
        public required int AccidentalPercent { get; init; }
        public string StageLabel { get; init; } = "";
        public string MainFocus { get; init; } = "";
    }
}
