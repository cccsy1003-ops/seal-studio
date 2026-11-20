param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$MSBuildPath = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = (Get-ChildItem (Join-Path $root 'src') -Filter '*.csproj' | Select-Object -First 1).FullName

if ($MSBuildPath) {
    $msbuild = $MSBuildPath
} else {
    $candidates = @(
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
        "$env:ProgramFiles\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
    )
    $msbuild = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $msbuild) {
        $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path $vswhere) {
            $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
            if ($found) { $msbuild = $found }
        }
    }
    if (-not $msbuild) {
        $command = Get-Command msbuild -ErrorAction SilentlyContinue
        if ($command) { $msbuild = $command.Source }
    }
}

if (-not $msbuild) {
    throw 'MSBuild not found. Install Visual Studio 2022 or Visual Studio Build Tools with the .NET desktop development workload, or pass -MSBuildPath.'
}

Write-Host "Using MSBuild: $msbuild"
& $msbuild $project "/p:Configuration=$Configuration" /p:Platform=x64 /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path (Split-Path $project) ('bin\' + [IO.Path]::GetFileNameWithoutExtension($project) + '.exe')
Write-Host "Build succeeded: $exe"
