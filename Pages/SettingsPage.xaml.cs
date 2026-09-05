using FikaHeadlessManager.Models;
using FikaHeadlessManager.Services;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;

namespace FikaHeadlessManager.Pages;

/// <summary>
/// Provides appearance settings and program information.
/// </summary>
public partial class SettingsPage : Page
{
    private readonly AppSettingsStore _settingsStore = new();
    private bool _suppressThemeEvents;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsPage"/> class.
    /// </summary>
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateVersionInformation();
        ApplyThemeSelection(ApplicationThemeManager.GetAppTheme());
    }

    private async void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressThemeEvents || sender is not RadioButton radio || radio.IsChecked != true)
        {
            return;
        }

        var dark = ReferenceEquals(radio, DarkThemeRadio);
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        await SaveThemeAsync(dark ? "Dark" : "Light");
    }

    private void ApplyThemeSelection(ApplicationTheme theme)
    {
        _suppressThemeEvents = true;
        if (theme == ApplicationTheme.Dark)
        {
            DarkThemeRadio.IsChecked = true;
        }
        else
        {
            LightThemeRadio.IsChecked = true;
        }

        _suppressThemeEvents = false;
    }

    private async Task SaveThemeAsync(string theme)
    {
        try
        {
            await _settingsStore.SaveAsync(new AppSettings { Theme = theme });
        }
        catch
        {
            // Persisting the theme is best-effort; the current session still uses the selection.
        }
    }

    private void UpdateVersionInformation()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString(3) ?? "未知";
        VersionValueText.Text = version;
        RuntimeValueText.Text = $".NET {Environment.Version}";
        OperatingSystemValueText.Text = Environment.OSVersion.VersionString;
    }
}
