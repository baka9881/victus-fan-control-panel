$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitOperatingSystem) {
    throw 'This project requires 64-bit Windows.'
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler was not found.' }
$artifactDirectory = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$sources = @('AssemblyInfo.cs','QuietCore.cs','QuietHardware.cs','QuietRecovery.cs','QuietDashboard.cs','QuietApp.cs') | ForEach-Object { Join-Path $PSScriptRoot ('src\' + $_) }
$manifest = Join-Path $PSScriptRoot 'src\app.manifest'
$icon = Join-Path $PSScriptRoot 'assets\app.ico'
$executable = Join-Path $artifactDirectory 'VictusControlPanel.exe'
& $compiler /nologo /utf8output /codepage:65001 /target:winexe /platform:x64 /optimize+ /warnaserror+ /r:System.dll /r:System.Core.dll /r:System.Management.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/win32manifest:$manifest" "/win32icon:$icon" "/out:$executable" $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
Write-Output "Built: $executable"
