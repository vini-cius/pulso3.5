param(
    [switch]$SkipBuild,
    [switch]$SkipSigning,
    [string]$PublisherName = $env:MSIX_PUBLISHER,
    [string]$ReleaseTag = ''
)

$ErrorActionPreference = 'Stop'
$version = (Get-Content (Join-Path $PSScriptRoot 'versao.txt') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Versão inválida em versao.txt: $version" }
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Compilar.ps1') }

if ([string]::IsNullOrWhiteSpace($PublisherName)) { $PublisherName = 'CN=Pulso 3.5 Dev' }
$tool = Get-Command 'MsixPackagingTool.exe' -ErrorAction SilentlyContinue
if (-not $tool) {
    throw 'MSIX Packaging Tool não encontrado. Instale-o pela Microsoft Store e execute este script em um terminal elevado. O link oficial é https://www.microsoft.com/store/productId/9N5LW3JBCXKF.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'A captura do MSIX Packaging Tool exige PowerShell executado como administrador. Feche este terminal e abra-o com Executar como administrador.'
}

$appDir = Join-Path $PSScriptRoot 'dist\app'
foreach ($required in @('Pulso35.exe', 'versao.txt')) {
    if (-not (Test-Path (Join-Path $appDir $required))) { throw "Arquivo compilado ausente: $required" }
}

$msixVersion = if ($version -match '^\d+\.\d+\.\d+$') { "$version.0" } else { $version }
$versionParts = $msixVersion.Split('.') | ForEach-Object { [int]$_ }
if ($versionParts.Count -ne 4 -or ($versionParts | Where-Object { $_ -gt 65535 }).Count -gt 0) {
    throw "A versão do pacote MSIX precisa ter quatro partes de 0 a 65535: $msixVersion"
}
if ([string]::IsNullOrWhiteSpace($ReleaseTag)) { $ReleaseTag = "v$version" }
if ($ReleaseTag -notmatch '^v\d+\.\d+\.\d+(\.\d+)?$') { throw "Tag de release inválida: $ReleaseTag" }

$dist = Join-Path $PSScriptRoot 'dist'
$work = Join-Path $dist 'msix-work'
$payload = Join-Path $work 'payload'
$installScript = Join-Path $work 'Instalar-Pulso35.cmd'
$templatePath = Join-Path $work 'ConversionTemplate.xml'
$generatedTemplatePath = Join-Path $dist "Pulso35-MSIX-$version.template.xml"
$msixName = "Pulso35-MSIX-$version.msix"
$msixPath = Join-Path $dist $msixName
$appInstallerPath = Join-Path $dist 'Pulso35.appinstaller'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Item (Join-Path $appDir 'Pulso35.exe') (Join-Path $payload 'Pulso35.exe') -Force
Copy-Item (Join-Path $appDir 'versao.txt') (Join-Path $payload 'versao.txt') -Force

$bootstrap = @'
@echo off
setlocal
set "SOURCE=%~dp0payload"
set "DEST=%ProgramFiles%\Pulso35"
if not exist "%DEST%" mkdir "%DEST%" || exit /b 1
copy /y "%SOURCE%\Pulso35.exe" "%DEST%\Pulso35.exe" >nul || exit /b 1
copy /y "%SOURCE%\versao.txt" "%DEST%\versao.txt" >nul || exit /b 1
exit /b 0
'@
[System.IO.File]::WriteAllText($installScript, $bootstrap, [System.Text.Encoding]::ASCII)

$templateNamespace = 'http://schemas.microsoft.com/appx/msixpackagingtool/template/2018'
$template = New-Object System.Xml.XmlDocument
$root = $template.CreateElement('MsixPackagingToolTemplate', $templateNamespace)
$template.AppendChild($root) | Out-Null

$settings = $template.CreateElement('Settings', $templateNamespace)
$settings.SetAttribute('AllowTelemetry', 'false')
$settings.SetAttribute('AllowPromptForPassword', 'false')
$settings.SetAttribute('EnforceMicrosoftStoreVersioningRequirements', 'false')
$root.AppendChild($settings) | Out-Null

$save = $template.CreateElement('SaveLocation', $templateNamespace)
$save.SetAttribute('PackagePath', $msixPath)
$save.SetAttribute('TemplatePath', $generatedTemplatePath)
$root.AppendChild($save) | Out-Null

$installer = $template.CreateElement('Installer', $templateNamespace)
$installer.SetAttribute('Path', $installScript)
$installer.SetAttribute('InstallLocation', (Join-Path $env:ProgramFiles 'Pulso35'))
$root.AppendChild($installer) | Out-Null

$packageInfo = $template.CreateElement('PackageInformation', $templateNamespace)
$packageInfo.SetAttribute('PackageName', 'Pulso35')
$packageInfo.SetAttribute('PackageDisplayName', 'Pulso 3.5')
$packageInfo.SetAttribute('PublisherName', $PublisherName)
$packageInfo.SetAttribute('PublisherDisplayName', 'Pulso 3.5')
$packageInfo.SetAttribute('Version', $msixVersion)
$applications = $template.CreateElement('Applications', $templateNamespace)
$application = $template.CreateElement('Application', $templateNamespace)
$application.SetAttribute('Id', 'Pulso35')
$application.SetAttribute('DisplayName', 'Pulso 3.5')
$application.SetAttribute('Description', 'Painel de computador para telas USB seriais.')
$application.SetAttribute('ExecutableName', 'Pulso35.exe')
$applications.AppendChild($application) | Out-Null
$packageInfo.AppendChild($applications) | Out-Null
$root.AppendChild($packageInfo) | Out-Null
$template.Save($templatePath)

if (Test-Path $msixPath) { Remove-Item $msixPath -Force }
Write-Output 'Criando o pacote com o Microsoft MSIX Packaging Tool…'
$quotedTemplate = '"' + $templatePath + '"'
$process = Start-Process -FilePath $tool.Source -ArgumentList @('create-package', '--template', $quotedTemplate) -WorkingDirectory $PSScriptRoot -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0 -or -not (Test-Path $msixPath)) {
    throw "O MSIX Packaging Tool não criou o pacote esperado (código $($process.ExitCode)). Execute em um terminal elevado e confira o driver de captura da ferramenta."
}

$sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$makeAppx = Get-ChildItem $sdkBin -Filter makeappx.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -match '\\x64$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $makeAppx) { throw 'MakeAppx.exe não foi encontrado no Windows 10/11 SDK.' }
$unpacked = Join-Path $work 'unpacked'
$packedMsix = Join-Path $work "Pulso35-MSIX-$version-Packed.msix"
if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
if (Test-Path $packedMsix) { Remove-Item $packedMsix -Force }
$unpackArguments = @('unpack', '/p', ('"' + $msixPath + '"'), '/d', ('"' + $unpacked + '"'), '/o')
$process = Start-Process -FilePath $makeAppx.FullName -ArgumentList $unpackArguments -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0) { throw "Não foi possível preparar o manifesto MSIX (código $($process.ExitCode))." }
$manifestPath = Join-Path $unpacked 'AppxManifest.xml'
if (-not (Test-Path $manifestPath)) { throw 'O pacote criado pelo MSIX Packaging Tool não contém AppxManifest.xml.' }
$manifest = New-Object System.Xml.XmlDocument
$manifest.PreserveWhitespace = $true
$manifest.Load($manifestPath)
$manifestRoot = $manifest.DocumentElement
$desktopNamespace = 'http://schemas.microsoft.com/appx/manifest/desktop/windows10'
$xmlnsNamespace = 'http://www.w3.org/2000/xmlns/'
if (-not $manifestRoot.GetAttribute('desktop', $xmlnsNamespace)) {
    $namespaceAttribute = $manifest.CreateAttribute('xmlns', 'desktop', $xmlnsNamespace)
    $namespaceAttribute.Value = $desktopNamespace
    $manifestRoot.Attributes.Append($namespaceAttribute) | Out-Null
}
$ignoredNamespaces = @($manifestRoot.GetAttribute('IgnorableNamespaces') -split '\s+' | Where-Object { $_ })
if ($ignoredNamespaces -notcontains 'desktop') {
    $ignoredNamespaces += 'desktop'
    $manifestRoot.SetAttribute('IgnorableNamespaces', ($ignoredNamespaces -join ' '))
}
$manifestNamespaces = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$manifestNamespaces.AddNamespace('f', $manifestRoot.NamespaceURI)
$manifestNamespaces.AddNamespace('desktop', $desktopNamespace)
$applicationNode = $manifest.SelectSingleNode('/f:Package/f:Applications/f:Application', $manifestNamespaces)
if (-not $applicationNode) { throw 'O manifesto MSIX não contém uma aplicação principal.' }
$extensionsNode = $applicationNode.SelectSingleNode('f:Extensions', $manifestNamespaces)
if (-not $extensionsNode) {
    $extensionsNode = $manifest.CreateElement('', 'Extensions', $manifestRoot.NamespaceURI)
    $applicationNode.AppendChild($extensionsNode) | Out-Null
}
$startupExtension = $manifest.CreateElement('desktop', 'Extension', $desktopNamespace)
$startupExtension.SetAttribute('Category', 'windows.startupTask')
$startupExtension.SetAttribute('Executable', 'Pulso35.exe')
$startupExtension.SetAttribute('EntryPoint', 'Windows.FullTrustApplication')
$startupTask = $manifest.CreateElement('desktop', 'StartupTask', $desktopNamespace)
$startupTask.SetAttribute('TaskId', 'Pulso35Startup')
$startupTask.SetAttribute('Enabled', 'false')
$startupTask.SetAttribute('DisplayName', 'Pulso 3.5')
$startupExtension.AppendChild($startupTask) | Out-Null
$extensionsNode.AppendChild($startupExtension) | Out-Null
$manifest.Save($manifestPath)
$packArguments = @('pack', '/d', ('"' + $unpacked + '"'), '/p', ('"' + $packedMsix + '"'), '/o')
$process = Start-Process -FilePath $makeAppx.FullName -ArgumentList $packArguments -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0 -or -not (Test-Path $packedMsix)) { throw "Não foi possível recriar o pacote MSIX com a tarefa de inicialização (código $($process.ExitCode))." }
Move-Item $packedMsix $msixPath -Force

if (-not $SkipSigning) {
    $certificates = @(
        Get-ChildItem 'Cert:\CurrentUser\My' -ErrorAction SilentlyContinue
        Get-ChildItem 'Cert:\LocalMachine\My' -ErrorAction SilentlyContinue
    )
    $certificate = $certificates |
        Where-Object { $_.Subject -eq $PublisherName -and $_.HasPrivateKey -and $_.NotBefore -le (Get-Date) -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if (-not $certificate) { throw "Não encontrei um certificado válido com chave privada cujo Subject seja exatamente '$PublisherName'. Configure MSIX_PUBLISHER e instale o certificado correspondente." }

    $signTool = Get-ChildItem $sdkBin -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -match '\\x64$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $signTool) { throw 'SignTool.exe não foi encontrado no Windows 10/11 SDK.' }

    $signArguments = @('sign', '/fd', 'SHA256', '/sha1', $certificate.Thumbprint, '/s', 'My')
    if ($certificate.PSPath -like '*LocalMachine*') { $signArguments += '/sm' }
    if ($PublisherName -ne 'CN=Pulso 3.5 Dev') {
        $signArguments += @('/tr', 'https://timestamp.digicert.com', '/td', 'SHA256')
    }
    $signArguments += $msixPath
    & $signTool.FullName @signArguments
    if ($LASTEXITCODE -ne 0) { throw "A assinatura do pacote MSIX falhou (código $LASTEXITCODE)." }

    if ($PublisherName -eq 'CN=Pulso 3.5 Dev') {
        Write-Warning 'Pacote assinado com o certificado de desenvolvimento. Outros computadores precisam confiar nesse certificado; ele não é adequado para distribuição pública.'
    } else {
        & $signTool.FullName verify /pa /v $msixPath
        if ($LASTEXITCODE -ne 0) { throw 'A assinatura do MSIX não passou pela verificação de confiança do Windows.' }
    }
} else {
    Write-Warning 'O pacote ficou sem assinatura e não pode ser instalado até receber uma assinatura confiável.'
}

$packageUri = "https://github.com/vini-cius/pulso3.5/releases/download/$ReleaseTag/$msixName"
$appInstallerUri = 'https://github.com/vini-cius/pulso3.5/releases/latest/download/Pulso35.appinstaller'
$appInstallerXml = New-Object System.Xml.XmlDocument
$appInstallerRoot = $appInstallerXml.CreateElement('AppInstaller', 'http://schemas.microsoft.com/appx/appinstaller/2021')
$appInstallerRoot.SetAttribute('Version', $msixVersion)
$appInstallerRoot.SetAttribute('Uri', $appInstallerUri)
$appInstallerXml.AppendChild($appInstallerRoot) | Out-Null
$mainPackage = $appInstallerXml.CreateElement('MainPackage', $appInstallerRoot.NamespaceURI)
$mainPackage.SetAttribute('Name', 'Pulso35')
$mainPackage.SetAttribute('Publisher', $PublisherName)
$mainPackage.SetAttribute('Version', $msixVersion)
$mainPackage.SetAttribute('Uri', $packageUri)
$mainPackage.SetAttribute('ProcessorArchitecture', 'x64')
$appInstallerRoot.AppendChild($mainPackage) | Out-Null
$updateSettings = $appInstallerXml.CreateElement('UpdateSettings', $appInstallerRoot.NamespaceURI)
$onLaunch = $appInstallerXml.CreateElement('OnLaunch', $appInstallerRoot.NamespaceURI)
$onLaunch.SetAttribute('HoursBetweenUpdateChecks', '12')
$onLaunch.SetAttribute('ShowPrompt', 'true')
$onLaunch.SetAttribute('UpdateBlocksActivation', 'false')
$updateSettings.AppendChild($onLaunch) | Out-Null
$appInstallerRoot.AppendChild($updateSettings) | Out-Null
$writerSettings = New-Object System.Xml.XmlWriterSettings
$writerSettings.Encoding = New-Object System.Text.UTF8Encoding($false)
$writerSettings.Indent = $true
$writer = [System.Xml.XmlWriter]::Create($appInstallerPath, $writerSettings)
try { $appInstallerXml.Save($writer) } finally { $writer.Dispose() }
Write-Output "Pacote MSIX criado: dist\$msixName"
Write-Output "Arquivo para instalação e atualização pelo Windows: dist\Pulso35.appinstaller"
