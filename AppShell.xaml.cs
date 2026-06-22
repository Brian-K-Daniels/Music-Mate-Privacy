using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using musicmate.Services;
using musicmate.Utilities;

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

                // Register routes for navigation
                Routing.RegisterRoute("HomePage", typeof(Pages.HomePage));
                Routing.RegisterRoute("WhatToPlayPage", typeof(Pages.WhatToPlayPage));
                Routing.RegisterRoute("SettingsPage", typeof(Pages.SettingsPage));
                Routing.RegisterRoute("StatisticsPages", typeof(Pages.StatisticsPages));
                Routing.RegisterRoute("AboutPage", typeof(Pages.AboutPage));
            }
            catch (Exception ex)
            {
                Utils.Log($"Error initializing AppShell: {ex.Message}");
            }
        }
    }
}
