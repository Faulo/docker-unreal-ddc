using System;

namespace UnrealDDC;

enum EZenPlatform {
    Linux,
    Windows
}

sealed record ZenReleaseAsset(
    long Id,
    string Name,
    string ArchiveSha256,
    string ServerFile,
    string ClientFile
);

sealed record ZenRelease(
    string Tag,
    string Version,
    ZenReleaseAsset Linux,
    ZenReleaseAsset Windows
) {
    public static readonly ZenRelease Pinned = new(
        "v5.8.20",
        "5.8.20",
        new ZenReleaseAsset(
            520631651,
            "zenserver-linux.zip",
            "fe3926e7c1cc27352a10ce3a0771b6d1334a77394f105dd5fb468a47513164cf",
            "zenserver",
            "zen"
        ),
        new ZenReleaseAsset(
            520631809,
            "zenserver-win64.zip",
            "1dc0c68e613162e6a2d29d96a0bae52121922a9eb804966ce7b98ba181de999b",
            "zenserver.exe",
            "zen.exe"
        )
    );

    public ZenReleaseAsset AssetFor(EZenPlatform platform) => platform switch {
        EZenPlatform.Linux => Linux,
        EZenPlatform.Windows => Windows,
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported Zen platform")
    };

    public static EZenPlatform CurrentPlatform() {
        if (OperatingSystem.IsLinux()) {
            return EZenPlatform.Linux;
        }
        if (OperatingSystem.IsWindows()) {
            return EZenPlatform.Windows;
        }
        throw new PlatformNotSupportedException("docker-unreal-ddc supports Linux and Windows only");
    }
}
