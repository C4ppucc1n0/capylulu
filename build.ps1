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
# 仅清理早期版本发布到 dist 下的外置动作副本；现有 assets 素材全部内嵌。
$legacyPublishedActions = Join-Path $publishDirectory "generated_actions"
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

if (Test-Path -LiteralPath $legacyPublishedActions) {
    $resolvedLegacy = (Resolve-Path -LiteralPath $legacyPublishedActions).Path
    $expectedLegacy = [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist\CapyLulu\generated_actions'))
    $legacyItem = Get-Item -LiteralPath $resolvedLegacy
    if ($resolvedLegacy -ne $expectedLegacy -or ($legacyItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Refusing to clean an unexpected legacy publish path.'
    }
    Remove-Item -LiteralPath $resolvedLegacy -Recurse -Force
}

Write-Host ""
Write-Host "Build complete: $publishDirectory\CapyLulu.exe"
