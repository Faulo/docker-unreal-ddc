FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS launcher

WORKDIR /source
COPY common/UnrealDDC/ ./
RUN dotnet publish UnrealDDC.csproj \
        --configuration Release \
        --runtime linux-x64 \
        --self-contained true \
        --property:PublishSingleFile=true \
        --property:InvariantGlobalization=true \
        --property:DebugType=None \
        --output /out && \
    test "$(find /out -maxdepth 1 -type f | wc -l)" -eq 1

FROM ubuntu:24.04

ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates && \
    rm -rf /var/lib/apt/lists/* && \
    groupadd --gid 10001 unrealddc && \
    useradd --uid 10001 --gid 10001 --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin unrealddc && \
    install -d -o 10001 -g 10001 /unreal-ddc /unreal-ddc/install /unreal-ddc/data

COPY --from=launcher --chown=10001:10001 /out/UnrealDDC /unreal-ddc/UnrealDDC

ENV UNREAL_DDC_ROOT=/unreal-ddc
ENV HOME=/unreal-ddc
ENV ZEN_RELEASE_VERSION=5.8.20

USER 10001:10001
WORKDIR /unreal-ddc

RUN ./UnrealDDC --launcher-version

VOLUME ["/unreal-ddc/install", "/unreal-ddc/data"]
EXPOSE 8558

HEALTHCHECK --interval=30s --timeout=10s --start-period=120s --retries=3 \
    CMD ["/unreal-ddc/install/v5.8.20/linux/zen", "version", "http://127.0.0.1:8558"]

ENTRYPOINT ["/unreal-ddc/UnrealDDC"]
CMD ["--dedicated", "--port=8558", "--data-dir=/unreal-ddc/data", "--http=asio", "--no-sentry", "--no-log-file", "--detach=false", "--status-panel=false", "--enable-execution-history=false", "--register-server=false"]
