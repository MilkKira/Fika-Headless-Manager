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

    /// <summary>Occurs when a managed process changes lifecycle state.</summary>
    public event EventHandler? StateChanged;

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
        ReportManagerMessage(profile, "正在验证安装目录和后端服务。");

        try
        {
            ValidateProfile(profile);
            var normalizedInstallDirectory = NormalizeDirectory(profile.InstallDirectory);
            if (_processes.Values.Any(managed =>
                    string.Equals(managed.InstallDirectory, normalizedInstallDirectory, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "另一个运行中的实例正在使用该 SPT 目录。请使用独立安装目录，以确保各进程日志互不混合。");
            }

            await EnsureBackendAvailableAsync(profile.BackendUrl);
            try
            {
                ArchivePreviousLogs(profile.InstallDirectory);
            }
            catch (IOException exception)
            {
                ReportManagerMessage(profile, $"无法归档上一次 BepInEx 日志：{exception.Message}");
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
                    RaiseLog(profile, "标准输出", args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    RaiseLog(profile, "错误输出", args.Data);
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
                throw new InvalidOperationException("操作系统拒绝了进程启动请求。");
            }

            var cancellation = new CancellationTokenSource();
            _processes[profile.Id] = new ManagedProcess(process, cancellation, withGraphics, normalizedInstallDirectory);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            profile.ProcessId = process.Id;
            profile.StartedAt = DateTimeOffset.Now;
            profile.Status = "Running";
            SuccessfulStarts++;
            ReportManagerMessage(profile, $"无头客户端已启动（进程 ID：{process.Id}）。");
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
            if (_processes.Remove(profile.Id, out var failedProcess))
            {
                failedProcess.Cancellation.Cancel();
                try
                {
                    if (!failedProcess.Process.HasExited)
                    {
                        failedProcess.Process.Kill(true);
                        await failedProcess.Process.WaitForExitAsync();
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the failed start was being cleaned up.
                }

                failedProcess.Process.Dispose();
                failedProcess.Cancellation.Dispose();
            }

            profile.Status = "Error";
            profile.ProcessId = null;
            FailedStarts++;
            ReportManagerMessage(profile, exception.Message);
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
            ReportManagerMessage(profile, "无头客户端已停止。");
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
        ReportManagerMessage(profile, $"无头客户端已退出，退出代码：{exitCode}。");

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
            throw new InvalidOperationException("实例名称、安装目录、配置文件 ID 和后端地址均为必填项。");
        }

        if (!File.Exists(Path.Combine(profile.InstallDirectory, "EscapeFromTarkov.exe")))
        {
            throw new FileNotFoundException("所选安装目录中未找到 EscapeFromTarkov.exe。");
        }

        if (!File.Exists(Path.Combine(profile.InstallDirectory, "BepInEx", "plugins", "Fika", "Fika.Headless.dll")))
        {
            throw new FileNotFoundException("未找到 BepInEx\\plugins\\Fika\\Fika.Headless.dll。");
        }

        if (!Uri.TryCreate(profile.BackendUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("后端地址不是有效的绝对地址。");
        }
    }

    private async Task EnsureBackendAvailableAsync(string backendUrl)
    {
        var baseUri = new Uri(backendUrl.EndsWith('/') ? backendUrl : backendUrl + "/");
        using var response = await _httpClient.GetAsync(new Uri(baseUri, "fika/presence/get"));
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Fika 后端返回了 HTTP {(int)response.StatusCode}。");
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

    private static string NormalizeDirectory(string directory) =>
        Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void ArchivePreviousLogs(string installDirectory)
    {
        var logPaths = new[]
        {
            Path.Combine(installDirectory, "BepInEx", "LogOutput.log"),
            Path.Combine(installDirectory, "Headless.log")
        };

        foreach (var logPath in logPaths.Where(File.Exists))
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

    private void ReportManagerMessage(ManagerProfile profile, string message)
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
        RaiseLog(profile, "管理器", message);
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

    private sealed record ManagedProcess(
        Process Process,
        CancellationTokenSource Cancellation,
        bool WithGraphics,
        string InstallDirectory);
}
