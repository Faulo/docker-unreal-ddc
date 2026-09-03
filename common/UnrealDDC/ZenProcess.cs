using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace UnrealDDC;

static class ZenProcess {
    public static async Task<int> RunAsync(ZenInstallation installation, IEnumerable<string> arguments) {
        var start = CreateStartInfo(installation.Server, arguments, installation.Directory);
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("Failed to start Zen server");
        using var shutdown = new ShutdownCoordinator(process, installation.Client, installation.Directory);
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments, string workingDirectory) {
        var start = new ProcessStartInfo {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        foreach (string argument in arguments) {
            start.ArgumentList.Add(argument);
        }
        RemoveCredentials(start);
        return start;
    }

    internal static void RemoveCredentials(ProcessStartInfo start) {
        start.Environment.Remove(EnvironmentVariableNames.UNREAL_CREDENTIALS_USR);
        start.Environment.Remove(EnvironmentVariableNames.UNREAL_CREDENTIALS_PSW);
    }

    sealed class ShutdownCoordinator : IDisposable {
        readonly Process _server;
        readonly string _client;
        readonly string _workingDirectory;
        readonly ConsoleCancelEventHandler _cancelHandler;
        readonly EventHandler _exitHandler;
        readonly PosixSignalRegistration? _terminateRegistration;
        readonly PosixSignalRegistration? _interruptRegistration;
        readonly ConsoleControlHandler? _windowsHandler;
        int _stopRequested;

        public ShutdownCoordinator(Process server, string client, string workingDirectory) {
            _server = server;
            _client = client;
            _workingDirectory = workingDirectory;
            _cancelHandler = OnCancel;
            _exitHandler = OnExit;
            Console.CancelKeyPress += _cancelHandler;
            AppDomain.CurrentDomain.ProcessExit += _exitHandler;

            if (OperatingSystem.IsWindows()) {
                _windowsHandler = OnWindowsControl;
                if (!SetConsoleCtrlHandler(_windowsHandler, true)) {
                    throw new InvalidOperationException("Failed to register the Windows shutdown handler");
                }
            } else {
                _terminateRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnPosixSignal);
                _interruptRegistration = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnPosixSignal);
            }
        }

        void OnCancel(object? sender, ConsoleCancelEventArgs arguments) {
            arguments.Cancel = true;
            _ = Task.Run(RequestStop);
        }

        void OnExit(object? sender, EventArgs arguments) => RequestStop();

        void OnPosixSignal(PosixSignalContext context) {
            context.Cancel = true;
            _ = Task.Run(RequestStop);
        }

        bool OnWindowsControl(EConsoleControlType controlType) {
            if (controlType is EConsoleControlType.CtrlC
                or EConsoleControlType.CtrlBreak
                or EConsoleControlType.CtrlClose
                or EConsoleControlType.CtrlShutdown) {
                RequestStop();
                return true;
            }
            return false;
        }

        void RequestStop() {
            if (Interlocked.Exchange(ref _stopRequested, 1) != 0) {
                return;
            }

            try {
                if (_server.HasExited) {
                    return;
                }

                var stop = CreateStartInfo(
                    _client,
                    [
                        "--no-sentry",
                        "--no-log-file",
                        "--enable-execution-history=false",
                        "down",
                        "--pid",
                        _server.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "--force"
                    ],
                    _workingDirectory
                );
                using var stopProcess = Process.Start(stop);
                if (stopProcess is null || !stopProcess.WaitForExit(TimeSpan.FromSeconds(25)) || stopProcess.ExitCode != 0) {
                    Console.Error.WriteLine("docker-unreal-ddc: Zen did not acknowledge the graceful shutdown request");
                }
            } catch (Exception exception) {
                Console.Error.WriteLine("docker-unreal-ddc: failed to request Zen shutdown: " + exception.Message);
            }
        }

        public void Dispose() {
            Console.CancelKeyPress -= _cancelHandler;
            AppDomain.CurrentDomain.ProcessExit -= _exitHandler;
            _terminateRegistration?.Dispose();
            _interruptRegistration?.Dispose();
            if (_windowsHandler is not null) {
                SetConsoleCtrlHandler(_windowsHandler, false);
            }
        }
    }

    enum EConsoleControlType : uint {
        CtrlC = 0,
        CtrlBreak = 1,
        CtrlClose = 2,
        CtrlLogoff = 5,
        CtrlShutdown = 6
    }

    delegate bool ConsoleControlHandler(EConsoleControlType controlType);

    [DllImport("Kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetConsoleCtrlHandler(ConsoleControlHandler handler, [MarshalAs(UnmanagedType.Bool)] bool add);
}
