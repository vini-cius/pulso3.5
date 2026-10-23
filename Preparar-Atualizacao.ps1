param(
    [string]$Notas = 'Atualização do Pulso 3.5.',
    [string]$PackageUrl = '',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$version = (Get-Content (Join-Path $PSScriptRoot 'versao.txt') -Raw).Trim()
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Compilar.ps1') }
if (-not [string]::IsNullOrWhiteSpace($PackageUrl)) {
    $packageUri = $null
    if (-not [Uri]::TryCreate($PackageUrl, [UriKind]::Absolute, [ref]$packageUri) -or $packageUri.Scheme -ne [Uri]::UriSchemeHttps) {
        throw 'PackageUrl precisa ser um endereço HTTPS absoluto.'
    }
}

$appDir = Join-Path $PSScriptRoot 'dist\app'
$releaseDir = Join-Path $PSScriptRoot 'dist\atualizacao'
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
$package = Join-Path $releaseDir ("Pulso35-{0}.zip" -f $version)
if (Test-Path $package) { Remove-Item $package -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($package, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in @('Pulso35.exe', 'Pulso35-Diagnostico.exe', 'versao.txt', 'atualizador.json')) {
        $file = Join-Path $appDir $name
        if (-not (Test-Path $file)) { throw "Arquivo da versão ausente: $file" }
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file, $name, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }

$hash = (Get-FileHash -Path $package -Algorithm SHA256).Hash.ToLowerInvariant()
$finalPackageUrl = [System.IO.Path]::GetFileName($package)
if (-not [string]::IsNullOrWhiteSpace($PackageUrl)) { $finalPackageUrl = $PackageUrl.Trim() }
$manifest = [ordered]@{
    version = $version
    packageUrl = $finalPackageUrl
    sha256 = $hash
    notes = $Notas
}
$json = ConvertTo-Json -InputObject $manifest -Depth 4
[System.IO.File]::WriteAllText((Join-Path $releaseDir 'latest.json'), $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Output "Pacote e manifesto prontos em $releaseDir. Publique os dois arquivos juntos em um endereço HTTPS."
