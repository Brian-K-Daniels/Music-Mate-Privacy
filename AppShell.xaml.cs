using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using musicmate.Services;
using musicmate.Utilities;
#if DEBUG
using musicmate.Pages;
#endif

namespace musicmate
{
    public partial class AppShell : Shell
    {
        public ICommand? GoPracticeCommand { get; }

        public AppShell()
        {
            try
            {
                InitializeComponent();

                BindingContext = this;
                GoPracticeCommand = new Command(async () => await GoToAsync("//HomePage"));
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
