[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$externalRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $externalRoot 'ui\native-imgui\neverlose\examples\example_win32_directx9\example_win32_directx9.vcxproj'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Installer (vswhere.exe) was not found.' }
$installation = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'A Visual Studio installation with the C++ x64 toolchain was not found.' }
$msbuild = Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuild)) { throw 'Visual Studio MSBuild was not found.' }

& $msbuild $project /t:Build "/p:Configuration=$Configuration" /p:Platform=x64 /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'The native vectraNewUi menu build failed.' }

$output = Join-Path (Split-Path -Parent $project) "bin\$Configuration\Vectra.Menu.Native.dll"
if (-not (Test-Path -LiteralPath $output)) { throw 'The native menu DLL was not produced.' }
Write-Output $output
