using SkiaSharp;
using musicmate.Diagnostics;

namespace musicmate.Drawables
{
    /// <summary>
    /// Loads and caches Bravura for rest rendering (Skia raster + Android platform text).
    /// Heavy I/O must not run on the UI thread during cold start (ANR risk).
    /// </summary>
    internal static class SmuFLFont
    {
        private const string EmbeddedName = "musicmate.Bravura.otf";

        private static readonly string[] PackageAssetCandidates =
        {
            "Bravura.otf",
            "fonts/Bravura.otf"
        };

        private static byte[]? _fontBytes;
        private static SKTypeface? _skiaTypeface;
#if ANDROID
        private static Android.Graphics.Typeface? _androidTypeface;
#endif
        private static bool _loadAttempted;
        private static int _preloadScheduled;

        /// <summary>
        /// Loads Bravura if needed. Safe to call from any thread; first caller wins.
        /// Prefer <see cref="ScheduleBackgroundPreload"/> during app startup.
        /// </summary>
        internal static void EnsureLoaded() => _ = SkiaTypeface;

        /// <summary>
        /// Kick off a one-shot background load after the first page is up so MusicPage
        /// rests do not pay cold-start cost on the UI thread.
        /// </summary>
        internal static void ScheduleBackgroundPreload()
        {
            if (_loadAttempted || Interlocked.Exchange(ref _preloadScheduled, 1) != 0)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    StartupTiming.Mark("SmuFLFont.BackgroundPreload:begin");
                    EnsureLoaded();
                    StartupTiming.Mark("SmuFLFont.BackgroundPreload:end",
                        IsLoaded ? "loaded" : "fallback");
                }
                catch (Exception ex)
                {
                    StartupTiming.Mark("SmuFLFont.BackgroundPreload:error", ex.Message);
                }
            });
        }

        internal static SKTypeface SkiaTypeface
        {
            get
            {
                LoadIfNeeded();
                return _skiaTypeface ?? SKTypeface.Default;
            }
        }

        internal static bool IsLoaded =>
            _skiaTypeface != null && _skiaTypeface != SKTypeface.Default;

#if ANDROID
        internal static Android.Graphics.Typeface AndroidTypeface
        {
            get
            {
                LoadIfNeeded();
                return _androidTypeface ?? Android.Graphics.Typeface.Default!;
            }
        }
#endif

        private static void LoadIfNeeded()
        {
            if (_loadAttempted)
                return;

            lock (typeof(SmuFLFont))
            {
                if (_loadAttempted)
                    return;

                _loadAttempted = true;
                StartupTiming.Mark("SmuFLFont.LoadIfNeeded:begin");
                long start = Environment.TickCount64;

                _fontBytes = TryLoadBytes();

                if (_fontBytes != null)
                {
                    _skiaTypeface = SKTypeface.FromStream(new MemoryStream(_fontBytes));
#if ANDROID
                    _androidTypeface = TryCreateAndroidTypeface(_fontBytes);
#endif
                }

                StartupTiming.Mark("SmuFLFont.LoadIfNeeded:end",
                    $"took={Environment.TickCount64 - start}ms loaded={IsLoaded}");

                if (IsLoaded)
                    DebugLog.WriteLine($"[SmuFLFont] Bravura loaded ({_skiaTypeface!.FamilyName})");
                else
                    DebugLog.WriteLine("[SmuFLFont] Bravura not loaded; using vector rest fallback.");
            }
        }

        private static byte[]? TryLoadBytes()
        {
            var embedded = TryLoadEmbeddedBytes();
            if (embedded != null)
                return embedded;

#if ANDROID
            // Prefer Assets.Open (sync, no sync-over-async). Never call
            // OpenAppPackageFileAsync().GetResult() on the UI thread — it can stall startup.
            try
            {
                var assets = Android.App.Application.Context?.Assets;
                if (assets != null)
                {
                    foreach (var name in PackageAssetCandidates)
                    {
                        try
                        {
                            using var stream = assets.Open(name);
                            StartupTiming.Mark("SmuFLFont.AssetsOpen", name);
                            return CopyToBytes(stream);
                        }
                        catch
                        {
                            // try next
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[SmuFLFont] android assets failed: {ex.Message}");
            }
#else
            foreach (var name in PackageAssetCandidates)
            {
                try
                {
                    // Non-Android: package file API is acceptable off UI; still avoid if possible.
                    using var stream = FileSystem.OpenAppPackageFileAsync(name).ConfigureAwait(false)
                        .GetAwaiter().GetResult();
                    if (stream != null)
                        return CopyToBytes(stream);
                }
                catch
                {
                    // try next
                }
            }
#endif
            return null;
        }

        private static byte[]? TryLoadEmbeddedBytes()
        {
            try
            {
                using var stream = typeof(SmuFLFont).Assembly.GetManifestResourceStream(EmbeddedName);
                if (stream != null)
                {
                    DebugLog.WriteLine($"[SmuFLFont] embedded resource: {EmbeddedName}");
                    StartupTiming.Mark("SmuFLFont.EmbeddedResource", EmbeddedName);
                    return CopyToBytes(stream);
                }
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[SmuFLFont] embedded load failed: {ex.Message}");
            }
            return null;
        }

#if ANDROID
        private static Android.Graphics.Typeface? TryCreateAndroidTypeface(byte[] bytes)
        {
            try
            {
                var path = Path.Combine(FileSystem.CacheDirectory, "Bravura.otf");
                if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
                    File.WriteAllBytes(path, bytes);
                var face = Android.Graphics.Typeface.CreateFromFile(path);
                if (face != null)
                    return face;
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[SmuFLFont] android typeface failed: {ex.Message}");
            }
            return null;
        }
#endif

        private static byte[] CopyToBytes(Stream stream)
        {
            var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }
}
