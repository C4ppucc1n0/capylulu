param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$localDotnet = Join-Path $projectRoot ".dotnet\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
$project = Join-Path $projectRoot "src\CapyLulu\CapyLulu.csproj"
$publishDirectory = Join-Path $projectRoot "dist\CapyLulu"
$publishedActions = Join-Path $publishDirectory "generated_actions"
$localNugetPackages = Join-Path $projectRoot ".nuget\packages"

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:NUGET_PACKAGES = $localNugetPackages

# 动作图作为嵌入资源：先清理编译缓存，避免已删除的资源残留在 EXE 中。
& $dotnet clean $project --configuration $Configuration --runtime win-x64 --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "Build cleanup failed." }

& $dotnet restore $project --runtime win-x64 --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw "Dependency restore failed." }

& $dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $publishDirectory `
    -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

if (Test-Path -LiteralPath $publishedActions) {
    Remove-Item -LiteralPath $publishedActions -Recurse -Force
}

Write-Host ""
Write-Host "Build complete: $publishDirectory\CapyLulu.exe"
