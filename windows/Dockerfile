# escape=`

ARG OS_BASE=ltsc2019

FROM mcr.microsoft.com/dotnet/sdk:9.0-nanoserver-1809 AS build

WORKDIR C:/solution

ENV NUGET_PACKAGES=C:/nuget/packages

COPY . .

# hadolint shell=powershell
RUN dotnet publish --runtime win-x64

FROM mcr.microsoft.com/windows/servercore:${OS_BASE}

SHELL ["C:/Windows/System32/WindowsPowerShell/v1.0/powershell", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

USER ContainerAdministrator
# hadolint shell=powershell
RUN New-Item -ItemType Directory -Path C:/unreal-ddc, C:/unreal-ddc/install, C:/unreal-ddc/data -Force | Out-Null

COPY --from=build C:/solution/common/UnrealDDC/bin/Release/net9.0/win-x64/publish/ C:/unreal-ddc/bin/

# hadolint shell=powershell
RUN Move-Item -LiteralPath C:/unreal-ddc/bin/UnrealDDC.exe -Destination C:/unreal-ddc/bin/unreal-ddc.exe; `
    $binDirectory = 'C:\unreal-ddc\bin'; `
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine'); `
    [Environment]::SetEnvironmentVariable('Path', ($binDirectory + ';' + $machinePath), 'Machine'); `
    $env:Path = $binDirectory + ';' + $env:Path

ENV UNREAL_DDC_ROOT="C:\unreal-ddc"
ENV ZEN_VERSION=5

WORKDIR C:/unreal-ddc
# hadolint shell=powershell
RUN unreal-ddc version; `
    if ($LASTEXITCODE -ne 0) { throw 'UnrealDDC smoke check failed' }

VOLUME ["C:/unreal-ddc/install", "C:/unreal-ddc/data"]
EXPOSE 8558

HEALTHCHECK --interval=30s --timeout=10s --start-period=120s --retries=3 `
    CMD ["unreal-ddc", "health"]

ENTRYPOINT ["unreal-ddc"]
CMD ["serve"]
