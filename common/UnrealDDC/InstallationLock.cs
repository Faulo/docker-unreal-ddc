using System;
using System.IO;
using System.Threading;

namespace UnrealDDC;

sealed class InstallationLock : IDisposable {
    readonly FileStream _stream;

    InstallationLock(FileStream stream) => _stream = stream;

    public static InstallationLock Acquire(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var nextNotice = DateTime.UtcNow;
        while (true) {
            try {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new InstallationLock(stream);
            } catch (IOException) when (DateTime.UtcNow >= nextNotice) {
                Console.Out.WriteLine("docker-unreal-ddc: waiting for another Zen installation to finish");
                nextNotice = DateTime.UtcNow.AddMinutes(1);
            } catch (IOException) {
                // Another container still owns the shared installation lock.
            }
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }
    }

    public void Dispose() => _stream.Dispose();
}
