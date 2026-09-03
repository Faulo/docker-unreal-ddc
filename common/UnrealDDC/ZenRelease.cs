using System;

namespace UnrealDDC;

enum EZenPlatform {
    LINUX,
    WINDOWS
}

sealed record ZenReleaseAsset(
    long id,
    string name,
    string archiveSha256,
    string serverFile,
    string clientFile
);

sealed record ZenRelease(
    string tag,
    Version version,
    ZenReleaseAsset linux,
    ZenReleaseAsset windows
) {
    public ZenReleaseAsset AssetFor(EZenPlatform platform) => platform switch {
        EZenPlatform.LINUX => linux,
        EZenPlatform.WINDOWS => windows,
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported Zen platform")
    };

    public static EZenPlatform CurrentPlatform() {
        if (OperatingSystem.IsLinux()) {
            return EZenPlatform.LINUX;
        }
        return OperatingSystem.IsWindows()
            ? EZenPlatform.WINDOWS
            : throw new PlatformNotSupportedException("docker-unreal-ddc supports Linux and Windows only");
    }
}
