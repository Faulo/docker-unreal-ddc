using System;
using System.IO;
using System.Threading;

namespace UnrealDDC;

sealed class InstallationLock : IDisposable {
    static readonly TimeSpan waitTimeout = TimeSpan.FromMinutes(10);
    readonly FileStream stream;

    InstallationLock(FileStream stream) => this.stream = stream;

    public static InstallationLock Acquire(string path, CancellationToken cancellationToken = default) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var nextNotice = DateTime.UtcNow;
        var deadline = DateTime.UtcNow + waitTimeout;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new InstallationLock(stream);
            } catch (IOException exception) when (IsLockContention(exception)) {
                if (DateTime.UtcNow >= deadline) {
                    throw new TimeoutException("Timed out waiting for another Zen installation to finish", exception);
                }
                if (DateTime.UtcNow >= nextNotice) {
                    Console.Out.WriteLineAsync("docker-unreal-ddc: waiting for another Zen installation to finish").GetAwaiter().GetResult();
                    nextNotice = DateTime.UtcNow.AddMinutes(1);
                }
            }
            if (cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(1))) {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    static bool IsLockContention(IOException exception) {
        int errorCode = exception.HResult & 0xffff;
        return errorCode is 11 or 32 or 33;
    }

    public void Dispose() => stream.Dispose();
}
