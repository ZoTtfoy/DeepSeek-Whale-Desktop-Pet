param([string]$Version='1.0.0')
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+([-.][A-Za-z0-9.]+)?$'){throw 'Invalid version'}
$appDir=Join-Path $PSScriptRoot 'app'
$dist=Join-Path $PSScriptRoot 'dist'
$stage=Join-Path $dist "DeepSeekWhale-v$Version-Windows"
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if(-not (Test-Path "$fx\csc.exe")){$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'}
$refs=@("/reference:$fx\WPF\PresentationFramework.dll","/reference:$fx\WPF\PresentationCore.dll","/reference:$fx\WPF\WindowsBase.dll",'/reference:System.Xaml.dll','/reference:System.Net.Http.dll','/reference:System.Web.Extensions.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Security.dll')
$sources=@(Get-ChildItem -LiteralPath $appDir -Filter '*.cs' | ForEach-Object FullName)
$art=(Get-ChildItem -LiteralPath $appDir -Filter '*.png').FullName
$icon=(Get-ChildItem -LiteralPath $appDir -Filter '*.ico').FullName
$manifest=(Get-ChildItem -LiteralPath $appDir -Filter '*.manifest').FullName
& "$fx\csc.exe" /nologo /target:winexe /optimize+ "/win32icon:$icon" "/win32manifest:$manifest" "/resource:$art,WhaleMascot" "/out:$stage\DeepSeekWhale.exe" @refs @sources
if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
Get-ChildItem -LiteralPath $appDir -Filter '*.exe.config' | Copy-Item -Destination "$stage\DeepSeekWhale.exe.config" -Force
New-Item -ItemType Directory -Path "$stage\poses" -Force | Out-Null
Get-ChildItem -LiteralPath "$appDir\poses" -File | Copy-Item -Destination "$stage\poses" -Force
Get-ChildItem -LiteralPath "$PSScriptRoot\docs" -Filter '*.md' | Copy-Item -Destination $stage -Force
Copy-Item -LiteralPath "$PSScriptRoot\RELEASE_NOTES.md" -Destination "$stage\README.md" -Force
$archive=Join-Path $dist "DeepSeekWhale-v$Version-Windows.zip"
Compress-Archive -LiteralPath $stage -DestinationPath $archive -CompressionLevel Optimal -Force
$hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'),$hash+'  '+[IO.Path]::GetFileName($archive)+[Environment]::NewLine,[Text.Encoding]::ASCII)
Write-Host "Built: $archive"
