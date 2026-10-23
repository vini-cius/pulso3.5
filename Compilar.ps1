param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $PSScriptRoot 'Controlador35.cs'
$panel = Join-Path $PSScriptRoot 'Painel35.cs'
$designer = Join-Path $PSScriptRoot 'ThemeDesigner.cs'
$gamer = Join-Path $PSScriptRoot 'GamerMode.cs'
$updater = Join-Path $PSScriptRoot 'UpdateManager.cs'
$versionFile = Join-Path $PSScriptRoot 'versao.txt'
$windowsMetadata = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata') -Filter Windows.winmd -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -notmatch '\\Facade$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
$winrtProjection = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll'
$outputDir = Join-Path $PSScriptRoot 'dist\app'
if (-not (Test-Path $compiler)) { throw "Compilador .NET Framework não encontrado: $compiler" }
if (-not $windowsMetadata) { throw 'Windows.winmd não encontrado. Instale o Windows 10/11 SDK.' }
if (-not (Test-Path $winrtProjection)) { throw "Projeção WinRT do .NET Framework não encontrada: $winrtProjection" }
if (-not (Test-Path $versionFile)) { throw "Arquivo de versão não encontrado: $versionFile" }
$version = (Get-Content $versionFile -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Versão inválida em versao.txt: $version" }
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    Copy-Item $versionFile (Join-Path $outputDir 'versao.txt') -Force
    Remove-Item (Join-Path $outputDir 'atualizador.json') -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $outputDir 'Pulso35-Diagnostico.exe') -Force -ErrorAction SilentlyContinue
    $output = Join-Path $outputDir 'Pulso35.exe'
} else {
    $output = [System.IO.Path]::GetFullPath($OutputPath)
    $outputDir = Split-Path -Parent $output
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}
$compilerArgs = @('/nologo', '/optimize+', '/target:winexe', '/platform:x64', ('/out:"' + $output + '"'), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Runtime.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Management.dll', '/reference:System.Web.Extensions.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll', ('/reference:"' + $winrtProjection + '"'), ('/reference:"' + $windowsMetadata.FullName + '"'), ('"' + $updater + '"'), ('"' + $panel + '"'), ('"' + $designer + '"'), ('"' + $gamer + '"'), ('"' + $source + '"'))
$stdout = Join-Path $env:TEMP 'Pulso35-build-out.txt'
$stderr = Join-Path $env:TEMP 'Pulso35-build-err.txt'
$process = Start-Process -FilePath $compiler -ArgumentList $compilerArgs -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
Get-Content $stdout
Get-Content $stderr
if ($process.ExitCode -ne 0) { throw 'Build failed: Pulso35.exe' }
Write-Output "Build OK — versão $version — arquivo $output"
