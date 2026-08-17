namespace FikaHeadlessManager;

/// <summary>
/// Provides the Windows Forms application entry point.
/// </summary>
internal static class Program
{
    /// <summary>Starts the Fika headless manager user interface.</summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
