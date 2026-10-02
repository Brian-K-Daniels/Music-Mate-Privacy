namespace musicmate.Services
{
    /// <summary>Shuffled no-repeat draw cycle for a fixed eligible tune title list.</summary>
    public sealed class TuneShuffleBag
    {
        private string? _contextKey;
        private List<string> _bag = [];
        private string? _lastPickedTitle;

        public string? DrawNext(IReadOnlyList<string> eligible, string contextKey, Random rng)
        {
            if (eligible.Count == 0)
                return null;

            if (!string.Equals(_contextKey, contextKey, StringComparison.Ordinal) || _bag.Count == 0)
                RefillBag(eligible, contextKey, rng);

            string picked = _bag[0];
            _bag.RemoveAt(0);
            _lastPickedTitle = picked;
            return picked;
        }

        public void Reset()
        {
            _contextKey = null;
            _bag.Clear();
            _lastPickedTitle = null;
        }

        private void RefillBag(IReadOnlyList<string> eligible, string key, Random rng)
        {
            _contextKey = key;
            _bag = eligible.ToList();
            Shuffle(_bag, rng);

            if (_bag.Count > 1
                && !string.IsNullOrEmpty(_lastPickedTitle)
                && _bag[0] == _lastPickedTitle)
            {
                int swapIndex = 1 + rng.Next(_bag.Count - 1);
                (_bag[0], _bag[swapIndex]) = (_bag[swapIndex], _bag[0]);
            }
        }

        private static void Shuffle(IList<string> items, Random rng)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }

    /// <summary>
    /// Session-scoped shuffle bag for composition-assigned practice tunes.
    /// </summary>
    public static class CompositionTuneShuffleBag
    {
        private static readonly object Gate = new();
        private static readonly TuneShuffleBag Bag = new();

        public static string? DrawNextTitle(CompositionTuneContext context, Random rng)
        {
            lock (Gate)
            {
                var eligible = CompositionTuneEligibility.GetEligibleTuneTitles(context);
                if (eligible.Count == 0)
                    return null;

                string key = CompositionTuneEligibility.BuildContextKey(context, eligible);
                return Bag.DrawNext(eligible, key, rng);
            }
        }

        public static void Reset()
        {
            lock (Gate)
                Bag.Reset();
        }
    }
}
