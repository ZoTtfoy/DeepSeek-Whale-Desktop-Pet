$ErrorActionPreference='Stop'
$appDir=Join-Path $PSScriptRoot 'app'
$testDir=Join-Path $PSScriptRoot 'work'
New-Item -ItemType Directory -Path "$testDir\poses" -Force | Out-Null
Get-ChildItem -LiteralPath "$appDir\poses" -File | Copy-Item -Destination "$testDir\poses" -Force
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if(-not (Test-Path "$fx\csc.exe")){$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'}
$refs=@("/reference:$fx\WPF\PresentationFramework.dll","/reference:$fx\WPF\PresentationCore.dll","/reference:$fx\WPF\WindowsBase.dll",'/reference:System.Xaml.dll','/reference:System.Net.Http.dll','/reference:System.Web.Extensions.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Security.dll')
$sources=@(Get-ChildItem -LiteralPath $appDir -Filter '*.cs' | ForEach-Object FullName)
$art=(Get-ChildItem -LiteralPath $appDir -Filter '*.png').FullName
& "$fx\csc.exe" /nologo /target:exe /main:VerifyNaturalPet "/out:$testDir\VerifyNaturalPet.exe" "/resource:$art,WhaleMascot" @refs @sources "$PSScriptRoot\tests\VerifyNaturalPet.cs"
if($LASTEXITCODE -ne 0){throw 'Test compilation failed'}
Push-Location $PSScriptRoot
try {& "$testDir\VerifyNaturalPet.exe"; if($LASTEXITCODE -ne 0){throw 'Offline verification failed'}} finally {Pop-Location}
