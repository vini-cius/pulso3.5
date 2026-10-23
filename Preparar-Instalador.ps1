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
if (-not $candidates) {
    throw 'O aplicativo foi compilado em dist\app. Para gerar o instalador, instale Inno Setup 6 e execute este script novamente.'
}
$compiler = $candidates | Select-Object -First 1
Push-Location $PSScriptRoot
try { & $compiler "/DAppVersion=$version" 'Controlador35.iss' }
finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw 'A criação do instalador falhou.' }
Write-Output "Instalador criado em dist\Pulso35-Setup-$version.exe"
