using System;
using System.IO;

namespace UnrealDDC.Tests;

sealed class TemporaryDirectory : IDisposable {
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "docker-unreal-ddc-tests", Guid.NewGuid().ToString("N"));

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public void Dispose() {
        if (Directory.Exists(Path)) {
            Directory.Delete(Path, true);
        }
    }
}
