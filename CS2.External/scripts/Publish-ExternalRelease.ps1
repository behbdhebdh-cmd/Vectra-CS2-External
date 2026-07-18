[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$externalRoot = Split-Path -Parent $PSScriptRoot
$workspace = Split-Path -Parent $externalRoot
[xml]$versionProps = Get-Content -LiteralPath (Join-Path $externalRoot 'Vectra.External\Version.props')
$version = $versionProps.Project.PropertyGroup.VersionPrefix
$offsetDirectory = Join-Path $workspace 'offsets'
$offsetInfoPath = Join-Path $offsetDirectory 'info.json'
$offsetInfo = Get-Content -Raw -LiteralPath $offsetInfoPath | ConvertFrom-Json
$generatedOffsetFiles = @('offsets.cs', 'client_dll.cs', 'animationsystem_dll.cs')
$generatedStamps = foreach ($file in $generatedOffsetFiles) {
    $path = Join-Path $offsetDirectory $file
    if (-not (Test-Path -LiteralPath $path)) { throw "Required generated offset file is missing: $file" }
    $header = @(Get-Content -LiteralPath $path -TotalCount 2)
    if ($header.Count -lt 2 -or $header[0] -ne '// Generated using https://github.com/a2x/cs2-dumper') { throw "Offset file is not recognized cs2-dumper output: $file" }
    $header[1]
}
$uniqueStamps = @($generatedStamps | Select-Object -Unique)
if ($uniqueStamps.Count -ne 1) { throw 'Generated offset sources do not come from the same dump.' }
$sourceTimestamp = ($uniqueStamps[0] -replace '^//\s*', '' -replace '\s+UTC$', '')
$infoTimestamp = [DateTimeOffset]::Parse([string]$offsetInfo.timestamp).UtcDateTime.ToString('yyyy-MM-dd HH:mm:ss')
if ($sourceTimestamp.Substring(0, 19) -ne $infoTimestamp) { throw 'Generated offset sources and offsets/info.json do not come from the same dump.' }
if ([int]$offsetInfo.build_number -le 0) { throw 'offsets/info.json contains no valid CS2 build number.' }

$tag = "v$version-cs2.$($offsetInfo.build_number)"
$stagingRoot = Join-Path $externalRoot '.release-staging'
$externalTests = Join-Path $stagingRoot "external-tests-$tag"
$loaderTests = Join-Path $stagingRoot "loader-tests-$tag"
$externalArtifacts = Join-Path $stagingRoot "external-publish-$tag"
$loaderArtifacts = Join-Path $stagingRoot "loader-publish-$tag"
$externalPublish = Join-Path $stagingRoot "external-$tag"
$loaderPublish = Join-Path $stagingRoot "loader-$tag"
$versionsDirectory = Join-Path $externalRoot 'releases\Vectra.External\native-imgui\versions'
$releaseDirectory = Join-Path $versionsDirectory $tag
$temporaryOutputs = @($externalTests, $loaderTests, $externalArtifacts, $loaderArtifacts, $externalPublish, $loaderPublish)

if (Test-Path -LiteralPath $releaseDirectory) { throw "Release '$tag' already exists. Bump Version.props before publishing again." }
foreach ($path in $temporaryOutputs) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force } }
New-Item -ItemType Directory -Path $versionsDirectory -Force | Out-Null

& (Join-Path $PSScriptRoot 'Build-NativeMenu.ps1') -Configuration Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Native vectraNewUi menu build failed; release was not created.' }

dotnet run --project (Join-Path $externalRoot 'Vectra.External.Tests\Vectra.External.Tests.csproj') -c Release --artifacts-path $externalTests
if ($LASTEXITCODE -ne 0) { throw 'External client tests failed; release was not created.' }
dotnet run --project (Join-Path $externalRoot 'Vectra.Loader.Tests\Vectra.Loader.Tests.csproj') -c Release --artifacts-path $loaderTests
if ($LASTEXITCODE -ne 0) { throw 'Loader tests failed; release was not created.' }

dotnet publish (Join-Path $externalRoot 'Vectra.External\Vectra.External.csproj') -c Release -r win-x64 --self-contained false --nologo --artifacts-path $externalArtifacts -o $externalPublish
if ($LASTEXITCODE -ne 0) { throw 'External client publish failed.' }
dotnet publish (Join-Path $externalRoot 'Vectra.Loader\Vectra.Loader.csproj') -c Release -r win-x64 --self-contained false --nologo --artifacts-path $loaderArtifacts -o $loaderPublish
if ($LASTEXITCODE -ne 0) { throw 'Loader publish failed.' }

$publishedInfoPath = Join-Path $externalPublish 'Offsets\info.json'
if (-not (Test-Path -LiteralPath $publishedInfoPath)) { throw 'Published package does not contain Offsets/info.json.' }
$publishedInfo = Get-Content -Raw -LiteralPath $publishedInfoPath | ConvertFrom-Json
if ([int]$publishedInfo.build_number -ne [int]$offsetInfo.build_number -or [string]$publishedInfo.timestamp -ne [string]$offsetInfo.timestamp) {
    throw 'Published offset metadata does not match the source dump.'
}
$publishedExternal = Join-Path $externalPublish 'Vectra.External.exe'
$publishedNativeMenu = Join-Path $externalPublish 'Vectra.Menu.Native.dll'
$publishedLoader = Join-Path $loaderPublish 'Vectra.Loader.exe'
if (-not (Test-Path -LiteralPath $publishedExternal)) { throw 'Published External executable is missing.' }
if (-not (Test-Path -LiteralPath $publishedNativeMenu)) { throw 'Published native menu DLL is missing.' }
if (-not (Test-Path -LiteralPath $publishedLoader)) { throw 'Published Loader executable is missing.' }

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $externalPublish '*') -Destination $releaseDirectory -Recurse -Force
Copy-Item -Path (Join-Path $loaderPublish '*') -Destination $releaseDirectory -Recurse -Force

$externalExe = Join-Path $releaseDirectory "Vectra.External-$tag.exe"
$loaderExe = Join-Path $releaseDirectory "Vectra.Loader-$tag.exe"
Rename-Item -LiteralPath (Join-Path $releaseDirectory 'Vectra.External.exe') -NewName (Split-Path -Leaf $externalExe)
Rename-Item -LiteralPath (Join-Path $releaseDirectory 'Vectra.Loader.exe') -NewName (Split-Path -Leaf $loaderExe)
$externalHash = (Get-FileHash -LiteralPath $externalExe -Algorithm SHA256).Hash
$nativeMenu = Join-Path $releaseDirectory 'Vectra.Menu.Native.dll'
$nativeMenuHash = (Get-FileHash -LiteralPath $nativeMenu -Algorithm SHA256).Hash
$loaderHash = (Get-FileHash -LiteralPath $loaderExe -Algorithm SHA256).Hash

$manifest = [ordered]@{
    product = 'Vectra External'
    version = $version
    cs2_build = [int]$offsetInfo.build_number
    tag = $tag
    created_utc = (Get-Date).ToUniversalTime().ToString('o')
    offset_dump_timestamp = [string]$offsetInfo.timestamp
    offset_source_sha256 = [ordered]@{
        offsets_cs = (Get-FileHash -LiteralPath (Join-Path $offsetDirectory 'offsets.cs') -Algorithm SHA256).Hash
        client_dll_cs = (Get-FileHash -LiteralPath (Join-Path $offsetDirectory 'client_dll.cs') -Algorithm SHA256).Hash
        animationsystem_dll_cs = (Get-FileHash -LiteralPath (Join-Path $offsetDirectory 'animationsystem_dll.cs') -Algorithm SHA256).Hash
        info_json = (Get-FileHash -LiteralPath $offsetInfoPath -Algorithm SHA256).Hash
    }
    executable = (Split-Path -Leaf $externalExe)
    executable_sha256 = $externalHash
    native_menu = (Split-Path -Leaf $nativeMenu)
    native_menu_sha256 = $nativeMenuHash
    loader_executable = (Split-Path -Leaf $loaderExe)
    loader_sha256 = $loaderHash
} | ConvertTo-Json -Depth 4
$manifest | Set-Content -LiteralPath (Join-Path $releaseDirectory 'release.json') -Encoding utf8
"$tag`nnative-imgui\versions\$tag" | Set-Content -LiteralPath (Join-Path $externalRoot 'releases\Vectra.External\latest.txt') -Encoding utf8

foreach ($path in $temporaryOutputs) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force } }
Write-Host "Published $tag to $releaseDirectory"
