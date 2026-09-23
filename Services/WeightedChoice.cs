namespace musicmate.Services
{
    /// <summary>Weighted random selection for child-level scale/key pools.</summary>
    public static class WeightedChoice
    {
        public static T Pick<T>(IReadOnlyList<T> options, Func<T, int> getWeight, Random? rng = null)
        {
            if (options == null || options.Count == 0)
                throw new InvalidOperationException("Weighted choice requires at least one option.");

            int total = 0;
            for (int i = 0; i < options.Count; i++)
            {
                int w = getWeight(options[i]);
                if (w > 0)
                    total += w;
            }

            if (total <= 0)
                return options[0];

            rng ??= Random.Shared;
            int roll = rng.Next(total);
            int cumulative = 0;
            for (int i = 0; i < options.Count; i++)
            {
                int w = getWeight(options[i]);
                if (w <= 0)
                    continue;
                cumulative += w;
                if (roll < cumulative)
                    return options[i];
            }

            return options[^1];
        }
    }
}
