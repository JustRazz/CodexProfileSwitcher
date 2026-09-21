$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'ProfileSwitcherPublic.exe'))) { & (Join-Path $PSScriptRoot 'Build.ps1') }
$run = Join-Path ([IO.Path]::GetTempPath()) ('psp-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $run | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ProfileSwitcherPublic.exe') -Destination (Join-Path $run 'ProfileSwitcherPublic.exe')
& $compiler /nologo /target:exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ("/reference:" + (Join-Path $run 'ProfileSwitcherPublic.exe')) ("/out:" + (Join-Path $run 'PublicTests.exe')) (Join-Path $PSScriptRoot 'tests\PublicTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& (Join-Path $run 'PublicTests.exe') (Join-Path $run 'data')
if ($LASTEXITCODE -ne 0) { throw ('Tests failed. See ' + (Join-Path $run 'data\result.txt')) }
Write-Output ('Isolated test results: ' + (Join-Path $run 'data\result.txt'))
