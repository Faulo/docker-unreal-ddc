using System;
using System.IO;

namespace UnrealDDC.Tests;

sealed class TemporaryDirectory : IDisposable {
    public string path { get; } = Path.Combine(Path.GetTempPath(), "docker-unreal-ddc-tests", Guid.NewGuid().ToString("N"));

    public TemporaryDirectory() => Directory.CreateDirectory(path);

    public void Dispose() {
        if (Directory.Exists(path)) {
            Directory.Delete(path, true);
        }
    }
}
