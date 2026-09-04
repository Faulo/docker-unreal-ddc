using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace UnrealDDC;

static partial class ZenProcess {
    public static async Task<int> RunAsync(ZenInstallation installation, IEnumerable<string> arguments, int port) {
        var start = CreateStartInfo(installation.server, arguments, installation.directory, true);
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("Failed to start Zen server");
        process.OutputDataReceived += MirrorStandardOutput;
        process.ErrorDataReceived += MirrorStandardError;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var shutdown = new ShutdownCoordinator(process, installation.client, installation.directory, port);
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    public static async Task<int> RunHealthAsync(ZenInstallation installation, int port) {
        var start = CreateStartInfo(
            installation.client,
            ["version", $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}"],
            installation.directory
        );
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("Failed to start the Zen health probe");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    internal static ProcessStartInfo CreateStartInfo(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        bool redirectOutput = false
    ) {
        var start = new ProcessStartInfo {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput
        };
        foreach (string argument in arguments) {
            start.ArgumentList.Add(argument);
        }
        RemoveCredentials(start);
        return start;
    }

    static void MirrorStandardOutput(object sender, DataReceivedEventArgs arguments) {
        if (arguments.Data is not null) {
            Console.Out.WriteLineAsync(arguments.Data).GetAwaiter().GetResult();
        }
    }

    static void MirrorStandardError(object sender, DataReceivedEventArgs arguments) {
        if (arguments.Data is not null) {
            Console.Error.WriteLineAsync(arguments.Data).GetAwaiter().GetResult();
        }
    }

    internal static void RemoveCredentials(ProcessStartInfo start) {
        start.Environment.Remove(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR);
        start.Environment.Remove(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR_FILE);
        start.Environment.Remove(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW);
        start.Environment.Remove(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW_FILE);
    }

    internal static ProcessStartInfo CreateStopStartInfo(string client, string workingDirectory, int port) => CreateStartInfo(
        client,
        [
            "--no-sentry",
            "down",
            $"--port={port.ToString(CultureInfo.InvariantCulture)}",
            "--force"
        ],
        workingDirectory
    );

    sealed class ShutdownCoordinator : IDisposable {
        const int MAX_ATTEMPTS = 3;
        readonly Process server;
        readonly string client;
        readonly string workingDirectory;
        readonly int port;
        readonly ConsoleCancelEventHandler cancelHandler;
        readonly EventHandler exitHandler;
        readonly PosixSignalRegistration? terminateRegistration;
        readonly PosixSignalRegistration? interruptRegistration;
        readonly ConsoleControlHandler? windowsHandler;
        int stopState;

        public ShutdownCoordinator(Process server, string client, string workingDirectory, int port) {
            this.server = server;
            this.client = client;
            this.workingDirectory = workingDirectory;
            this.port = port;
            cancelHandler = OnCancel;
            exitHandler = OnExit;
            Console.CancelKeyPress += cancelHandler;
            AppDomain.CurrentDomain.ProcessExit += exitHandler;

            if (OperatingSystem.IsWindows()) {
                windowsHandler = OnWindowsControl;
                if (!SetConsoleCtrlHandler(windowsHandler, true)) {
                    throw new InvalidOperationException("Failed to register the Windows shutdown handler");
                }
            } else {
                terminateRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnPosixSignal);
                interruptRegistration = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnPosixSignal);
            }
        }

        void OnCancel(object? sender, ConsoleCancelEventArgs arguments) {
            arguments.Cancel = true;
            RequestStop();
        }

        void OnExit(object? sender, EventArgs arguments) => RequestStop();

        void OnPosixSignal(PosixSignalContext context) {
            context.Cancel = true;
            RequestStop();
        }

        bool OnWindowsControl(EConsoleControlType controlType) {
            if (controlType is not (EConsoleControlType.CTRL_C
                or EConsoleControlType.CTRL_BREAK
                or EConsoleControlType.CTRL_CLOSE
                or EConsoleControlType.CTRL_SHUTDOWN)) {
                return false;
            }
            RequestStop();
            if (server.HasExited) {
                // Windows assigns a control-event status when this handler
                // returns. Zen has flushed and exited, so end the launcher
                // explicitly with the clean container exit code.
                Environment.Exit(0);
            }
            return true;
        }

        void RequestStop() {
            if (Interlocked.CompareExchange(ref stopState, 1, 0) != 0) {
                return;
            }

            bool stopped = false;
            try {
                for (int attempt = 1; attempt <= MAX_ATTEMPTS && !server.HasExited; attempt++) {
                    if (TryRequestStop()) {
                        stopped = true;
                        break;
                    }
                    if (attempt >= MAX_ATTEMPTS) {
                        continue;
                    }
                    Console.Error.WriteLineAsync(
                        $"docker-unreal-ddc: Zen shutdown attempt {attempt}/{MAX_ATTEMPTS} failed; retrying"
                    ).GetAwaiter().GetResult();
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }
                stopped |= server.HasExited;
                if (stopped) {
                    return;
                }
                Console.Error.WriteLineAsync(
                    "docker-unreal-ddc: Zen did not acknowledge the graceful shutdown request; terminating the server"
                ).GetAwaiter().GetResult();
                server.Kill(true);
            } catch (Exception exception) {
                Console.Error.WriteLineAsync("docker-unreal-ddc: failed to request Zen shutdown: " + exception.Message).GetAwaiter().GetResult();
            } finally {
                Volatile.Write(ref stopState, stopped ? 2 : 0);
            }
        }

        bool TryRequestStop() {
            if (server.HasExited) {
                return true;
            }
            var stop = CreateStopStartInfo(client, workingDirectory, port);
            using var stopProcess = Process.Start(stop);
            if (stopProcess is null) {
                return false;
            }
            if (stopProcess.WaitForExit(TimeSpan.FromSeconds(6))) {
                return stopProcess.ExitCode == 0 && server.WaitForExit(TimeSpan.FromSeconds(2));
            }
            stopProcess.Kill(true);
            return false;
        }

        public void Dispose() {
            Console.CancelKeyPress -= cancelHandler;
            AppDomain.CurrentDomain.ProcessExit -= exitHandler;
            terminateRegistration?.Dispose();
            interruptRegistration?.Dispose();
            // Unregistering blocks while a Windows control handler is active.
            // During shutdown, leave it registered for the remaining lifetime
            // of this process so the main thread can exit normally.
            if (windowsHandler is not null && Volatile.Read(ref stopState) == 0) {
                SetConsoleCtrlHandler(windowsHandler, false);
            }
        }
    }

    enum EConsoleControlType : uint {
        CTRL_C = 0,
        CTRL_BREAK = 1,
        CTRL_CLOSE = 2,
        CTRL_SHUTDOWN = 6
    }

    delegate bool ConsoleControlHandler(EConsoleControlType controlType);

    [LibraryImport("Kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleCtrlHandler(ConsoleControlHandler handler, [MarshalAs(UnmanagedType.Bool)] bool add);
}
