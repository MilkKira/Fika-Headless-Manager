using FikaHeadlessManager.Models;
using FikaHeadlessManager.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace FikaHeadlessManager;

/// <summary>
/// Hosts the responsive dashboard and coordinates all managed headless clients.
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ConfigurationStore _configurationStore = new();
    private readonly HeadlessManagerService _managerService;
    private readonly DispatcherTimer _refreshTimer;
    private ManagerProfile? _editingProfile;
    private IReadOnlyList<double> _trendValues = new double[] { 0 };
    private IReadOnlyList<double> _comparisonValues = new double[] { 0, 0, 0, 0 };
    private bool _allowClose;
    private string _revenue = "$0";
    private string _users = "0";
    private string _conversionRate = "—";
    private string _activeSessions = "0";

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        _managerService = new HeadlessManagerService();
        _managerService.ActivityCreated += ManagerService_ActivityCreated;
        ActivityView = CollectionViewSource.GetDefaultView(Activities);
        ActivityView.Filter = FilterActivity;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += (_, _) => RefreshDashboard(addTrendSample: true);
        Loaded += MainWindow_Loaded;
    }

    /// <summary>Occurs when a dashboard property value changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the configured manager profiles.</summary>
    public ObservableCollection<ManagerProfile> Profiles { get; } = [];

    /// <summary>Gets the recent lifecycle activity.</summary>
    public ObservableCollection<ActivityEntry> Activities { get; } = [];

    /// <summary>Gets the searchable activity collection view.</summary>
    public ICollectionView ActivityView { get; }

    /// <summary>Gets the projected revenue display value.</summary>
    public string Revenue
    {
        get => _revenue;
        private set => SetField(ref _revenue, value);
    }

    /// <summary>Gets the configured user-profile count.</summary>
    public string Users
    {
        get => _users;
        private set => SetField(ref _users, value);
    }

    /// <summary>Gets the successful-start conversion rate.</summary>
    public string ConversionRate
    {
        get => _conversionRate;
        private set => SetField(ref _conversionRate, value);
    }

    /// <summary>Gets the active headless session count.</summary>
    public string ActiveSessions
    {
        get => _activeSessions;
        private set => SetField(ref _activeSessions, value);
    }

    /// <summary>Gets the sampled active-session trend values.</summary>
    public IReadOnlyList<double> TrendValues
    {
        get => _trendValues;
        private set => SetField(ref _trendValues, value);
    }

    /// <summary>Gets the current lifecycle comparison values.</summary>
    public IReadOnlyList<double> ComparisonValues
    {
        get => _comparisonValues;
        private set => SetField(ref _comparisonValues, value);
    }

    /// <summary>Gets the lifecycle comparison labels.</summary>
    public IReadOnlyList<string> ComparisonLabels { get; } = new[] { "Started", "Failed", "Running", "Stopped" };

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var profiles = await _configurationStore.LoadAsync();
            if (profiles.Count == 0)
            {
                var imported = await TryImportLegacyProfileAsync();
                if (imported is not null)
                {
                    profiles = new[] { imported };
                    await _configurationStore.SaveAsync(profiles);
                    AddActivity(imported.Name, "Imported legacy HeadlessConfig.json", "Success");
                }
            }

            foreach (var profile in profiles)
            {
                Profiles.Add(profile);
            }
        }
        catch (Exception exception)
        {
            AddActivity("Application", $"Could not load saved profiles: {exception.Message}", "Error");
        }

        RefreshDashboard(addTrendSample: true);
        ApplyResponsiveLayout(ActualWidth);
        _refreshTimer.Start();
    }

    private void ManagerService_ActivityCreated(object? sender, ActivityEntry entry)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ManagerService_ActivityCreated(sender, entry));
            return;
        }

        Activities.Insert(0, entry);
        while (Activities.Count > 200)
        {
            Activities.RemoveAt(Activities.Count - 1);
        }

        ActivityView.Refresh();
        RefreshDashboard(addTrendSample: false);
    }

    private void RefreshDashboard(bool addTrendSample)
    {
        var running = Profiles.Count(profile => profile.Status == "Running");
        var stopped = Profiles.Count(profile => profile.Status == "Stopped");
        var attempts = _managerService.SuccessfulStarts + _managerService.FailedStarts;
        Users = Profiles.Count.ToString();
        ActiveSessions = running.ToString();
        ConversionRate = attempts == 0
            ? "—"
            : ((double)_managerService.SuccessfulStarts / attempts).ToString("P0");
        Revenue = "$0";
        ComparisonValues = new double[]
        {
            _managerService.SuccessfulStarts,
            _managerService.FailedStarts,
            running,
            stopped
        };

        if (addTrendSample)
        {
            TrendValues = TrendValues.Append((double)running).TakeLast(13).ToArray();
        }

        foreach (var profile in Profiles)
        {
            profile.RefreshUptime();
        }

        LastUpdatedText.Text = $"Updated {DateTime.Now:HH:mm:ss}";
    }

    private async void StartManager_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ManagerProfile profile)
        {
            await _managerService.StartAsync(profile);
        }
    }

    private async void StopManager_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ManagerProfile profile)
        {
            await _managerService.StopAsync(profile);
        }
    }

    private async void StartAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var profile in Profiles.Where(profile => profile.Status is "Stopped" or "Error").ToArray())
        {
            await _managerService.StartAsync(profile);
        }
    }

    private async void StopAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var profile in Profiles.ToArray())
        {
            await _managerService.StopAsync(profile);
        }
    }

    private void AddManager_Click(object sender, RoutedEventArgs e)
    {
        _editingProfile = null;
        EditorTitle.Text = "Add manager";
        NameInput.Text = $"Headless {Profiles.Count + 1}";
        DirectoryInput.Text = string.Empty;
        ProfileInput.Text = string.Empty;
        BackendInput.Text = "https://127.0.0.1:6969/";
        TitleInput.Text = string.Empty;
        AutoRestartInput.IsChecked = true;
        ExtraLoggingInput.IsChecked = false;
        EditorError.Text = string.Empty;
        EditorOverlay.Visibility = Visibility.Visible;
        NameInput.Focus();
    }

    private void EditManager_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ManagerProfile profile)
        {
            return;
        }

        if (profile.Status is not ("Stopped" or "Error"))
        {
            AddActivity(profile.Name, "Stop the manager before editing its configuration", "Info");
            return;
        }

        _editingProfile = profile;
        EditorTitle.Text = "Edit manager";
        NameInput.Text = profile.Name;
        DirectoryInput.Text = profile.InstallDirectory;
        ProfileInput.Text = profile.ProfileId;
        BackendInput.Text = profile.BackendUrl;
        TitleInput.Text = profile.Title;
        AutoRestartInput.IsChecked = profile.AutoRestart;
        ExtraLoggingInput.IsChecked = profile.ExtraLogging;
        EditorError.Text = string.Empty;
        EditorOverlay.Visibility = Visibility.Visible;
    }

    private async void SaveManager_Click(object sender, RoutedEventArgs e)
    {
        var validationMessage = ValidateEditor();
        if (validationMessage is not null)
        {
            EditorError.Text = validationMessage;
            return;
        }

        var profile = new ManagerProfile
        {
            Id = _editingProfile?.Id ?? Guid.NewGuid(),
            Name = NameInput.Text.Trim(),
            InstallDirectory = Path.GetFullPath(DirectoryInput.Text.Trim()),
            ProfileId = ProfileInput.Text.Trim(),
            BackendUrl = BackendInput.Text.Trim(),
            Title = TitleInput.Text.Trim(),
            AutoRestart = AutoRestartInput.IsChecked == true,
            ExtraLogging = ExtraLoggingInput.IsChecked == true
        };

        if (_editingProfile is null)
        {
            Profiles.Add(profile);
            AddActivity(profile.Name, "Manager profile added", "Success");
        }
        else
        {
            var index = Profiles.IndexOf(_editingProfile);
            Profiles[index] = profile;
            AddActivity(profile.Name, "Manager profile updated", "Success");
        }

        await SaveProfilesAsync();
        EditorOverlay.Visibility = Visibility.Collapsed;
        RefreshDashboard(addTrendSample: false);
    }

    private async void RemoveManager_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ManagerProfile profile)
        {
            return;
        }

        if (profile.Status is not ("Stopped" or "Error"))
        {
            AddActivity(profile.Name, "Stop the manager before removing it", "Info");
            return;
        }

        Profiles.Remove(profile);
        AddActivity(profile.Name, "Manager profile removed", "Info");
        await SaveProfilesAsync();
        RefreshDashboard(addTrendSample: false);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the SPT installation directory",
            InitialDirectory = Directory.Exists(DirectoryInput.Text) ? DirectoryInput.Text : Environment.CurrentDirectory
        };
        if (dialog.ShowDialog(this) == true)
        {
            DirectoryInput.Text = dialog.FolderName;
        }
    }

    private void CancelEditor_Click(object sender, RoutedEventArgs e) => EditorOverlay.Visibility = Visibility.Collapsed;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ActivityView?.Refresh();
    }

    private bool FilterActivity(object item)
    {
        if (item is not ActivityEntry entry || string.IsNullOrWhiteSpace(SearchBox?.Text))
        {
            return true;
        }

        var search = SearchBox.Text.Trim();
        return entry.ManagerName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               entry.Event.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               entry.Status.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double windowWidth)
    {
        var compact = windowWidth < 1240;
        KpiGrid.RowDefinitions[1].Height = compact ? GridLength.Auto : new GridLength(0);
        SetCardLayout(RevenueCard, 0, 0, compact ? new Thickness(0, 0, 8, 8) : new Thickness(0, 0, 8, 0));
        SetCardLayout(UsersCard, 0, compact ? 1 : 1, compact ? new Thickness(8, 0, 0, 8) : new Thickness(8, 0, 8, 0));
        SetCardLayout(ConversionCard, compact ? 1 : 0, compact ? 0 : 2, compact ? new Thickness(0, 8, 8, 0) : new Thickness(8, 0, 8, 0));
        SetCardLayout(SessionsCard, compact ? 1 : 0, compact ? 1 : 3, compact ? new Thickness(8, 8, 0, 0) : new Thickness(8, 0, 0, 0));

        var stackCharts = windowWidth < 1100;
        ChartsGrid.ColumnDefinitions[0].Width = stackCharts ? new GridLength(1, GridUnitType.Star) : new GridLength(2, GridUnitType.Star);
        ChartsGrid.ColumnDefinitions[1].Width = stackCharts ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(TrendCard, 0);
        Grid.SetColumn(TrendCard, 0);
        Grid.SetColumnSpan(TrendCard, stackCharts ? 2 : 1);
        TrendCard.Margin = stackCharts ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 10, 0);
        Grid.SetRow(ComparisonCard, stackCharts ? 1 : 0);
        Grid.SetColumn(ComparisonCard, stackCharts ? 0 : 1);
        Grid.SetColumnSpan(ComparisonCard, stackCharts ? 2 : 1);
        ComparisonCard.Margin = stackCharts ? new Thickness(0, 10, 0, 0) : new Thickness(10, 0, 0, 0);
    }

    private static void SetCardLayout(FrameworkElement card, int row, int column, Thickness margin)
    {
        Grid.SetRow(card, row);
        Grid.SetColumn(card, column);
        Grid.SetColumnSpan(card, 1);
        card.Margin = margin;
    }

    private string? ValidateEditor()
    {
        if (string.IsNullOrWhiteSpace(NameInput.Text) || string.IsNullOrWhiteSpace(DirectoryInput.Text) ||
            string.IsNullOrWhiteSpace(ProfileInput.Text) || string.IsNullOrWhiteSpace(BackendInput.Text))
        {
            return "Name, install directory, profile ID, and backend URL are required.";
        }

        if (!Uri.TryCreate(BackendInput.Text.Trim(), UriKind.Absolute, out _))
        {
            return "Enter a valid absolute backend URL.";
        }

        if (!Directory.Exists(DirectoryInput.Text.Trim()))
        {
            return "The selected install directory does not exist.";
        }

        if (!File.Exists(Path.Combine(DirectoryInput.Text.Trim(), "EscapeFromTarkov.exe")))
        {
            return "EscapeFromTarkov.exe was not found in that directory.";
        }

        if (!File.Exists(Path.Combine(DirectoryInput.Text.Trim(), "BepInEx", "plugins", "Fika", "Fika.Headless.dll")))
        {
            return "Fika.Headless.dll was not found under BepInEx\\plugins\\Fika.";
        }

        return null;
    }

    private async Task SaveProfilesAsync()
    {
        try
        {
            await _configurationStore.SaveAsync(Profiles);
        }
        catch (Exception exception)
        {
            AddActivity("Application", $"Could not save profiles: {exception.Message}", "Error");
        }
    }

    private static async Task<ManagerProfile?> TryImportLegacyProfileAsync()
    {
        var configPath = Path.Combine(Environment.CurrentDirectory, "HeadlessConfig.json");
        if (!File.Exists(configPath) || !File.Exists(Path.Combine(Environment.CurrentDirectory, "EscapeFromTarkov.exe")))
        {
            return null;
        }

        await using var stream = File.OpenRead(configPath);
        var settings = await JsonSerializer.DeserializeAsync<LegacySettings>(stream);
        if (settings?.ProfileId is null || settings.BackendUrl is null)
        {
            return null;
        }

        return new ManagerProfile
        {
            Name = string.IsNullOrWhiteSpace(settings.Title) ? "Imported headless" : settings.Title,
            InstallDirectory = Environment.CurrentDirectory,
            ProfileId = settings.ProfileId,
            BackendUrl = settings.BackendUrl.ToString(),
            Title = settings.Title ?? string.Empty,
            ExtraLogging = settings.ExtraLogging
        };
    }

    private void AddActivity(string managerName, string message, string status) =>
        ManagerService_ActivityCreated(this, new ActivityEntry
        {
            Timestamp = DateTimeOffset.Now,
            ManagerName = managerName,
            Event = message,
            Status = status
        });

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        _refreshTimer.Stop();
        IsEnabled = false;
        await _managerService.DisposeAsync();
        _allowClose = true;
        Close();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private sealed record LegacySettings
    {
        public string? ProfileId { get; init; }
        public Uri? BackendUrl { get; init; }
        public bool ExtraLogging { get; init; }
        public string? Title { get; init; }
    }
}
