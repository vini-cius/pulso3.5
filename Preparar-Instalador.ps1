param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$version = (Get-Content (Join-Path $PSScriptRoot 'versao.txt') -Raw).Trim()
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Compilar.ps1') }
$candidates = @(
    $env:INNO_SETUP_COMPILER,
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) }
$compiler = $candidates | Select-Object -First 1
$temporaryToolDirectory = $null
$temporarySetup = $null
$stdout = Join-Path $env:TEMP 'Pulso35-installer-out.txt'
$stderr = Join-Path $env:TEMP 'Pulso35-installer-err.txt'
try {
    if (-not $compiler) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $id = [guid]::NewGuid().ToString('N')
        $temporaryToolDirectory = Join-Path $env:TEMP ("Pulso35-InnoSetup-$id")
        $temporarySetup = Join-Path $env:TEMP ("innosetup-7.1.0-x64-$id.exe")
        $url = 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe'
        Write-Output 'Inno Setup não encontrado. Baixando o compilador oficial para uma pasta temporária…'
        Invoke-WebRequest -Uri $url -OutFile $temporarySetup
        $signature = Get-AuthenticodeSignature -FilePath $temporarySetup
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') {
            throw 'A assinatura digital do Inno Setup não foi validada.'
        }
        $setupProcess = Start-Process -FilePath $temporarySetup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', ("/DIR=$temporaryToolDirectory")) -Wait -PassThru
        if ($setupProcess.ExitCode -ne 0) { throw "A instalação temporária do Inno Setup falhou (código $($setupProcess.ExitCode))." }
        $compiler = Join-Path $temporaryToolDirectory 'ISCC.exe'
        if (-not (Test-Path $compiler)) { throw 'O compilador ISCC.exe não foi encontrado após preparar o Inno Setup.' }
    }
    $process = Start-Process -FilePath $compiler -ArgumentList @("/DAppVersion=$version", 'Controlador35.iss') -WorkingDirectory $PSScriptRoot -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (Test-Path $stdout) { Get-Content $stdout }
    if (Test-Path $stderr) { Get-Content $stderr }
    if ($process.ExitCode -ne 0) { throw "A criação do instalador falhou (código $($process.ExitCode))." }
} finally {
    Remove-Item $stdout, $stderr -Force -ErrorAction SilentlyContinue
    if ($temporarySetup) { Remove-Item $temporarySetup -Force -ErrorAction SilentlyContinue }
    if ($temporaryToolDirectory) { Remove-Item $temporaryToolDirectory -Recurse -Force -ErrorAction SilentlyContinue }
}
Write-Output "Instalador criado em dist\Pulso35-Setup-$version.exe"
