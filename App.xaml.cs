using FikaHeadlessManager.Services;
using System.Windows;
using Wpf.Ui.Appearance;

namespace FikaHeadlessManager;

/// <summary>
/// Provides the application entry point and shared WPF resources.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Applies the persisted theme and shows the main window.
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var settings = await new AppSettingsStore().LoadAsync();
            if (settings is not null)
            {
                var theme = settings.Theme switch
                {
                    "Dark" => ApplicationTheme.Dark,
                    _ => ApplicationTheme.Light
                };
                ApplicationThemeManager.Apply(theme);
            }
        }
        catch
        {
            // Fall back to the theme declared in App.xaml.
        }

        new MainWindow().Show();
    }
}
