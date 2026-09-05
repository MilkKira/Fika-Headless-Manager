using FikaHeadlessManager.Models;
using FikaHeadlessManager.Services;
using Microsoft.Win32;
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
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "FikaHeadlessManager";
    private readonly AppSettingsStore _settingsStore = new();
    private bool _suppressThemeEvents;
    private bool _suppressAutoStartEvents;

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
        ApplyAutoStartState();
    }

    private void ApplyAutoStartState()
    {
        _suppressAutoStartEvents = true;
        AutoStartToggle.IsChecked = IsAutoStartEnabled();
        _suppressAutoStartEvents = false;
    }

    private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressAutoStartEvents)
        {
            return;
        }

        try
        {
            SetAutoStart(AutoStartToggle.IsChecked == true);
        }
        catch (Exception exception)
        {
            ApplyAutoStartState();
            var owner = Window.GetWindow(this);
            var message = $"无法修改开机自启动设置：{exception.Message}";
            if (owner is not null)
            {
                MessageBox.Show(owner, message, "Fika 无头管理器", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(message, "Fika 无头管理器", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(AutoStartValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    private static void SetAutoStart(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (!enable)
        {
            key.DeleteValue(AutoStartValueName, false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("无法确定当前可执行文件路径。");
        }

        key.SetValue(AutoStartValueName, $"\"{executablePath}\"");
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
