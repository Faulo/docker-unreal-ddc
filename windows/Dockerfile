# escape=`

ARG OS_BASE=ltsc2019

FROM mcr.microsoft.com/dotnet/sdk:9.0-nanoserver-1809 AS build

WORKDIR C:/solution

ENV NUGET_PACKAGES=C:/solution/.nuget/packages

COPY . .

RUN dotnet publish `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --property:PublishSingleFile=true `
        --property:InvariantGlobalization=true `
        --property:DebugType=None

FROM mcr.microsoft.com/windows/servercore:${OS_BASE}

SHELL ["C:/Windows/System32/WindowsPowerShell/v1.0/powershell", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

USER ContainerAdministrator
RUN New-Item -ItemType Directory -Path C:/unreal-ddc, C:/unreal-ddc/install, C:/unreal-ddc/data -Force | Out-Null

COPY --from=build C:/solution/common/UnrealDDC/bin/Release/net9.0/win-x64/publish/UnrealDDC.exe C:/unreal-ddc/UnrealDDC.exe

ENV UNREAL_DDC_ROOT="C:\unreal-ddc"
ENV ZEN_VERSION=5

WORKDIR C:/unreal-ddc
RUN C:/unreal-ddc/UnrealDDC.exe --launcher-version; `
    if ($LASTEXITCODE -ne 0) { throw 'UnrealDDC smoke check failed' }

VOLUME ["C:/unreal-ddc/install", "C:/unreal-ddc/data"]
EXPOSE 8558

HEALTHCHECK --interval=30s --timeout=10s --start-period=120s --retries=3 `
    CMD ["C:/unreal-ddc/UnrealDDC.exe", "--health"]

ENTRYPOINT ["C:/unreal-ddc/UnrealDDC.exe"]
CMD []
