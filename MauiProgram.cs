using Microsoft.Extensions.Logging;
using musicmate.Diagnostics;
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
            StartupTiming.Mark("MauiProgram.CreateMauiApp:begin");

            var builder = MauiApp.CreateBuilder();
            StartupTiming.Time("UseMauiApp+toolkit+skia", () =>
            {
                builder
                    .UseMauiApp<App>()
                    .UseMauiCommunityToolkit()
                    .UseSkiaSharp()
                    .ConfigureFonts(fonts =>
                    {
                        fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                        // Add this if you ship the MDL2 font file (place the TTF under Resources/Fonts)
                        fonts.AddFont("SegoeMDL2Assets.ttf", "SegoeMDL2");
                        // Bravura is also embedded for Skia rests; MauiFont registration is light.
                        // Heavy Skia/Android typeface preload is deferred until after Home appears.
                        fonts.AddFont("Bravura.otf", "Bravura");
                    });
            });

            // SQLite native provider — keep; typically fast once.
            StartupTiming.Time("SQLitePCL.Batteries.Init", () => SQLitePCL.Batteries.Init());

            builder.Services.AddSingleton<NoteSessionService>();
            builder.Services.AddSingleton<DisplayedTuneHistory>();
            builder.Services.AddSingleton<PitchDetectionService>();
#if ANDROID || WINDOWS
            builder.Services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
#endif
            builder.Services.AddSingleton(AudioManager.Current);
            builder.Services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();
            builder.Services.AddSingleton<ICountInClickService, CountInClickService>();

            string noteDbPath = Path.Combine(FileSystem.AppDataDirectory, "notestats.db3");
            string sessionDbPath = Path.Combine(FileSystem.AppDataDirectory, "sessions.db3");
            string sessionResultDbPath = Path.Combine(FileSystem.AppDataDirectory, "session_results.db3");
            string noteAttemptDbPath = Path.Combine(FileSystem.AppDataDirectory, "note_attempts.db3");
            // DB wrappers only store paths; tables are created lazily via InitializeAsync.
            builder.Services.AddSingleton(new NoteDatabase(noteDbPath));
            builder.Services.AddSingleton(new SessionDatabase(sessionDbPath));
            builder.Services.AddSingleton(new SessionResultDatabase(sessionResultDbPath));
            builder.Services.AddSingleton(new NoteAttemptDatabase(noteAttemptDbPath));

            builder.Services.AddSingleton<IOrientationService, OrientationService>();
            builder.Services.AddSingleton<StatusService>();
            builder.Services.AddSingleton<ThemeService>();
            builder.Services.AddSingleton<SettingsResetService>();
            builder.Services.AddSingleton<StatisticsCacheService>();
            builder.Services.AddSingleton<NoteMasteryService>();
            builder.Services.AddSingleton<SavedTuneStore>();

#if ANDROID
            builder.Services.AddSingleton<ISafeAreaService, musicmate.Platforms.Android.SafeAreaService>();
#else
            builder.Services.AddSingleton<ISafeAreaService>(sp => new FallbackSafeAreaService());
#endif
#if DEBUG || LOCAL_RELEASE
            // Debug / LocalRelease: local stub allows free "purchase" (and reset via Restore).
            builder.Services.AddSingleton<IStoreService, LocalStoreService>();
#else
            // Play Release: real Google Play Billing.
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

            StartupTiming.Mark("MauiProgram.builder.Build:begin");
            var app = builder.Build();
            StartupTiming.Mark("MauiProgram.builder.Build:end");
            ServiceHelper.Initialize(app.Services); // <-- ensure service locator is initialized

#if DEBUG
            // Preference sync for log toggles — not needed before first paint.
            _ = Task.Run(() =>
            {
                try
                {
                    StartupTiming.Time("DebugLogSettings.LoadAll", DebugLogSettings.LoadAll);
                    AppLifecycleLog.RegisterUnhandledExceptionHooks();
                }
                catch { }
            });
#else
            // Release: no debug log prefs.
#endif

            // Sync premium state from the store on every cold start (already async / fire-and-forget).
            try
            {
                var store = app.Services.GetService<IStoreService>();
                if (store != null)
                    _ = store.InitializeAsync();
            }
            catch { }

            // Theme colors: App ctor pushes resources when Application exists.
            // Do not LoadFromPreferences here (duplicates App and runs before UI).

            StartupTiming.Mark("MauiProgram.CreateMauiApp:end");
            return app;
        }
    }
}
