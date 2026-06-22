using Microsoft.Extensions.Logging;
using musicmate.Drawables;
using musicmate.Services;
using Plugin.Maui.Audio;
using SkiaSharp.Views.Maui.Controls.Hosting;
using CommunityToolkit.Maui;

namespace musicmate
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .UseSkiaSharp()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    // Add this if you ship the MDL2 font file (place the TTF under Resources/Fonts)
                    fonts.AddFont("SegoeMDL2Assets.ttf", "SegoeMDL2");
                    fonts.AddFont("Bravura.otf", "Bravura");
                });

            // Preload Bravura for GraphicsView rest glyphs (ICanvas ignores MauiFont names).
            SmuFLFont.EnsureLoaded();

            // Configure sqlite-net-base to use the SourceGear SQLite native provider.
            SQLitePCL.Batteries.Init();

            builder.Services.AddSingleton<NoteSessionService>();
            builder.Services.AddSingleton<PitchDetectionService>();
#if ANDROID || WINDOWS
            builder.Services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
#endif
            builder.Services.AddSingleton(AudioManager.Current);
            builder.Services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();

            string noteDbPath = Path.Combine(FileSystem.AppDataDirectory, "notestats.db3");
            string sessionDbPath = Path.Combine(FileSystem.AppDataDirectory, "sessions.db3");
            string sessionResultDbPath = Path.Combine(FileSystem.AppDataDirectory, "session_results.db3");
            string noteAttemptDbPath = Path.Combine(FileSystem.AppDataDirectory, "note_attempts.db3");
            builder.Services.AddSingleton(new NoteDatabase(noteDbPath));
            builder.Services.AddSingleton(new SessionDatabase(sessionDbPath));
            builder.Services.AddSingleton(new SessionResultDatabase(sessionResultDbPath));
            builder.Services.AddSingleton(new NoteAttemptDatabase(noteAttemptDbPath));

            builder.Services.AddSingleton<IOrientationService, OrientationService>();
            builder.Services.AddSingleton<StatusService>();
            builder.Services.AddSingleton<ThemeService>();

#if ANDROID
            builder.Services.AddSingleton<ISafeAreaService, musicmate.Platforms.Android.SafeAreaService>();
#else
            builder.Services.AddSingleton<ISafeAreaService>(sp => new FallbackSafeAreaService());
#endif
#if DEBUG
            // Debug: local stub allows free "purchase" and a Restore button to reset it.
            builder.Services.AddSingleton<IStoreService, LocalStoreService>();
#else
            // Release: real Google Play Billing.
#if ANDROID
            builder.Services.AddSingleton<IStoreService, musicmate.Platforms.Android.GooglePlayStoreService>();
#else
            // Fallback for non-Android release builds (e.g. Windows side-load).
            builder.Services.AddSingleton<IStoreService, LocalStoreService>();
#endif
#endif

#if DEBUG
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();
            ServiceHelper.Initialize(app.Services); // <-- ensure service locator is initialized

#if DEBUG
            // Key-sig / transposition self-test → logcat tag "MusicMate" (cold start).
            V3StaffDrawable.RunKeySignatureTests();
            V3StaffDrawable.RunMeasureLayoutTests();
#endif

            // Sync premium state from the store on every cold start.
            // In Debug this is a no-op (LocalStoreService.InitializeAsync does nothing).
            // In Release this connects to Google Play and refreshes the persisted flag.
            try
            {
                var store = app.Services.GetService<IStoreService>();
                if (store != null)
                    _ = store.InitializeAsync();   // fire-and-forget; StatusService persists result
            }
            catch { }

            // Deploy saved panel background color now that services are initialized
            try
            {
                var theme = app.Services.GetService<ThemeService>();
                if (theme != null)
                {
                    var saved = Microsoft.Maui.Storage.Preferences.Default.Get<string?>("StaffPanelColor", null);
                    if (!string.IsNullOrEmpty(saved))
                    {
                        theme.PanelBackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb(saved);
                    }
                }
            }
            catch { }
            return app;
        }
    }
}
