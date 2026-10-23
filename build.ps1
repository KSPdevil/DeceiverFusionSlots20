# DeceiverFusionSlots20 - build + package script
# Double click "BUILD.bat" instead of running this file directly.
# It compiles Main.cs into DeceiverFusionSlots20.dll and packs an installable zip.

param(
    [switch]$Debug,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

function Say($msg, $color = 'Cyan') { Write-Host "[DFS20] $msg" -ForegroundColor $color }

$modDir = $PSScriptRoot
if (-not $modDir) { $modDir = Split-Path -Parent $MyInvocation.MyCommand.Path }

$gameDir  = 'F:\yongwangzheZWYZTK V2.1.1\Pathfinder Wrath of the Righteous'
$managed  = Join-Path $gameDir 'Wrath_Data\Managed'
$src      = Join-Path $modDir 'Main.cs'
$dll      = Join-Path $modDir 'DeceiverFusionSlots20.dll'
$info     = Join-Path $modDir 'Info.json'
$zipOut   = Join-Path $modDir 'DeceiverFusionSlots20-UMM.zip'

Say 'DeceiverFusionSlots20 build script'
Write-Host ''

# ---------------------------------------------------------------- checks
if (-not (Test-Path $managed)) { Say "Managed not found: $managed" 'Red'; if (-not $NoPause) { Read-Host 'Press Enter' }; exit 1 }
if (-not (Test-Path $src))     { Say "Source not found: $src" 'Red';      if (-not $NoPause) { Read-Host 'Press Enter' }; exit 1 }
if (-not (Test-Path $info))    { Say "Info.json not found: $info" 'Red';  if (-not $NoPause) { Read-Host 'Press Enter' }; exit 1 }

# ---------------------------------------------------------------- locate compiler
Say 'Locating a C# compiler...'
$compiler = $null   # @{ Exe ; Prefix }  ->  & Exe @Prefix @rsp

# 1) 项目自带的 Roslyn（仓库同级 .tools 目录，离线可用）
$roslynLocal = Join-Path (Split-Path -Parent $modDir) '.tools\roslyn\bincore\csc.dll'
$dotnet      = Get-Command dotnet -ErrorAction SilentlyContinue

if ((Test-Path $roslynLocal) -and $dotnet) {
    $compiler = @{ Exe = $dotnet.Source; Prefix = @($roslynLocal) }
    Say "Using Roslyn (bundled): $roslynLocal"
}

if (-not $compiler) {
    $fw = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($fw) {
        $compiler = @{ Exe = $fw; Prefix = @() }
        Say "Using .NET Framework compiler: $fw"
    }
}

if (-not $compiler -and $dotnet) {
    Say 'No local compiler found, downloading Roslyn 4.8 from nuget.org...'
    try {
        $tmpRoot = Join-Path $env:TEMP 'dfs20-roslyn'
        New-Item -ItemType Directory -Force -Path $tmpRoot | Out-Null
        $pkg = Join-Path $tmpRoot 'roslyn.zip'
        if (-not (Test-Path $pkg)) {
            Invoke-WebRequest -Uri 'https://www.nuget.org/api/v2/package/Microsoft.Net.Compilers.Toolset/4.8.0' -OutFile $pkg -UseBasicParsing
        }
        Expand-Archive -Path $pkg -DestinationPath $tmpRoot -Force
        $found = Get-ChildItem -Path $tmpRoot -Recurse -Filter 'csc.dll' |
                 Where-Object { $_.FullName -like '*bincore*' } |
                 Select-Object -First 1
        if ($found) {
            $compiler = @{ Exe = $dotnet.Source; Prefix = @($found.FullName) }
            Say "Using Roslyn (downloaded): $($found.FullName)"
        }
    } catch {
        Say "Download failed: $($_.Exception.Message)" 'Yellow'
    }
}

if (-not $compiler) {
    Say 'ERROR: no C# compiler available.' 'Red'
    Say 'Install .NET SDK (https://dotnet.microsoft.com/download) or run this on a PC with Visual Studio.' 'Red'
    if (-not $NoPause) { Read-Host 'Press Enter' }
    exit 1
}

# ---------------------------------------------------------------- build response file
$exclude = @('netstandard.dll', 'UnityEngine.dll')
$refs = Get-ChildItem -Path $managed -Filter '*.dll' |
        Where-Object { $exclude -notcontains $_.Name -and $_.Name -notlike 'System.Private.*' }

$rsp = Join-Path $env:TEMP 'dfs20-build.rsp'
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('/noconfig')
$lines.Add('/nostdlib+')
$lines.Add('/target:library')
$lines.Add('/langversion:latest')
$lines.Add('/optimize-')
$lines.Add('/warn:0')
if ($Debug) { $lines.Add('/define:DFS20_DEBUG') }
$lines.Add("/out:`"$dll`"")
foreach ($r in $refs) { $lines.Add("/r:`"$($r.FullName)`"") }
$lines.Add("`"$src`"")
[System.IO.File]::WriteAllLines($rsp, $lines, [System.Text.Encoding]::ASCII)

Say "References: $($refs.Count) assemblies from Managed"
if ($Debug) { Say 'DEBUG build: runtime logging enabled' 'Yellow' }

# ---------------------------------------------------------------- compile
if (Test-Path $dll) { Remove-Item $dll -Force }
Say 'Compiling...'

if ($compiler.Prefix.Count -gt 0) { $env:DOTNET_ROLL_FORWARD = 'LatestMajor' }

$out = & $compiler.Exe @($compiler.Prefix) "@$rsp" 2>&1
$code = $LASTEXITCODE
$out | ForEach-Object { Write-Host $_ }

if ($code -ne 0 -or -not (Test-Path $dll)) {
    Say "COMPILE FAILED (exit $code). Errors above - copy them back to me." 'Red'
    if (-not $NoPause) { Read-Host 'Press Enter' }
    exit $code
}

$size = (Get-Item $dll).Length
Say "Compiled OK: $dll ($size bytes)" 'Green'

# ---------------------------------------------------------------- package zip
try {
    if (Test-Path $zipOut) { Remove-Item $zipOut -Force }
    Compress-Archive -Path @($dll, $info) -DestinationPath $zipOut -CompressionLevel Optimal
    Say "ZIP created: $zipOut" 'Green'
    Say 'Install it with Unity Mod Manager: Mods -> Install -> pick this zip.' 'Green'
} catch {
    Say "ZIP failed: $($_.Exception.Message)" 'Yellow'
}

# ---------------------------------------------------------------- optional direct install
$target = Join-Path $gameDir 'Mods\DeceiverFusionSlots20'
try {
    if (Test-Path (Split-Path -Parent (Join-Path $gameDir 'Mods'))) {
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        Copy-Item $dll  -Destination $target -Force
        Copy-Item $info -Destination $target -Force
        Say "Also copied to: $target" 'Green'
    }
} catch {
    Say "Direct copy skipped: $($_.Exception.Message)" 'Yellow'
}

Write-Host ''
Say 'ALL DONE.' 'Green'
if (-not $NoPause) { Read-Host 'Press Enter to close' }
