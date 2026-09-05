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

        ApplicationTheme theme;
        try
        {
            var settings = await new AppSettingsStore().LoadAsync();
            theme = settings?.Theme switch
            {
                "Light" => ApplicationTheme.Light,
                _ => ApplicationTheme.Dark
            };
        }
        catch
        {
            // Fall back to the default dark theme if settings cannot be read.
            theme = ApplicationTheme.Dark;
        }

        ApplyTheme(theme);

        new MainWindow().Show();
    }

    /// <summary>
    /// Switches the application theme by replacing the Wpf.Ui theme dictionary in
    /// <see cref="Application.Resources"/>. This is reliable before the main window is
    /// realized, unlike <see cref="ApplicationThemeManager.Apply(ApplicationTheme)"/>
    /// which can be unreliable at that point because of how Wpf.Ui caches its
    /// <c>UiApplication</c> resources.
    /// </summary>
    public static void ApplyTheme(ApplicationTheme theme)
    {
        var themeName = theme == ApplicationTheme.Light ? "Light" : "Dark";
        var themeUri = new Uri(
            $"pack://application:,,,/Wpf.Ui;component/Resources/Theme/{themeName}.xaml",
            UriKind.Absolute);
        var merged = Application.Current.Resources.MergedDictionaries;
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.ToString() ?? string.Empty;
            if (source.Contains("Wpf.Ui", StringComparison.OrdinalIgnoreCase)
                && source.Contains("theme", StringComparison.OrdinalIgnoreCase))
            {
                merged[i] = new ResourceDictionary { Source = themeUri };
                return;
            }
        }

        // Fallback if the theme dictionary cannot be located.
        ApplicationThemeManager.Apply(theme);
    }
}
