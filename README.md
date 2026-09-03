# Unreal DDC Docker Image

This repository defines Linux and Windows variants of `faulo/unreal-ddc`, a long-running Unreal Zen Storage Server for a shared Derived Data Cache (DDC).

The integration contract requires both variants to:

- install the pinned Zen release from Epic using an entitled GitHub account;
- expose the stock Zen HTTP service on port 8558;
- report Docker health from Zen's readiness endpoint;
- persist both the verified Zen installation and DDC data in named volumes;
- retain cached values across container replacement; and
- run the Windows `http.sys` and Linux ASIO transports respectively.

Epic distributes Zen binaries only to accounts with access to `EpicGames/zen`. The repository and public launcher image therefore do not redistribute those binaries. A container downloads and verifies the pinned release on its first start using `UNREAL_CREDENTIALS_USR` and `UNREAL_CREDENTIALS_PSW`, then reuses the installation volume without credentials on subsequent starts.

Implementation, configuration, build, deployment, and benchmark details will be added with the image.
