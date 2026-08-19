param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root 'src\ProjectPublisher.App\ProjectPublisher.App.csproj'
$Output = Join-Path $Root "artifacts\$Runtime"

Push-Location $Root
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET 8 SDK was not found. Install Microsoft.DotNet.SDK.8, restart PowerShell, and run dotnet --version.'
    }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git for Windows was not found. Install it from https://git-scm.com/download/win and restart PowerShell.'
    }
    Write-Host "Using $(dotnet --version) and $(git --version)" -ForegroundColor DarkCyan
    Write-Host 'Restoring packages...' -ForegroundColor Cyan
    dotnet restore GitHubProjectPublisher.sln

    if (-not $SkipTests) {
        Write-Host 'Running security pipeline tests...' -ForegroundColor Cyan
        dotnet test GitHubProjectPublisher.sln -c Release --no-restore
    }

    Write-Host "Restoring the $Runtime runtime pack..." -ForegroundColor Cyan
    dotnet restore $Project -r $Runtime

    Write-Host "Publishing self-contained Windows app for $Runtime..." -ForegroundColor Cyan
    dotnet publish $Project `
        -c Release `
        -r $Runtime `
        --self-contained true `
        --no-restore `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $Output

    Write-Host "Done: $Output" -ForegroundColor Green
    Write-Host 'Run ProjectPublisher.exe from that folder.' -ForegroundColor Green
}
finally {
    Pop-Location
}
