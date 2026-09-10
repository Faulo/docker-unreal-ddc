FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build

WORKDIR /solution

COPY . .

RUN dotnet publish \
        --configuration Release \
        --runtime linux-x64 \
        --self-contained true \
        --property:PublishSingleFile=true \
        --property:InvariantGlobalization=true \
        --property:DebugType=None

FROM debian:bookworm-slim

ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates && \
    rm -rf /var/lib/apt/lists/* && \
    groupadd --gid 10001 unrealddc && \
    useradd --uid 10001 --gid 10001 --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin unrealddc && \
    install -d -o 10001 -g 10001 /unreal-ddc /unreal-ddc/install /unreal-ddc/data

COPY --from=build --chown=10001:10001 /solution/common/UnrealDDC/bin/Release/net9.0/linux-x64/publish/UnrealDDC /unreal-ddc/UnrealDDC

ENV UNREAL_DDC_ROOT=/unreal-ddc
ENV HOME=/unreal-ddc
ENV ZEN_VERSION=5

USER 10001:10001
WORKDIR /unreal-ddc

RUN ./UnrealDDC --launcher-version

VOLUME ["/unreal-ddc/install", "/unreal-ddc/data"]
EXPOSE 8558

HEALTHCHECK --interval=30s --timeout=10s --start-period=120s --retries=3 \
    CMD ["/unreal-ddc/UnrealDDC", "--health"]

ENTRYPOINT ["/unreal-ddc/UnrealDDC"]
CMD []
