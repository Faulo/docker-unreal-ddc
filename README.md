# Unreal DDC Docker Image

`faulo/unreal-ddc` runs Epic's Zen Storage Server as a persistent shared Unreal Engine Derived Data Cache (DDC). The image supports Linux amd64 and Windows amd64 on Server 2019, exposes Zen on port 8558, and reports Docker health through the stable `UnrealDDC --health` launcher command.

When credentials are available, the image selects the newest stable Zen release matching `ZEN_VERSION` whenever the container starts. Without credentials it can restart a matching verified installation from the persistent install volume. Linux uses Zen's ASIO HTTP server as an unprivileged UID 10001 process. Windows uses the production-oriented `http.sys` server and runs as `ContainerAdministrator`.

## Runtime acquisition

Epic distributes Zen from the private `EpicGames/zen` repository. To avoid redistributing licensed binaries, the public Docker image contains only the small `UnrealDDC` launcher. When credentials are available, the launcher:

1. queries the available releases using an entitled GitHub account and selects the newest stable version matching `ZEN_VERSION`;
2. downloads the platform-specific archive when that version is not already installed;
3. verifies the archive against the SHA-256 digest published by GitHub;
4. extracts only `zenserver` and the `zen` client;
5. records and revalidates both executable checksums; and
6. publishes the selected installation for the health probe before launching the stock server.

Verified, versioned installations are kept in a volume and reused. A restart without credentials revalidates the active installation's executable checksums and starts it when its version matches `ZEN_VERSION`; credentials remain necessary for the initial installation and update discovery. All direct and file-backed credential variables are removed from the Zen child process environment. Concurrent installations are serialized by a bounded, cancellable lock in that shared volume, and a failed or corrupt download is never published as an installation.

Transient transport, timeout, rate-limit, and server-side download failures are retried four times with bounded exponential backoff. Authentication and entitlement failures fail immediately with a focused diagnostic.

The GitHub account must be linked to an Epic Games account and able to read `EpicGames/zen`. Use and storage of the downloaded binaries remain subject to the [Unreal Engine EULA](https://www.unrealengine.com/eula/unreal).

## Tags and persistent paths

| Platform | Explicit tag | Install volume | Data volume |
| --- | --- | --- | --- |
| Linux amd64 | `latest-linux` | `/unreal-ddc/install` | `/unreal-ddc/data` |
| Windows amd64, Server 2019 | `latest-windows-ltsc2019` | `C:/unreal-ddc/install` | `C:/unreal-ddc/data` |

`latest` is a combined manifest that selects the matching image for the Docker host. The explicit tags are useful in deployment configuration. Always persist both paths. A Linux bind mount must be writable by UID/GID 10001; named volumes are initialized with the correct ownership automatically.

## Run on Linux

Create the volumes and start the service:

```bash
docker volume create unreal-ddc-install
docker volume create unreal-ddc-data
docker run --detach \
  --name unreal-ddc \
  --restart unless-stopped \
  --publish 8558:8558 \
  --env UNREAL_CREDENTIALS_USR \
  --env UNREAL_CREDENTIALS_PSW_FILE=/run/secrets/unreal_credentials_psw \
  --mount type=bind,source=/path/on/host/unreal_credentials_psw,target=/run/secrets/unreal_credentials_psw,readonly \
  --volume unreal-ddc-install:/unreal-ddc/install \
  --volume unreal-ddc-data:/unreal-ddc/data \
  faulo/unreal-ddc:latest-linux
```

## Run on Windows

The equivalent PowerShell commands for a Windows-container daemon mount a secrets directory because Windows containers do not support binding an individual file:

```powershell
docker volume create unreal-ddc-install
docker volume create unreal-ddc-data
$SecretsPath = (Resolve-Path .\secrets).Path
docker run --detach `
    --name unreal-ddc `
    --restart unless-stopped `
    --publish 8558:8558 `
    --env UNREAL_CREDENTIALS_USR `
    --env UNREAL_CREDENTIALS_PSW_FILE=C:/run/secrets/unreal_credentials_psw `
    --mount "type=bind,source=$SecretsPath,target=C:/run/secrets,readonly" `
    --volume unreal-ddc-install:C:/unreal-ddc/install `
    --volume unreal-ddc-data:C:/unreal-ddc/data `
    faulo/unreal-ddc:latest-windows-ltsc2019
```

`UNREAL_CREDENTIALS_USR` is the GitHub username and `UNREAL_CREDENTIALS_PSW` is its token. Each value can instead be read from the path named by its `_FILE` counterpart, and direct and file-backed values can be mixed. Setting both forms for one value is rejected. Missing, unreadable, and empty credential files fail without printing their contents. Credentials are removed from the Zen child process environment and are never placed in a URL, image layer, or installation marker.

For Docker Compose, store the token in `./secrets/unreal_credentials_psw` and use:

```yaml
services:
  unreal-ddc:
    image: faulo/unreal-ddc:latest-linux
    restart: unless-stopped
    ports:
      - "8558:8558"
    environment:
      UNREAL_CREDENTIALS_USR: ${UNREAL_CREDENTIALS_USR}
      UNREAL_CREDENTIALS_PSW_FILE: /run/secrets/unreal_credentials_psw
    secrets:
      - unreal_credentials_psw
    volumes:
      - unreal-ddc-install:/unreal-ddc/install
      - unreal-ddc-data:/unreal-ddc/data

secrets:
  unreal_credentials_psw:
    file: ./secrets/unreal_credentials_psw

volumes:
  unreal-ddc-install:
  unreal-ddc-data:
```

For a Portainer Swarm stack, create `unreal_credentials_psw` in Portainer and replace the top-level secret definition with `external: true`. Linux secrets use `/run/secrets/<name>`; Windows Swarm secrets use `C:/ProgramData/Docker/secrets/<name>`, so set `UNREAL_CREDENTIALS_PSW_FILE` to that Windows path. See Docker's [secrets documentation](https://docs.docker.com/engine/swarm/secrets/).

A Windows Swarm or Portainer stack uses the same external secret with the Windows image and path:

```yaml
services:
  unreal-ddc:
    image: faulo/unreal-ddc:latest-windows-ltsc2019
    environment:
      UNREAL_CREDENTIALS_USR: ${UNREAL_CREDENTIALS_USR}
      UNREAL_CREDENTIALS_PSW_FILE: C:/ProgramData/Docker/secrets/unreal_credentials_psw
    secrets:
      - unreal_credentials_psw

secrets:
  unreal_credentials_psw:
    external: true
```

The image has an empty Docker `CMD`. `UnrealDDC` supplies the production defaults and appends any explicitly provided container arguments to the `zenserver` command line. Zen's stdout and stderr are mirrored to the launcher streams and are therefore available through `docker logs`.

## Runtime configuration

| Variable | Default | Meaning |
| --- | --- | --- |
| `UNREAL_CREDENTIALS_USR` / `UNREAL_CREDENTIALS_USR_FILE` | none | GitHub username or path to a file containing it. Set exactly one form when credentials are supplied. |
| `UNREAL_CREDENTIALS_PSW` / `UNREAL_CREDENTIALS_PSW_FILE` | none | GitHub token or path to a file containing it. Set exactly one form when credentials are supplied. |
| `ZEN_VERSION` | `5` | Semantic version selector. A selector without an operator is a caret range: `5`, `5.8`, and `5.8.20` mean `^5`, `^5.8`, and `^5.8.20`. Prefix a complete version with `=` for an exact release. |
| `ZEN_PORT` | `8558` | Zen HTTP port and health-probe port. |
| `ZEN_DATA_DIR` | platform data volume | Absolute Zen data directory. |
| `ZEN_GC_DISKSIZE_SOFTLIMIT` | Zen default | Value for `--gc-disksize-softlimit`. Accepts bytes or decimal/IEC units such as `100GB`, `1000MB`, or `20GiB`. |
| `ZEN_GC_LOW_DISKSPACE_THRESHOLD` | Zen default | Value for `--gc-low-diskspace-threshold`, using the same byte-size syntax. |
| `ZEN_GC_CACHE_DURATION_SECONDS` | Zen default | Value for `--gc-cache-duration-seconds`. Accepts compact ISO-style durations without the leading `P`, including `10D` and `1Y60S`; `P` and `T` are also accepted. A year is 365 days. |

Caret ranges use semantic compatibility bounds. For example, `^5.8` selects the newest available version greater than or equal to 5.8.0 and lower than 6.0.0. The launcher never selects drafts or prereleases.

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

The contract verifies the daemon OS, authenticated version discovery and acquisition, automatic upgrades, stable launcher health checks, environment normalization, log mirroring, cache writes, clean exit state, container replacement, and persistent cache reads. GitHub Actions builds and publishes both platform variants; the `jenkins/docker-unreal-ddc` job runs this contract against both production hosts.

Explorer-oriented `docker-build-*.bat` and `docker-test-*.bat` entry points use the `linux` or `windows` Docker contexts and pause before closing.

## Production host benchmark

The measured deployment recommendation is **Garl**. Across three identical LAN trials, Garl averaged 100.4 requests/s and 45.9 MiB/s; Dende averaged 19.5 requests/s and 9.83 MiB/s. Garl was also 65 times faster at creating 4 KiB files in the data volume. The exact topology, raw trial values, storage results, support caveat, and reproduction command are in [the 2026-09-03 benchmark report](docs/benchmark-2026-09-03.md).
