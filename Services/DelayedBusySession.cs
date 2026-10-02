namespace musicmate.Services
{
    /// <summary>
    /// Generation-counted busy depth used by <see cref="NavigationBusyService"/>.
    /// Nested Begin calls share one delayed-show generation so an older timer cannot
    /// show the spinner after a newer End/Reset.
    /// </summary>
    public sealed class DelayedBusySession
    {
        public const int DefaultShowDelayMs = 1000;

        private readonly object _gate = new();
        private int _depth;
        private int _generation;

        public bool IsBusy
        {
            get { lock (_gate) return _depth > 0; }
        }

        public int Generation
        {
            get { lock (_gate) return _generation; }
        }

        /// <summary>
        /// Starts a busy scope. <paramref name="ownsShowTimer"/> is true only for the
        /// outermost Begin (the one that should arm the delayed spinner).
        /// </summary>
        public int Begin(out bool ownsShowTimer)
        {
            lock (_gate)
            {
                _depth++;
                if (_depth > 1)
                {
                    ownsShowTimer = false;
                    return _generation;
                }

                ownsShowTimer = true;
                return ++_generation;
            }
        }

        /// <returns>True when depth reached zero and any pending show must be cancelled.</returns>
        public bool End(out int generationAfter)
        {
            lock (_gate)
            {
                if (_depth > 0)
                    _depth--;

                if (_depth > 0)
                {
                    generationAfter = _generation;
                    return false;
                }

                _generation++;
                generationAfter = _generation;
                return true;
            }
        }

        public int Reset()
        {
            lock (_gate)
            {
                _depth = 0;
                return ++_generation;
            }
        }

        public bool IsCurrent(int generation)
        {
            lock (_gate)
                return generation == _generation && _depth > 0;
        }
    }
}
