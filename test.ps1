$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Native icon tests require Windows.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDirectory = Join-Path $PSScriptRoot 'artifacts\tests'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$coreTests = Join-Path $testDirectory 'Quiet.Tests.exe'
& $compiler /nologo /utf8output /codepage:65001 /warnaserror+ /target:exe "/out:$coreTests" (Join-Path $PSScriptRoot 'src\QuietCore.cs') (Join-Path $PSScriptRoot 'tests\QuietTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Control tests failed to compile.' }
& $coreTests
if ($LASTEXITCODE -ne 0) { throw 'Control tests failed.' }
$nativeTests = Join-Path $testDirectory 'NativeIconTests.exe'
& $compiler /nologo /utf8output /codepage:65001 /warnaserror+ /target:exe /r:System.Drawing.dll "/out:$nativeTests" (Join-Path $PSScriptRoot 'tests\NativeIconTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native icon tests failed to compile.' }
& $nativeTests (Join-Path $PSScriptRoot 'assets\app.ico') $testDirectory
if ($LASTEXITCODE -ne 0) { throw 'Native icon transparency tests failed.' }
Write-Output 'All control and icon tests passed. No fan commands were sent.'
