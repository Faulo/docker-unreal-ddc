# Dende versus Garl benchmark

Measured on 2026-09-03 with disposable `tmp/unreal-ddc:latest` images and Epic Zen 5.8.20. Both candidates passed the same install, health, cache-write, container-replacement, and persistent cache-read integration contract before benchmarking.

## Recommendation

Run the production DDC on **Garl**. Its Linux candidate was stable throughout the contract and delivered 5.1 times the request rate and 4.7 times the data throughput of Dende for the representative LAN cache workload. Small-file creation was 65 times faster, which matters for a content-addressed cache with a mix of small objects.

Epic's Unreal Engine 5.5 shared Zen guide describes Windows Server as production-ready and Linux as usable for local storage while Linux server tuning continued. Zen 5.8.20 now ships an official Linux server, and this repository validates that exact binary, but the deployment should still be treated as a deliberate newer-version choice and monitored after rollout. See Epic's [shared Zen setup guide](https://dev.epicgames.com/documentation/unreal-engine/set-up-zen-storage-server-as-shared-ddc-for-unreal-engine?lang=en-US).

## Test topology

| Target | Host | Container transport | Resources reported by Zen |
| --- | --- | --- | --- |
| Dende | Windows Server 2019, build 17763 | `http.sys` | 8 logical processors, 15.7 GiB RAM |
| Garl | Ubuntu 24.04 | ASIO | 12 logical processors, 11.7 GiB RAM |

A single Windows Zen client on the same LAN accessed port `18558` published by each disposable container. The order was alternated between trials. Each server used fresh named install and data volumes; those containers and volumes were removed after the run.

## Shared-cache workload

The client seeded 512 deterministic values totaling 277 MiB, using `4KiB:50,64KiB:30,1MiB:15,8MiB:5`, seed `20260903`, and 16 concurrent connections. Each warm-cache trial ran for 10 seconds, plus completion of requests already in flight.

| Target | Seed time | Trial request rates | Mean requests/s | Trial throughputs | Mean MiB/s |
| --- | ---: | --- | ---: | --- | ---: |
| Dende | 29.6 s | 18.5, 22.7, 17.3 | 19.5 | 8.60, 10.9, 9.99 MiB/s | 9.83 |
| Garl | 10.2 s | 99.9, 101.0, 100.2 | 100.4 | 46.2, 46.2, 45.4 MiB/s | 45.9 |

The workload is reproducible with [`common/benchmark-endpoints.ps1`](../common/benchmark-endpoints.ps1). Run it only against disposable or explicitly approved endpoints because it writes approximately 277 MiB into the `docker.unreal.ddc.benchmark/production` cache bucket.

## Data-volume storage

Zen's direct, unbuffered disk benchmark ran inside each container against its named data volume with four threads and 32 MiB per thread (128 MiB total).

| Block | Dende write/read | Garl write/read |
| --- | ---: | ---: |
| 4 KiB | 29.0 / 14.2 MiB/s | 101 / 39.7 MiB/s |
| 64 KiB | 163 / 214 MiB/s | 238 / 162 MiB/s |
| 1 MiB | 215 / 259 MiB/s | 258 / 234 MiB/s |
| 4 MiB | 220 / 250 MiB/s | 258 / 233 MiB/s |

The accompanying 2,000-file, 4 KiB metadata test used four threads:

| Target | Create | Delete |
| --- | ---: | ---: |
| Dende | 629 files/s | 5,195 files/s |
| Garl | 40,816 files/s | 105,263 files/s |

Large sequential reads are close and sometimes favor Dende slightly. Garl's advantage is decisive for mixed cache traffic and small-file metadata, which is reflected in the end-to-end LAN results.
