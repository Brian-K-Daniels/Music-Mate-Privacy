using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using musicmate.LayoutDebug;
using musicmate.Pages;
using musicmate.Services;
using musicmate.Utilities;

namespace musicmate
{
    public partial class AppShell : Shell
    {
        public ICommand? GoPracticeCommand { get; }
        public ICommand? GoTunerCommand { get; }

        public AppShell()
        {
            try
            {
                InitializeComponent();

                BindingContext = this;
                GoPracticeCommand = new Command(async () => await GoToAsync("//HomePage"));
                GoTunerCommand = new Command(async () => await GoTunerAsync());
                var theme = ServiceHelper.GetService<ThemeService>();
                if (theme != null)
                {
                    theme.ThemeColorsChanged += (_, _) => theme.ApplyToShellIfAvailable();
                    theme.ApplyToShellIfAvailable();
                }
#if DEBUG
                RegisterDebugFlyoutItem();
#endif
                // All page routes are declared via Route="..." on ShellContent in AppShell.xaml,
                // so no additional Routing.RegisterRoute calls are needed here.  The previous
                // calls silently threw ArgumentException (duplicate route) on every cold start.
            }
            catch (Exception ex)
            {
                Utils.Log($"Error initializing AppShell: {ex.Message}");
            }
        }

        /// <summary>
        /// Same path as What to Play → Other → Tuner, then open Music.
        /// </summary>
        private async Task GoTunerAsync()
        {
            try
            {
                var session = ServiceHelper.GetService<NoteSessionService>();
                var alreadyOnTuner = session?.Tune == PlayModePickerOptions.Tuner
                    && CurrentPage is MusicPage;

                // Always apply Other → Tuner so What to Play's Other picker / SelectedTune stay in sync.
                if (session != null)
                {
                    LayoutTestTune.SetEnabled(false);
                    PlayModePickerOptions.ApplyOtherSelection(session, PlayModePickerOptions.Tuner);
                }

                // Close first; re-navigating to Music while already there can leave the flyout open.
                FlyoutIsPresented = false;

                if (!alreadyOnTuner)
                    await GoToAsync("//MusicPage");

                // Ensure close sticks after any Shell navigation side effects.
                FlyoutIsPresented = false;
            }
            catch (Exception ex)
            {
                Utils.Log($"Error opening Tuner: {ex.Message}");
            }
        }

#if DEBUG
        private void RegisterDebugFlyoutItem()
        {
            var debugItem = new FlyoutItem
            {
                Title = "Debug Items",
                Route = "DebugItems",
            };
            debugItem.Items.Add(new ShellContent
            {
                Title = "Debug Items",
                Route = "DebugItemsPage",
                ContentTemplate = new DataTemplate(typeof(DebugItemsPage)),
            });
            Items.Add(debugItem);
        }
#endif
    }
}
