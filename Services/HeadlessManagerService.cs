using FikaHeadlessManager.Models;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace FikaHeadlessManager.Services;

/// <summary>
/// Starts, monitors, hides, restarts, and stops multiple Fika headless client processes.
/// </summary>
public sealed class HeadlessManagerService : IAsyncDisposable
{
    private const int HideWindow = 0;
    private readonly Dictionary<Guid, ManagedProcess> _processes = [];
    private readonly HttpClient _httpClient;
    private readonly SynchronizationContext? _synchronizationContext;
    private bool _isDisposing;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeadlessManagerService"/> class.
    /// </summary>
    public HeadlessManagerService()
    {
        _synchronizationContext = SynchronizationContext.Current;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("responsecompressed", "0");
    }

    /// <summary>Occurs when a lifecycle event should be added to the dashboard.</summary>
    public event EventHandler<ActivityEntry>? ActivityCreated;

    /// <summary>Occurs when output is captured for a managed headless client.</summary>
    public event EventHandler<ProcessLogEntry>? LogReceived;

    /// <summary>Gets the number of successful process starts in the current application session.</summary>
    public int SuccessfulStarts { get; private set; }

    /// <summary>Gets the number of failed process starts in the current application session.</summary>
    public int FailedStarts { get; private set; }

    /// <summary>Validates and starts a profile without displaying console windows.</summary>
    /// <param name="profile">The profile to start.</param>
    /// <param name="withGraphics"><see langword="true"/> to preserve the game window; otherwise, <see langword="false"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task StartAsync(ManagerProfile profile, bool withGraphics = false)
    {
        if (_processes.ContainsKey(profile.Id))
        {
            return;
        }

        profile.Status = "Starting";
        RaiseActivity(profile, "Validating installation and backend", "Info");

        try
        {
            ValidateProfile(profile);
            await EnsureBackendAvailableAsync(profile.BackendUrl);
            try
            {
                ArchivePreviousLog(profile.InstallDirectory);
            }
            catch (IOException exception)
            {
                RaiseActivity(profile, $"Previous BepInEx log could not be archived: {exception.Message}", "Info");
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(profile.InstallDirectory, "EscapeFromTarkov.exe"),
                    WorkingDirectory = profile.InstallDirectory,
                    Arguments = BuildArguments(profile, withGraphics),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    RaiseLog(profile, "stdout", args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    RaiseLog(profile, "stderr", args.Data);
                }
            };

            process.Exited += (_, _) =>
            {
                if (_synchronizationContext is not null)
                {
                    _synchronizationContext.Post(_ => _ = HandleExitAsync(profile, process), null);
                }
                else
                {
                    _ = HandleExitAsync(profile, process);
                }
            };
            if (!process.Start())
            {
                throw new InvalidOperationException("The operating system rejected the process start request.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var cancellation = new CancellationTokenSource();
            _processes[profile.Id] = new ManagedProcess(process, cancellation, withGraphics);
            profile.ProcessId = process.Id;
            profile.StartedAt = DateTimeOffset.Now;
            profile.Status = "Running";
            SuccessfulStarts++;
            RaiseActivity(profile, $"Headless client started (PID {process.Id})", "Success");
            _ = HideWindowsLoopAsync(process, withGraphics, cancellation.Token);
            _ = TailLogFileAsync(
                profile,
                Path.Combine(profile.InstallDirectory, "BepInEx", "LogOutput.log"),
                "BepInEx",
                cancellation.Token);
            if (profile.ExtraLogging)
            {
                _ = TailLogFileAsync(
                    profile,
                    Path.Combine(profile.InstallDirectory, "Headless.log"),
                    "Headless",
                    cancellation.Token);
            }
        }
        catch (Exception exception)
        {
            profile.Status = "Error";
            profile.ProcessId = null;
            FailedStarts++;
            RaiseActivity(profile, exception.Message, "Error");
        }
    }

    /// <summary>Stops a running profile and its child processes.</summary>
    /// <param name="profile">The profile to stop.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task StopAsync(ManagerProfile profile)
    {
        if (!_processes.Remove(profile.Id, out var managed))
        {
            profile.Status = "Stopped";
            return;
        }

        managed.Cancellation.Cancel();
        profile.Status = "Stopping";
        try
        {
            if (!managed.Process.HasExited)
            {
                managed.Process.Kill(true);
                await managed.Process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and stop request.
        }
        finally
        {
            managed.Process.Dispose();
            managed.Cancellation.Dispose();
            profile.ProcessId = null;
            profile.StartedAt = null;
            profile.Status = "Stopped";
            RaiseActivity(profile, "Headless client stopped", "Info");
        }
    }

    /// <summary>Stops all processes and releases network and process resources.</summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        _isDisposing = true;
        foreach (var profileId in _processes.Keys.ToArray())
        {
            if (!_processes.Remove(profileId, out var managed))
            {
                continue;
            }

            managed.Cancellation.Cancel();
            try
            {
                if (!managed.Process.HasExited)
                {
                    managed.Process.Kill(true);
                    await managed.Process.WaitForExitAsync();
                }
            }
            catch (InvalidOperationException)
            {
                // The process already exited.
            }

            managed.Process.Dispose();
            managed.Cancellation.Dispose();
        }

        _processes.Clear();
        _httpClient.Dispose();
    }

    private async Task HandleExitAsync(ManagerProfile profile, Process process)
    {
        if (!_processes.Remove(profile.Id, out var managed) || managed.Process != process)
        {
            return;
        }

        managed.Cancellation.Cancel();
        var exitCode = process.ExitCode;
        process.Dispose();
        managed.Cancellation.Dispose();
        profile.ProcessId = null;
        profile.StartedAt = null;
        profile.Status = "Stopped";
        RaiseActivity(profile, $"Headless client exited with code {exitCode}", exitCode == 0 ? "Info" : "Error");

        if (!_isDisposing && profile.AutoRestart)
        {
            profile.Status = "Restarting";
            await Task.Delay(TimeSpan.FromSeconds(3));
            await StartAsync(profile, managed.WithGraphics);
        }
    }

    private static void ValidateProfile(ManagerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.InstallDirectory) ||
            string.IsNullOrWhiteSpace(profile.ProfileId) || string.IsNullOrWhiteSpace(profile.BackendUrl))
        {
            throw new InvalidOperationException("Name, install directory, profile ID, and backend URL are required.");
        }

        if (!File.Exists(Path.Combine(profile.InstallDirectory, "EscapeFromTarkov.exe")))
        {
            throw new FileNotFoundException("EscapeFromTarkov.exe was not found in the selected install directory.");
        }

        if (!File.Exists(Path.Combine(profile.InstallDirectory, "BepInEx", "plugins", "Fika", "Fika.Headless.dll")))
        {
            throw new FileNotFoundException("BepInEx\\plugins\\Fika\\Fika.Headless.dll was not found.");
        }

        if (!Uri.TryCreate(profile.BackendUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("The backend URL is not a valid absolute URL.");
        }
    }

    private async Task EnsureBackendAvailableAsync(string backendUrl)
    {
        var baseUri = new Uri(backendUrl.EndsWith('/') ? backendUrl : backendUrl + "/");
        using var response = await _httpClient.GetAsync(new Uri(baseUri, "fika/presence/get"));
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The Fika backend returned HTTP {(int)response.StatusCode}.");
        }
    }

    private static string BuildArguments(ManagerProfile profile, bool withGraphics)
    {
        var backend = JsonSerializer.Serialize(profile.BackendUrl);
        var config = $"{{'BackendUrl':{backend},'Version':'live'}}";
        var arguments = $"-token={Quote(profile.ProfileId)} -config={Quote(config)}";
        if (!withGraphics)
        {
            arguments += " -nographics -batchmode";
        }

        if (profile.ExtraLogging)
        {
            arguments += " -logfile Headless.log";
        }

        if (!string.IsNullOrWhiteSpace(profile.Title))
        {
            arguments += $" -title={Quote(profile.Title)}";
        }

        return arguments + " --enable-console false";
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static void ArchivePreviousLog(string installDirectory)
    {
        var logPath = Path.Combine(installDirectory, "BepInEx", "LogOutput.log");
        if (File.Exists(logPath))
        {
            File.Move(logPath, Path.ChangeExtension(logPath, ".previous.log"), true);
        }
    }

    private static async Task HideWindowsLoopAsync(Process process, bool withGraphics, CancellationToken cancellationToken)
    {
        try
        {
            while (!process.HasExited && !cancellationToken.IsCancellationRequested)
            {
                EnumWindows((window, parameter) =>
                {
                    _ = GetWindowThreadProcessId(window, out var processId);
                    if (processId != process.Id)
                    {
                        return true;
                    }

                    var className = new char[256];
                    _ = GetClassName(window, className, className.Length);
                    var isConsole = new string(className).TrimEnd('\0').Equals("ConsoleWindowClass", StringComparison.OrdinalIgnoreCase);
                    if (!withGraphics || isConsole)
                    {
                        _ = ShowWindow(window, HideWindow);
                    }

                    return true;
                }, IntPtr.Zero);

                await Task.Delay(750, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
        catch (InvalidOperationException)
        {
            // The process ended while its windows were being enumerated.
        }
    }

    private async Task TailLogFileAsync(
        ManagerProfile profile,
        string logPath,
        string source,
        CancellationToken cancellationToken)
    {
        long position = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (File.Exists(logPath))
                    {
                        await using var stream = new FileStream(
                            logPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete,
                            4096,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        if (stream.Length < position)
                        {
                            position = 0;
                        }

                        stream.Position = position;
                        using var reader = new StreamReader(
                            stream,
                            Encoding.UTF8,
                            detectEncodingFromByteOrderMarks: true,
                            bufferSize: 4096,
                            leaveOpen: true);
                        var text = await reader.ReadToEndAsync(cancellationToken);
                        position = stream.Position;
                        if (!string.IsNullOrEmpty(text))
                        {
                            RaiseLog(profile, source, text.TrimEnd('\r', '\n'));
                        }
                    }
                }
                catch (IOException)
                {
                    // The writer may briefly replace or exclusively lock the log file.
                }
                catch (UnauthorizedAccessException)
                {
                    // Keep polling because permissions can change after game startup.
                }

                await Task.Delay(500, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal process shutdown path.
        }
    }

    private void RaiseActivity(ManagerProfile profile, string message, string status)
    {
        ActivityCreated?.Invoke(this, new ActivityEntry
        {
            Timestamp = DateTimeOffset.Now,
            ManagerName = profile.Name,
            Event = message,
            Status = status
        });

        RaiseLog(profile, "Manager", message);
    }

    private void RaiseLog(ManagerProfile profile, string source, string message) =>
        LogReceived?.Invoke(this, new ProcessLogEntry
        {
            ProfileId = profile.Id,
            ManagerName = profile.Name,
            Timestamp = DateTimeOffset.Now,
            Source = source,
            Message = message
        });

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, char[] className, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    private sealed record ManagedProcess(Process Process, CancellationTokenSource Cancellation, bool WithGraphics);
}
