param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,
    [Parameter(Mandatory = $true)]
    [string]$MetadataPath
)

$ErrorActionPreference = 'Stop'

$metadataFile = Get-Item -LiteralPath $MetadataPath -ErrorAction Stop
$metadata = Get-Content -LiteralPath $metadataFile.FullName -Raw | ConvertFrom-Json
foreach ($name in @('Endpoint', 'CodeSigningAccountName', 'CertificateProfileName')) {
    if ([string]::IsNullOrWhiteSpace([string]$metadata.$name)) {
        throw "O arquivo de metadados não contém o campo obrigatório '$name'."
    }
}
$endpoint = $null
if (-not [Uri]::TryCreate([string]$metadata.Endpoint, [UriKind]::Absolute, [ref]$endpoint) -or $endpoint.Scheme -ne 'https') {
    throw 'Endpoint no arquivo de metadados precisa ser uma URL HTTPS absoluta.'
}
if ($metadata.ExcludeCredentials -and @($metadata.ExcludeCredentials) -contains 'AzureCliCredential') {
    throw 'O arquivo de metadados exclui AzureCliCredential. Remova-o de ExcludeCredentials se for autenticar com az login.'
}

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$signTool = Get-ChildItem -LiteralPath $sdkRoot -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -match '\\x64$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $signTool) {
    throw 'SignTool x64 não foi encontrado. Instale o Windows 10/11 SDK ou o pacote Artifact Signing Client Tools.'
}

$clientRoots = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\ArtifactSigningClientTools'),
    (Join-Path $env:ProgramFiles 'Microsoft\ArtifactSigningClientTools')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$dlib = $null
foreach ($root in $clientRoots) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    $dlib = Get-ChildItem -LiteralPath $root -Filter Azure.CodeSigning.Dlib.dll -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\Azure\.CodeSigning\.Dlib\.dll$' } |
        Select-Object -First 1
    if ($dlib) { break }
}
if (-not $dlib) {
    throw 'Azure.CodeSigning.Dlib.dll x64 não foi encontrado. Reinstale o Microsoft Azure Artifact Signing Client Tools.'
}

foreach ($inputPath in $Path) {
    $file = Get-Item -LiteralPath $inputPath -ErrorAction Stop
    if ($file.PSIsContainer) { throw "Informe arquivos, não pastas: $($file.FullName)" }

    Write-Output "Assinando $($file.FullName) com Azure Artifact Signing…"
    $signArguments = @(
        'sign', '/v', '/fd', 'SHA256',
        '/tr', 'http://timestamp.acs.microsoft.com', '/td', 'SHA256',
        '/dlib', $dlib.FullName,
        '/dmdf', $metadataFile.FullName,
        $file.FullName
    )
    & $signTool.FullName @signArguments
    if ($LASTEXITCODE -ne 0) {
        throw "O Azure Artifact Signing falhou para '$($file.FullName)' (código $LASTEXITCODE). Confira az login, a região do endpoint e a função Certificate Profile Signer."
    }

    & $signTool.FullName verify /pa /v $file.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "A verificação Authenticode falhou para '$($file.FullName)' (código $LASTEXITCODE)."
    }
}
