param(
    [string]$Version = "1.0.3"
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repo "artifacts"
$publish = Join-Path $artifacts "publish"
$releaseName = "Exark-RU-v$Version"
$release = Join-Path $artifacts $releaseName
$archive = Join-Path $artifacts "$releaseName.zip"
$checksums = Join-Path $artifacts "SHA256SUMS.txt"

python (Join-Path $PSScriptRoot "merge_translation.py")
python (Join-Path $PSScriptRoot "validate_translation.py")

dotnet build (Join-Path $repo "ExarkRu.slnx") -c Release
dotnet publish (Join-Path $repo "src\ExarkRu.Patcher\ExarkRu.Patcher.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publish

if (Test-Path $release) {
    Remove-Item $release -Recurse -Force
}
New-Item -ItemType Directory -Path $release | Out-Null

Copy-Item (Join-Path $publish "ExarkRu.Patcher.exe") $release
Copy-Item (Join-Path $repo "src\ExarkRu.Runtime\bin\Release\netstandard2.1\ExarkRu.Runtime.dll") $release
Copy-Item (Join-Path $repo "localization\translations.ru.json") $release
Copy-Item (Join-Path $repo "README.md") $release
Copy-Item (Join-Path $repo "exark-ru.png") $release

if (Test-Path $archive) {
    Remove-Item $archive -Force
}
Compress-Archive -Path (Join-Path $release "*") -DestinationPath $archive -CompressionLevel Optimal

if (Test-Path $checksums) {
    Remove-Item $checksums -Force
}

$archiveHash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  $([System.IO.Path]::GetFileName($archive))" |
    Set-Content -Path $checksums -Encoding ascii

Write-Host "Release: $archive"
Write-Host "Checksums: $checksums"
