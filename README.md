# Pulso 3.5

**Painel open source para telas USB.** O Pulso 3.5 monta e exibe um painel de computador em telas USB seriais de 3,5 polegadas. Inclui monitoramento de CPU, RAM, GPU e temperaturas, clima, editor visual de temas e execução contínua na bandeja do Windows.

O programa pode ser instalado em computadores diferentes. A instalação padrão guarda preferências no perfil local do usuário; a versão portátil guarda tudo na pasta extraída.

## Recursos

- Relógio e data em português, com opção de mostrar segundos.
- Uso de CPU, RAM e GPU; temperaturas de CPU/GPU quando o hardware e o provedor de sensores oferecem esses dados.
- Modo gamer automático: detecta jogos monitorados em primeiro plano pelo RTSS e lê FPS/frame time do RTSS ou do MSI Afterburner; média e 1% low aparecem quando o Afterburner os fornece. O cartão gamer aparece enquanto o jogo está em foco; modelos anteriores recebem esse cartão na atualização do tema.
- Clima configurável por cidade; a configuração inicial é Mauá, São Paulo.
- Editor de tema retrato e paisagem com camadas, arrastar e soltar, redimensionamento, imagens de fundo, texto dinâmico e prévia ao vivo.
- Fechar ou minimizar a janela mantém o programa na bandeja e a tela atualizada. **Sair e parar a tela** encerra o controlador.
- Inicialização automática opcional pelo Windows, configurável na janela principal ou no menu da bandeja, sem privilégios de administrador.
- Pacote MSIX com instalação e atualização pelo App Installer, além da versão portátil nas Releases do GitHub.
- Temas exportados em `.tema` podem ser compartilhados com outros computadores.

## Compatibilidade

- Windows x64 e .NET Framework 4.5 ou superior.
- O código de comunicação identifica telas seriais compatíveis pelo VID `1A86` e PID `5722`, usando o painel em 320 × 480 ou 480 × 320. Outras revisões e modelos precisam de validação física.
- A temperatura de CPU pode exigir LibreHardwareMonitor ou OpenHardwareMonitor com a leitura WMI ativada. A temperatura de GPU NVIDIA usa o NVML do driver.
- FPS e frame time requerem RTSS ou MSI Afterburner em execução e com dados disponíveis para o jogo. O Pulso só lê as memórias compartilhadas desses programas; não injeta código no jogo nem altera configurações, e não os inclui na instalação.
- Clima requer internet; leituras de hardware, temas e comunicação USB funcionam localmente.

## Instalar ou usar a versão portátil

Na página **Releases** do GitHub, escolha uma das opções:

- **MSIX:** baixe `Pulso35.appinstaller` na página **Releases** e abra o arquivo. O Windows instalará o Pulso 3.5; depois, o App Installer consulta novas versões automaticamente. Preferências e temas ficam em `%LOCALAPPDATA%\Controlador35`.
- **Portátil:** baixe `Pulso35-Portable-<versão>.zip` e extraia todos os arquivos para uma pasta onde você possa gravar. Execute `Pulso35.exe`. O marcador `portable.flag` faz com que as configurações, imagens e logs sejam salvos na subpasta `data`, que pode acompanhar o programa quando você mover a pasta para outro computador.

O MSIX precisa de uma assinatura confiável para instalar. Certificados autoassinados são adequados apenas para testes e precisam ser confiados manualmente em cada computador; o certificado de desenvolvimento do Pulso não é usado nas Releases públicas.

Depois de abrir o aplicativo, conecte a tela USB e clique em **Conectar e mostrar**. Para deixá-lo ativo em segundo plano, feche a janela pelo X; use o menu do ícone na bandeja para reabrir, ativar a inicialização com o Windows ou sair. Quando essa opção está ligada, o Pulso inicia oculto na bandeja, procura a tela e conecta automaticamente quando encontra apenas uma tela compatível.

## Atualizações

O botão **Atualizações…** abre as Releases do GitHub quando o aplicativo foi instalado por MSIX. Abra o arquivo `Pulso35.appinstaller` para aplicar uma atualização manualmente; o Windows também verifica novas versões ao iniciar o aplicativo. Na versão portátil, o Pulso compara versões, pede confirmação, confere o SHA-256 do pacote e atualiza os arquivos do aplicativo. A pasta portátil precisa permitir gravação.

Cada release publica `Pulso35-MSIX-<versão>.msix`, `Pulso35.appinstaller`, o ZIP portátil, `latest.json` e `SHA256SUMS.txt`. Para criar uma release, envie uma tag `vMAJOR.MINOR.PATCH`; o GitHub Actions compila o código, assina o executável, cria o pacote com o MSIX Packaging Tool, assina o MSIX e publica os artefatos.

## Compilar no Windows

Requisitos: Windows, PowerShell, compilador do .NET Framework, MSIX Packaging Tool e Windows 10/11 SDK. Abra o PowerShell como administrador para a captura do instalador pelo MSIX Packaging Tool. Para assinar localmente, instale no Windows um certificado cujo Subject corresponda a `MSIX_PUBLISHER`, ou use o Azure Artifact Signing Client Tools e o script `Assinar-Arquivos-Azure.ps1` descrito abaixo.

```powershell
.\Compilar.ps1
.\Preparar-Instalador.ps1 -SkipBuild
.\Preparar-Atualizacao.ps1 -SkipBuild
```

O aplicativo compilado fica em `dist\app`; o pacote MSIX e `Pulso35.appinstaller` ficam em `dist`; o ZIP portátil e `latest.json` ficam em `dist\atualizacao`. A instalação pública deve ser assinada por um emissor confiável. Para desenvolvimento local, o certificado autoassinado `CN=Pulso 3.5 Dev` pode gerar um pacote de teste, mas outros computadores só o instalarão após confiarem nesse certificado.

### Assinatura em uma máquina de desenvolvimento

O cliente Artifact Signing instalado pelo WinGet é apenas a ferramenta local do SignTool; ele não cria uma conta nem um certificado. Depois de obter uma conta Azure de Artifact Signing, um perfil de certificado e a função **Artifact Signing Certificate Profile Signer**, entre no Azure CLI (`az login`) e crie `metadata.json` com os dados da conta e do perfil. Esse arquivo está ignorado pelo Git:

```json
{
  "Endpoint": "https://brs.codesigning.azure.net",
  "CodeSigningAccountName": "nome-da-conta",
  "CertificateProfileName": "nome-do-perfil"
}
```

Compile, assine o executável portátil, gere um MSIX ainda sem assinatura e então assine o pacote. Troque `CN=Publicador Validado` pelo Subject exibido no certificado de Public Trust:

```powershell
az login
.\Compilar.ps1
.\Assinar-Arquivos-Azure.ps1 -Path .\dist\app\Pulso35.exe -MetadataPath .\metadata.json
.\Preparar-Instalador.ps1 -SkipBuild -SkipSigning -PublisherName 'CN=Publicador Validado'
$versao = (Get-Content .\versao.txt -Raw).Trim()
.\Assinar-Arquivos-Azure.ps1 -Path ".\dist\Pulso35-MSIX-$versao.msix" -MetadataPath .\metadata.json
```

Use no `Endpoint` a URL da região da sua conta. Mantenha o Subject do certificado idêntico ao `PublisherName` do MSIX para que o Windows aceite a instalação e as atualizações.

O workflow `CI` compila no Windows e verifica o pacote portátil. **Publicar release** é executado para tags `v*`, instala o MSIX Packaging Tool no executor Windows, assina o executável portátil e o MSIX e publica os artefatos. Para a assinatura preferida por OIDC, crie o ambiente GitHub `release`, configure nele os secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` e `AZURE_SUBSCRIPTION_ID`, e as variáveis `MSIX_PUBLISHER`, `ARTIFACT_SIGNING_ENDPOINT`, `ARTIFACT_SIGNING_ACCOUNT` e `ARTIFACT_SIGNING_PROFILE`. No Azure, crie uma credencial federada para `repo:vini-cius/pulso3.5:environment:release` com emissor `https://token.actions.githubusercontent.com` e público `api://AzureADTokenExchange`; atribua à identidade o papel **Artifact Signing Certificate Profile Signer**. O workflow usa essa identidade temporária do GitHub, sem guardar uma chave privada PFX nos secrets.

Como alternativa, o workflow aceita os secrets `MSIX_SIGNING_PFX_BASE64` e `MSIX_SIGNING_PFX_PASSWORD` para um certificado Authenticode confiável. Quando as configurações completas do Artifact Signing estão presentes, essa opção tem prioridade. O `MSIX_PUBLISHER` deve ser exatamente o Subject do certificado em qualquer um dos dois modos. O `GITHUB_TOKEN` publica a release.

## Dados locais e assinatura

Na instalação padrão, os dados ficam em `%LOCALAPPDATA%\Controlador35`; a desinstalação não remove essa pasta. No modo portátil, os dados ficam em `data` junto ao executável. A opção de inicialização vale para o usuário atual do Windows; na versão portátil, desative e ative novamente depois de mover a pasta do aplicativo.

O pacote MSIX publicado deve ter assinatura de um emissor confiável. Artifact Signing exige uma assinatura Azure paga e validação de identidade; não funciona com assinaturas Azure gratuitas, de avaliação ou patrocinadas. A disponibilidade de certificados Public Trust depende do país e do tipo de identidade. Perfis Private Trust servem a ambientes controlados e não substituem uma assinatura pública para distribuição ampla. Mantenha `MSIX_PUBLISHER` igual em todas as releases: esse identificador define a família do pacote, e mudá-lo impede o Windows de reconhecer atualizações da instalação existente. A assinatura também não elimina imediatamente alertas do SmartScreen ou detecções de antivírus; reputação e histórico de distribuição ainda contam. O executável portátil recebe sua própria assinatura no workflow e também pode ser analisado pelo antivírus.

## Contribuir e segurança

Leia [CONTRIBUTING.md](CONTRIBUTING.md) antes de abrir pull requests. Problemas de segurança devem ser enviados pelo canal privado da aba **Security** do GitHub, quando habilitado; não publique detalhes de uma vulnerabilidade em issues abertas.

## Licença e marcas

O projeto está sob GNU GPL versão 3 ou, a seu critério, qualquer versão posterior; consulte [LICENSE](LICENSE). A referência pública de protocolo para telas Turing Smart Screen está sob GPL-3.0. Este projeto não é afiliado nem endossado por Turing, XuanFang, Kipye ou fabricantes relacionados; os nomes e marcas pertencem a seus respectivos titulares.
