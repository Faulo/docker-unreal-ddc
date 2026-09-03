# Unreal DDC Docker Image

`faulo/unreal-ddc` runs Epic's Zen Storage Server as a persistent shared Unreal Engine Derived Data Cache (DDC). The image supports Linux amd64 and Windows amd64 on Server 2019, exposes Zen on port 8558, and reports Docker health from Zen's readiness endpoint.

The current image pins Zen 5.8.20. Linux uses Zen's ASIO HTTP server as an unprivileged UID 10001 process. Windows uses the production-oriented `http.sys` server and runs as `ContainerAdministrator`.

## Runtime acquisition

Epic distributes Zen from the private `EpicGames/zen` repository. To avoid redistributing licensed binaries, the public Docker image contains only the small `UnrealDDC` launcher. On the first start, the launcher:

1. downloads the platform-specific pinned release using an entitled GitHub account;
2. verifies the release archive against its pinned SHA-256 checksum;
3. extracts only `zenserver` and the `zen` client;
4. records and revalidates both executable checksums on every start; and
5. launches the stock server with the image command.

The verified installation is kept in a volume. Later containers reuse it without credentials. Concurrent first starts are serialized by a lock in that shared volume, and a failed or corrupt download is never published as an installation.

Transient transport, timeout, rate-limit, and server-side download failures are retried four times with bounded exponential backoff. Authentication and entitlement failures fail immediately with a focused diagnostic.

The GitHub account must be linked to an Epic Games account and able to read `EpicGames/zen`. Use and storage of the downloaded binaries remain subject to the [Unreal Engine EULA](https://www.unrealengine.com/eula/unreal).

## Tags and persistent paths

| Platform | Explicit tag | Install volume | Data volume |
| --- | --- | --- | --- |
| Linux amd64 | `latest-linux` | `/unreal-ddc/install` | `/unreal-ddc/data` |
| Windows amd64, Server 2019 | `latest-windows-ltsc2019` | `C:/unreal-ddc/install` | `C:/unreal-ddc/data` |

`latest` is a combined manifest that selects the matching image for the Docker host. The explicit tags are useful in deployment configuration. Always persist both paths. A Linux bind mount must be writable by UID/GID 10001; named volumes are initialized with the correct ownership automatically.

## Run on Linux

Create the volumes and bootstrap the licensed installation once. The `--powercycle` command initializes Zen and exits:

```bash
docker volume create unreal-ddc-install
docker volume create unreal-ddc-data
docker run --rm \
  --env UNREAL_CREDENTIALS_USR \
  --env UNREAL_CREDENTIALS_PSW \
  --volume unreal-ddc-install:/unreal-ddc/install \
  --volume unreal-ddc-data:/unreal-ddc/data \
  faulo/unreal-ddc:latest-linux \
  --powercycle --data-dir=/unreal-ddc/data --http=asio \
  --no-sentry --no-log-file --detach=false \
  --status-panel=false --enable-execution-history=false --register-server=false
```

Start the service without forwarding credentials into its container configuration:

```bash
docker run --detach \
  --name unreal-ddc \
  --restart unless-stopped \
  --publish 8558:8558 \
  --volume unreal-ddc-install:/unreal-ddc/install \
  --volume unreal-ddc-data:/unreal-ddc/data \
  faulo/unreal-ddc:latest-linux
```

## Run on Windows

The equivalent PowerShell commands for a Windows-container daemon are:

```powershell
docker volume create unreal-ddc-install
docker volume create unreal-ddc-data
docker run --rm `
    --env UNREAL_CREDENTIALS_USR `
    --env UNREAL_CREDENTIALS_PSW `
    --volume unreal-ddc-install:C:/unreal-ddc/install `
    --volume unreal-ddc-data:C:/unreal-ddc/data `
    faulo/unreal-ddc:latest-windows-ltsc2019 `
    --powercycle --data-dir=C:/unreal-ddc/data --http=httpsys `
    --no-sentry --no-log-file --detach=false `
    --status-panel=false --enable-execution-history=false --register-server=false

docker run --detach `
    --name unreal-ddc `
    --restart unless-stopped `
    --publish 8558:8558 `
    --volume unreal-ddc-install:C:/unreal-ddc/install `
    --volume unreal-ddc-data:C:/unreal-ddc/data `
    faulo/unreal-ddc:latest-windows-ltsc2019
```

`UNREAL_CREDENTIALS_USR` is the GitHub username and `UNREAL_CREDENTIALS_PSW` is its token. They must be supplied together and are used only when the pinned installation is absent or fails checksum validation. They are removed from the Zen child process environment and are never placed in a URL, image layer, or installation marker.

Additional container arguments replace the default command and are forwarded unchanged to `zenserver`. Preserve `--dedicated`, an explicit `--port`, `--data-dir`, the platform's `--http` implementation, and `--detach=false` when defining a custom production command.

## Connect Unreal Engine

Set Unreal's supported shared Zen override to the server URL:

```text
UE-ZenSharedDataCacheHost=http://garl:8558
```

The companion `faulo/unreal` image exposes this as its `UNREAL_DDC` setting. Zen's default HTTP endpoint is neither encrypted nor authenticated, so publish it only on a trusted network and never directly to the internet. Epic recommends a stable hostname or DNS entry and a dedicated data disk for shared deployments. See Epic's [DDC overview](https://dev.epicgames.com/documentation/unreal-engine/using-derived-data-cache-in-unreal-engine?lang=en-US) and [shared Zen setup guide](https://dev.epicgames.com/documentation/unreal-engine/set-up-zen-storage-server-as-shared-ddc-for-unreal-engine?lang=en-US).

## Build and validation

Launcher unit tests do not download Zen or require Docker:

```powershell
dotnet test docker-unreal-ddc.sln --configuration Release
```

Build only disposable candidates and always select the remote daemon explicitly:

```powershell
docker --context garl build --tag tmp/unreal-ddc:latest --file linux/Dockerfile .
docker --context dende build --tag tmp/unreal-ddc:latest --file windows/Dockerfile .
```

With the two credential environment variables set, run the full image contract:

```powershell
pwsh common/test-images.ps1 -DockerContext garl -ExpectedOs linux -Image tmp/unreal-ddc:latest
pwsh common/test-images.ps1 -DockerContext dende -ExpectedOs windows -Image tmp/unreal-ddc:latest
```

The contract verifies the daemon OS, authenticated acquisition, exact Zen version, readiness, cache writes, clean shutdown, container replacement, credential-free restart, and persistent cache reads. GitHub Actions builds and publishes both platform variants; the `jenkins/docker-unreal-ddc` job runs this contract against both production hosts.

Explorer-oriented `docker-build-*.bat` and `docker-test-*.bat` entry points use the `linux` or `windows` Docker contexts and pause before closing.

## Production host benchmark

The measured deployment recommendation is **Garl**. Across three identical LAN trials, Garl averaged 100.4 requests/s and 45.9 MiB/s; Dende averaged 19.5 requests/s and 9.83 MiB/s. Garl was also 65 times faster at creating 4 KiB files in the data volume. The exact topology, raw trial values, storage results, support caveat, and reproduction command are in [the 2026-09-03 benchmark report](docs/benchmark-2026-09-03.md).
