using FikaHeadlessManager.Pages;
using System.Windows;
using Wpf.Ui.Controls;

namespace FikaHeadlessManager;

/// <summary>
/// Hosts the Fluent shell with navigation between the dashboard and settings pages.
/// </summary>
public partial class MainWindow : FluentWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Navigate(typeof(DashboardPage));
    }
}
