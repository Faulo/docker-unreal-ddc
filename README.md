# Unreal DDC Docker Image

`faulo/unreal-ddc` runs Epic's Zen Storage Server as a persistent shared Unreal Engine Derived Data Cache (DDC). The image supports Linux amd64 on Debian 13 (Trixie) and Windows amd64 on Server 2019, exposes Zen on port 8558, and reports Docker health through the stable `unreal-ddc health` launcher command.

When credentials are available, the image selects the newest stable Zen release matching `ZEN_VERSION` whenever the container starts. Without credentials it can restart a matching verified installation from the persistent install volume. Linux uses Zen's ASIO HTTP server as an unprivileged UID 10001 process. Windows uses the production-oriented `http.sys` server and runs as `ContainerAdministrator`.

## Runtime acquisition

Epic distributes Zen from the private `EpicGames/zen` repository. To avoid redistributing licensed binaries, the public Docker image contains only the small `UnrealDDC` launcher. When credentials are available, the launcher:

1. queries the available releases using an entitled GitHub account and selects the newest stable version matching `ZEN_VERSION`;
2. downloads the platform-specific archive when that version is not already installed;
3. verifies the archive against the SHA-256 digest published by GitHub;
4. extracts only `zenserver` and the `zen` client;
5. records and revalidates both executable checksums; and
6. publishes the selected installation for the health probe before launching the stock server.

Verified, versioned installations are kept in a volume and reused. A restart without credentials revalidates the active installation's executable checksums and starts it when its version matches `ZEN_VERSION`; credentials remain necessary for the initial installation, every automatic update check, and selecting an uncached version. All direct and file-backed credential variables are removed from the Zen child process environment. Concurrent installations are serialized by a bounded, cancellable lock in that shared volume, and a failed or corrupt download is never published as an installation.

Transient transport, timeout, rate-limit, and server-side download failures are retried four times with bounded exponential backoff. Authentication and entitlement failures fail immediately with a focused diagnostic.

The GitHub account must be linked to an Epic Games account and able to read `EpicGames/zen`. Use and storage of the downloaded binaries remain subject to the [Unreal Engine EULA](https://www.unrealengine.com/eula/unreal).

## Tags and persistent paths

| Platform | Explicit tag | Install volume | Data volume |
| --- | --- | --- | --- |
| Linux amd64 | `latest-linux-amd64` | `/unreal-ddc/install` | `/unreal-ddc/data` |
| Windows amd64, Server 2019 | `latest-windowsservercore-ltsc2019` | `C:/unreal-ddc/install` | `C:/unreal-ddc/data` |

`latest` is a combined manifest that selects the matching image for the Docker host. The target-specific tags contain only the selected platform image and its attestations. Use the Windows tag to select Server Core LTSC 2019 explicitly. Historical platform tags are retained but no longer updated. Always persist both paths. A Linux bind mount must be writable by UID/GID 10001; named volumes are initialized with the correct ownership automatically.

## Run on Linux

Create the volumes and start the service:

```bash
docker volume create unreal-ddc-install
docker volume create unreal-ddc-data
docker run --detach \
  --name unreal-ddc \
  --restart unless-stopped \
  --publish 8558:8558 \
  --env UNREAL_CREDENTIALS_PAT_FILE=/run/secrets/github_unreal_ddc_pat \
  --mount type=bind,source=/path/on/host/github_unreal_ddc_pat,target=/run/secrets/github_unreal_ddc_pat,readonly \
  --volume unreal-ddc-install:/unreal-ddc/install \
  --volume unreal-ddc-data:/unreal-ddc/data \
  faulo/unreal-ddc:latest-linux-amd64
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
    --env UNREAL_CREDENTIALS_PAT_FILE=C:/run/secrets/github_unreal_ddc_pat `
    --mount "type=bind,source=$SecretsPath,target=C:/run/secrets,readonly" `
    --volume unreal-ddc-install:C:/unreal-ddc/install `
    --volume unreal-ddc-data:C:/unreal-ddc/data `
    faulo/unreal-ddc:latest-windowsservercore-ltsc2019
```

Set exactly one of `UNREAL_CREDENTIALS_PAT` or `UNREAL_CREDENTIALS_PAT_FILE`. The first contains a GitHub personal access token; the second names a file containing it. Empty or unreadable files fail without printing their path or contents. The launcher removes credential variables from Zen child processes, and credential contents are never placed in a URL, image layer, installation marker, or log.

The former `UNREAL_CREDENTIALS_USR`/`UNREAL_CREDENTIALS_PSW` pair and their `_FILE` forms remain available temporarily. Legacy values must still form a complete pair, and each value must use either its direct or file form. The launcher rejects any mixture of PAT and legacy variables and warns when it uses the legacy pair. The legacy interface may be removed in the next major version; migrate deployments to PAT now.

## Credential files and Docker secrets

For Linux Docker Compose, including a Portainer stack backed by a Compose file, mount the token as a service secret:

```yaml
services:
  unreal-ddc:
    image: faulo/unreal-ddc:latest-linux-amd64
    restart: unless-stopped
    ports:
      - "8558:8558"
    environment:
      UNREAL_CREDENTIALS_PAT_FILE: /run/secrets/github_unreal_ddc_pat
    secrets:
      - github_unreal_ddc_pat
    volumes:
      - unreal-ddc-install:/unreal-ddc/install
      - unreal-ddc-data:/unreal-ddc/data

secrets:
  github_unreal_ddc_pat:
    file: ./secrets/github_unreal_ddc_pat

volumes:
  unreal-ddc-install:
  unreal-ddc-data:
```

For a Portainer-managed Swarm stack, create `github_unreal_ddc_pat` as a Portainer/Docker secret first and replace the top-level declaration with:

```yaml
secrets:
  github_unreal_ddc_pat:
    external: true
```

Windows Docker secrets require Swarm. The default Windows secret directory is `C:/ProgramData/Docker/secrets`; this Portainer-compatible stack fragment selects the Windows image and points the launcher at the mounted token:

```yaml
services:
  unreal-ddc:
    image: faulo/unreal-ddc:latest-windowsservercore-ltsc2019
    environment:
      UNREAL_CREDENTIALS_PAT_FILE: C:/ProgramData/Docker/secrets/github_unreal_ddc_pat
    secrets:
      - github_unreal_ddc_pat
    deploy:
      replicas: 1
      placement:
        constraints:
          - node.platform.os == windows

secrets:
  github_unreal_ddc_pat:
    external: true
```

Create the external secret through Portainer or with `docker secret create github_unreal_ddc_pat <token-file>`, then deploy the stack. For a standalone Windows container, bind-mount a read-only directory containing the token and set `UNREAL_CREDENTIALS_PAT_FILE` to that container path; standalone Compose secrets support Linux containers only. See Docker's [Compose secrets guide](https://docs.docker.com/compose/how-tos/use-secrets/) and [Windows Swarm secret notes](https://docs.docker.com/engine/swarm/secrets/#windows-support).

The image is a dedicated appliance. Its fixed `unreal-ddc` entrypoint uses `serve` as the default command, so starting a container without an explicit command starts Zen server mode. The launcher accepts exactly `serve`, `health`, or `version`; it does not forward arbitrary arguments to `zenserver`. Docker's explicit `--entrypoint` option remains available for low-level diagnostics. `unreal-ddc version` reports the launcher version and whether a detected Zen installation is missing, verified, or invalid. Zen's stdout and stderr are mirrored to the launcher streams and are therefore available through `docker logs`.

## Runtime configuration

| Variable | Default | Meaning |
| --- | --- | --- |
| `UNREAL_CREDENTIALS_PAT` / `UNREAL_CREDENTIALS_PAT_FILE` | none | GitHub token or path to a file containing it. Set exactly one form when credentials are supplied. |
| `UNREAL_CREDENTIALS_USR` / `UNREAL_CREDENTIALS_USR_FILE` | none | Deprecated GitHub username or path to a file containing it. Requires the legacy token pair. |
| `UNREAL_CREDENTIALS_PSW` / `UNREAL_CREDENTIALS_PSW_FILE` | none | Deprecated GitHub token or path to a file containing it. Requires the legacy username pair. |
| `ZEN_VERSION` | `5` | Version prefix or exact semantic version. `5`/`5.*` selects the newest `5.x` release, `5.8`/`5.8.*` selects the newest `5.8.x` release, and `5.8.20` selects only that release. |
| `ZEN_PORT` | `8558` | Zen HTTP port and health-probe port. |
| `ZEN_DATA_DIR` | platform data volume | Absolute Zen data directory. |
| `ZEN_GC_DISKSIZE_SOFTLIMIT` | Zen default | Value for `--gc-disksize-softlimit`. Accepts bytes or decimal/IEC units such as `100GB`, `1000MB`, or `20GiB`. |
| `ZEN_GC_LOW_DISKSPACE_THRESHOLD` | Zen default | Value for `--gc-low-diskspace-threshold`, using the same byte-size syntax. |
| `ZEN_GC_CACHE_DURATION` | Zen default | Value for `--gc-cache-duration-seconds`. Accepts compact ISO-style durations without the leading `P`, including `10D` and `1Y60S`; `P` and `T` are also accepted. A year is 365 days. |

Prefix selectors have strict component bounds: `5` is at least 5.0.0 and lower than 6.0.0, while `5.8` is at least 5.8.0 and lower than 5.9.0. The launcher never selects drafts or prereleases. Operator forms such as `^5` and `=5.8.20` are not supported.

## Connect Unreal Engine

Set Unreal's supported shared Zen override to the server URL:

```text
UE-ZenSharedDataCacheHost=http://garl:8558
```

The companion `faulo/unreal` image exposes this as its `UNREAL_DDC` setting. Zen's default HTTP endpoint is neither encrypted nor authenticated, so publish it only on a trusted network and never directly to the internet. Epic recommends a stable hostname or DNS entry and a dedicated data disk for shared deployments. See Epic's [DDC overview](https://dev.epicgames.com/documentation/unreal-engine/using-derived-data-cache-in-unreal-engine?lang=en-US) and [shared Zen setup guide](https://dev.epicgames.com/documentation/unreal-engine/set-up-zen-storage-server-as-shared-ddc-for-unreal-engine?lang=en-US).

## Build and validation

Both Dockerfiles publish the complete solution and copy the launcher payload into the platform’s `/unreal-ddc/bin` or `C:/unreal-ddc/bin` directory on `PATH`. Publish settings live in the application project; test projects are excluded from publication. The public `unreal-ddc` command remains available, including through `docker exec`.

Launcher unit tests do not download Zen or require Docker:

```powershell
dotnet test docker-unreal-ddc.sln --configuration Release --logger junit
```

Build only disposable candidates and always select the remote daemon explicitly:

```powershell
docker --context garl build --tag tmp/unreal-ddc:latest --file linux/Dockerfile .
docker --context dende build --tag tmp/unreal-ddc:latest --file windows/Dockerfile .
```

With `UNREAL_CREDENTIALS_PAT` set, run the full image contract:

```powershell
pwsh common/test-images.ps1 -DockerContext garl -ExpectedOs linux -Image tmp/unreal-ddc:latest
pwsh common/test-images.ps1 -DockerContext dende -ExpectedOs windows -Image tmp/unreal-ddc:latest
```

The contract verifies the daemon OS, authenticated version discovery and acquisition, automatic upgrades, stable launcher health checks, environment normalization, log mirroring, cache writes, clean exit state, container replacement, and persistent cache reads. GitHub Actions builds and publishes both platform variants; the `jenkins/docker-unreal-ddc` job runs this contract against both production hosts.

Explorer-oriented `docker-build-*.bat` and `docker-test-*.bat` entry points use the `linux` or `windows` Docker contexts and pause before closing.

## Production host benchmark

The measured deployment recommendation is **Garl**. Across three identical LAN trials, Garl averaged 100.4 requests/s and 45.9 MiB/s; Dende averaged 19.5 requests/s and 9.83 MiB/s. Garl was also 65 times faster at creating 4 KiB files in the data volume. The exact topology, raw trial values, storage results, support caveat, and reproduction command are in [the 2026-09-03 benchmark report](docs/benchmark-2026-09-03.md).

Jenkins runs Release unit tests with `--logger junit` in .NET 9 SDK containers on
both Linux and Windows agents and publishes `**/TestResults.xml` with its native `junit` step.
Each test project writes its report to `TestResults/TestResults.xml`.
Image integration tests continue to use the targets and variants configured
in `.jenkins/pesterProject.properties`.

Docker publication uses the canonical `Faulo/workflows-docker@v3` caller and schema-version-2 `docker.json`. The shared workflow resolves the namespace and image from `.env`, builds Linux amd64 and Windows Server Core LTSC 2019, publishes `latest` and both target-specific tags, verifies digests/platform metadata, and deletes only its run-specific Windows staging tags. Docker Hub credentials must permit publication and staging-tag deletion. GitHub performs image builds and publication; Jenkins owns project tests. Pushes to `main`, manual dispatch, and the monthly schedule at 03:17 UTC on the first day trigger publication.
