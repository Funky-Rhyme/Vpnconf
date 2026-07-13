#!/usr/bin/env pwsh
# Builds the self-contained Native AOT binary (vpnconf.exe).
#
# Why this script: the AOT link step calls `vswhere.exe` to locate the MSVC toolchain, but the
# VS Installer directory (where vswhere lives) is not always on PATH. We prepend it so the link
# step can find the C++ toolchain. Requires the "Desktop development with C++" workload.
#
# Usage:  ./publish-aot.ps1 [-Runtime win-x64]
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$installerDir = "C:\Program Files (x86)\Microsoft Visual Studio\Installer"
if (Test-Path (Join-Path $installerDir "vswhere.exe")) {
    $env:PATH = "$installerDir;$env:PATH"
}

$project = Join-Path $PSScriptRoot "NetworkRoutesConflictResolver\NetworkRoutesConflictResolver.csproj"

dotnet publish $project -r $Runtime -c Release
if ($LASTEXITCODE -ne 0) {
    throw "AOT publish failed with exit code $LASTEXITCODE."
}

$out = Join-Path $PSScriptRoot "NetworkRoutesConflictResolver\bin\Release\net10.0\$Runtime\publish\vpnconf.exe"
Write-Host "`nNative AOT binary: $out" -ForegroundColor Green
