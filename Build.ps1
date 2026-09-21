$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Security.dll /reference:System.Web.Extensions.dll ("/win32icon:" + (Join-Path $PSScriptRoot 'assets\switcher-knot-v1.ico')) ("/out:" + (Join-Path $PSScriptRoot 'ProfileSwitcherPublic.exe')) (Join-Path $PSScriptRoot 'src\Switcher.cs') (Join-Path $PSScriptRoot 'src\Interface.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output 'Built ProfileSwitcherPublic.exe'
