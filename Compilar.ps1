$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $PSScriptRoot 'Controlador35.cs'
$panel = Join-Path $PSScriptRoot 'Painel35.cs'
$designer = Join-Path $PSScriptRoot 'ThemeDesigner.cs'
$updater = Join-Path $PSScriptRoot 'UpdateManager.cs'
$versionFile = Join-Path $PSScriptRoot 'versao.txt'
$outputDir = Join-Path $PSScriptRoot 'dist\app'
if (-not (Test-Path $compiler)) { throw "Compilador .NET Framework não encontrado: $compiler" }
if (-not (Test-Path $versionFile)) { throw "Arquivo de versão não encontrado: $versionFile" }
$version = (Get-Content $versionFile -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Versão inválida em versao.txt: $version" }
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
Copy-Item $versionFile (Join-Path $outputDir 'versao.txt') -Force
Remove-Item (Join-Path $outputDir 'atualizador.json') -Force -ErrorAction SilentlyContinue
foreach ($target in @(@('Pulso35.exe', 'winexe'), @('Pulso35-Diagnostico.exe', 'exe'))) {
    $output = Join-Path $outputDir $target[0]
    $compilerArgs = @('/nologo', '/optimize+', ('/target:' + $target[1]), '/platform:x64', ('/out:"' + $output + '"'), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Management.dll', '/reference:System.Web.Extensions.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll', ('"' + $updater + '"'), ('"' + $panel + '"'), ('"' + $designer + '"'), ('"' + $source + '"'))
    $stdout = Join-Path $env:TEMP 'Pulso35-build-out.txt'
    $stderr = Join-Path $env:TEMP 'Pulso35-build-err.txt'
    $process = Start-Process -FilePath $compiler -ArgumentList $compilerArgs -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    Get-Content $stdout
    Get-Content $stderr
    if ($process.ExitCode -ne 0) { throw ('Build failed: ' + $target[0]) }
}
Write-Output "Build OK — versão $version — arquivos em $outputDir"
