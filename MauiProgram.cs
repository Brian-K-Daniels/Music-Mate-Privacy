using Microsoft.Extensions.Logging;
using musicmate.Services;
using Plugin.Maui.Audio;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace musicmate
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseSkiaSharp()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    // Add this if you ship the MDL2 font file (place the TTF under Resources/Fonts)
                    fonts.AddFont("SegoeMDL2Assets.ttf", "SegoeMDL2");
                });

            builder.Services.AddSingleton<NoteSessionService>();
            builder.Services.AddSingleton<PitchDetectionService>();
            #if ANDROID || WINDOWS
                builder.Services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
            #endif
            builder.Services.AddSingleton(AudioManager.Current);
            builder.Services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();

            string noteDbPath = Path.Combine(FileSystem.AppDataDirectory, "notestats.db3");
            string sessionDbPath = Path.Combine(FileSystem.AppDataDirectory, "sessions.db3");
            builder.Services.AddSingleton(new NoteDatabase(noteDbPath));
            builder.Services.AddSingleton(new SessionDatabase(sessionDbPath));

            builder.Services.AddSingleton<IOrientationService, OrientationService>();
            builder.Services.AddSingleton<StatusService>();
            builder.Services.AddSingleton<ThemeService>();
            // Local store service used for initial development. Replace with real Play Billing implementation later.
            builder.Services.AddSingleton<IStoreService, LocalStoreService>();

    #if DEBUG
            builder.Logging.AddDebug();
    #endif

            var app = builder.Build();
            ServiceHelper.Initialize(app.Services); // <-- ensure service locator is initialized

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
