# escape=`

ARG OS_BASE=ltsc2019

FROM mcr.microsoft.com/dotnet/sdk:9.0-nanoserver-1809 AS launcher

WORKDIR C:/source
COPY common/UnrealDDC/ ./
RUN dotnet publish UnrealDDC.csproj `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --property:PublishSingleFile=true `
        --property:InvariantGlobalization=true `
        --property:DebugType=None `
        --output C:/out

FROM mcr.microsoft.com/windows/servercore:${OS_BASE}

SHELL ["C:/Windows/System32/WindowsPowerShell/v1.0/powershell", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

USER ContainerAdministrator
RUN New-Item -ItemType Directory -Path C:/unreal-ddc, C:/unreal-ddc/install, C:/unreal-ddc/data -Force | Out-Null

COPY --from=launcher C:/out/UnrealDDC.exe C:/unreal-ddc/UnrealDDC.exe

ENV UNREAL_DDC_ROOT="C:\unreal-ddc"
ENV ZEN_RELEASE_VERSION=5.8.20

WORKDIR C:/unreal-ddc
RUN C:/unreal-ddc/UnrealDDC.exe --launcher-version; `
    if ($LASTEXITCODE -ne 0) { throw 'UnrealDDC smoke check failed' }

VOLUME ["C:/unreal-ddc/install", "C:/unreal-ddc/data"]
EXPOSE 8558

HEALTHCHECK --interval=30s --timeout=10s --start-period=120s --retries=3 `
    CMD ["C:/unreal-ddc/install/v5.8.20/windows/zen.exe", "version", "http://127.0.0.1:8558"]

ENTRYPOINT ["C:/unreal-ddc/UnrealDDC.exe"]
CMD ["--dedicated", "--port=8558", "--data-dir=C:/unreal-ddc/data", "--http=httpsys", "--no-sentry", "--no-log-file", "--detach=false", "--status-panel=false", "--enable-execution-history=false", "--register-server=false"]
