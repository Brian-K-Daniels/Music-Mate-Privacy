using SkiaSharp;

namespace musicmate.Drawables
{
    /// <summary>
    /// Loads and caches Bravura for rest rendering (Skia raster + Android platform text).
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

        internal static void EnsureLoaded() => _ = SkiaTypeface;

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

            _loadAttempted = true;
            _fontBytes = TryLoadBytes();

            if (_fontBytes != null)
            {
                _skiaTypeface = SKTypeface.FromStream(new MemoryStream(_fontBytes));
#if ANDROID
                _androidTypeface = TryCreateAndroidTypeface(_fontBytes);
#endif
            }

            if (IsLoaded)
                System.Diagnostics.Debug.WriteLine($"[SmuFLFont] Bravura loaded ({_skiaTypeface!.FamilyName})");
            else
                System.Diagnostics.Debug.WriteLine("[SmuFLFont] Bravura not loaded; using vector rest fallback.");
        }

        private static byte[]? TryLoadBytes()
        {
            var embedded = TryLoadEmbeddedBytes();
            if (embedded != null)
                return embedded;

            foreach (var name in PackageAssetCandidates)
            {
                try
                {
                    using var stream = FileSystem.OpenAppPackageFileAsync(name).GetAwaiter().GetResult();
                    if (stream != null)
                        return CopyToBytes(stream);
                }
                catch
                {
                    // try next
                }
            }

#if ANDROID
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
                System.Diagnostics.Debug.WriteLine($"[SmuFLFont] android assets failed: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"[SmuFLFont] embedded resource: {EmbeddedName}");
                    return CopyToBytes(stream);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmuFLFont] embedded load failed: {ex.Message}");
            }
            return null;
        }

#if ANDROID
        private static Android.Graphics.Typeface? TryCreateAndroidTypeface(byte[] bytes)
        {
            try
            {
                var path = Path.Combine(FileSystem.CacheDirectory, "Bravura.otf");
                File.WriteAllBytes(path, bytes);
                var face = Android.Graphics.Typeface.CreateFromFile(path);
                if (face != null)
                    return face;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmuFLFont] android typeface failed: {ex.Message}");
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
