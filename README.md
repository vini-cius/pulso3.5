# Pulso 3.5

**Painel open source para telas USB.** O Pulso 3.5 monta e exibe um painel de computador em telas USB seriais de 3,5 polegadas. Inclui monitoramento de CPU, RAM, GPU e temperaturas, clima, editor visual de temas e execução contínua na bandeja do Windows.

O programa pode ser instalado em computadores diferentes. A instalação padrão guarda preferências no perfil local do usuário; a versão portátil guarda tudo na pasta extraída.

## Recursos

- Relógio e data em português, com opção de mostrar segundos.
- Uso de CPU, RAM e GPU; temperaturas de CPU/GPU quando o hardware e o provedor de sensores oferecem esses dados.
- Clima configurável por cidade; a configuração inicial é Mauá, São Paulo.
- Editor de tema retrato e paisagem com camadas, arrastar e soltar, redimensionamento, imagens de fundo, texto dinâmico e prévia ao vivo.
- Fechar ou minimizar a janela mantém o programa na bandeja e a tela atualizada. **Sair e parar a tela** encerra o controlador.
- Instalador padrão e versão portátil publicados nas Releases do GitHub.
- Temas exportados em `.tema` podem ser compartilhados com outros computadores.

## Compatibilidade

- Windows x64 e .NET Framework 4.5 ou superior.
- O código de comunicação identifica telas seriais compatíveis pelo VID `1A86` e PID `5722`, usando o painel em 320 × 480 ou 480 × 320. Outras revisões e modelos precisam de validação física.
- A temperatura de CPU pode exigir LibreHardwareMonitor ou OpenHardwareMonitor com a leitura WMI ativada. A temperatura de GPU NVIDIA usa o NVML do driver.
- Clima requer internet; leituras de hardware, temas e comunicação USB funcionam localmente.

## Instalar ou usar a versão portátil

Na página **Releases** do GitHub, escolha uma das opções:

- **Instalador:** baixe `Pulso35-Setup-<versão>.exe` e execute. A instalação fica em `%LOCALAPPDATA%\Programs\Pulso35`, sem pedir privilégios de administrador. Preferências e temas ficam em `%LOCALAPPDATA%\Controlador35`.
- **Portátil:** baixe `Pulso35-Portable-<versão>.zip` e extraia todos os arquivos para uma pasta onde você possa gravar. Execute `Pulso35.exe`. O marcador `portable.flag` faz com que as configurações, imagens e logs sejam salvos na subpasta `data`, que pode acompanhar o programa quando você mover a pasta para outro computador.

Depois de abrir o aplicativo, conecte a tela USB e clique em **Conectar e mostrar**. Para deixá-lo ativo em segundo plano, feche a janela pelo X; use o menu do ícone na bandeja para reabrir ou sair.

## Atualizações

O botão **Atualizações…** consulta a release mais recente deste repositório no GitHub. O Pulso compara versões, pede confirmação, confere o SHA-256 do pacote e atualiza os arquivos do aplicativo. A versão portátil precisa estar em uma pasta com permissão de gravação para receber atualizações pelo botão; também é possível baixar e extrair o ZIP portátil da nova release.

Cada release publica o instalador, o ZIP portátil, `latest.json` e `SHA256SUMS.txt`. Para criar uma release, envie uma tag `vMAJOR.MINOR.PATCH`, por exemplo `v1.0.0`; o GitHub Actions compila o código e anexa os arquivos automaticamente.

## Compilar no Windows

Requisitos: PowerShell, compilador do .NET Framework e Windows 10/11 SDK. Se o Inno Setup 6 ou 7 não estiver instalado, `Preparar-Instalador.ps1` baixa o compilador oficial, valida sua assinatura digital e o usa temporariamente.

```powershell
.\Compilar.ps1
.\Preparar-Instalador.ps1 -SkipBuild
.\Preparar-Atualizacao.ps1 -SkipBuild
```

O aplicativo compilado fica em `dist\app`; o instalador fica em `dist`; o ZIP portátil e `latest.json` ficam em `dist\atualizacao`. Para uma publicação, o workflow configura no manifesto o endereço HTTPS do ZIP correspondente à release.

O workflow `CI` compila no Windows e verifica o pacote portátil. **Publicar release** é executado para tags `v*`, monta o instalador com Inno Setup e publica os artefatos. Nenhum segredo é necessário: o workflow usa o `GITHUB_TOKEN` apenas para criar a release.

## Dados locais e assinatura

Na instalação padrão, os dados ficam em `%LOCALAPPDATA%\Controlador35`; a desinstalação não remove essa pasta. No modo portátil, os dados ficam em `data` junto ao executável. O aplicativo não configura inicialização automática com o Windows.

Os executáveis publicados não têm assinatura Authenticode. O certificado autoassinado de desenvolvimento não é usado na release e não torna os arquivos confiáveis nos computadores de outras pessoas. Por isso, o Smart App Control pode bloquear o instalador ou o aplicativo até que haja uma assinatura de um emissor confiável ou reputação suficiente.

## Contribuir e segurança

Leia [CONTRIBUTING.md](CONTRIBUTING.md) antes de abrir pull requests. Problemas de segurança devem ser enviados pelo canal privado da aba **Security** do GitHub, quando habilitado; não publique detalhes de uma vulnerabilidade em issues abertas.

## Licença e marcas

O projeto está sob GNU GPL versão 3 ou, a seu critério, qualquer versão posterior; consulte [LICENSE](LICENSE). A referência pública de protocolo para telas Turing Smart Screen está sob GPL-3.0. Este projeto não é afiliado nem endossado por Turing, XuanFang, Kipye ou fabricantes relacionados; os nomes e marcas pertencem a seus respectivos titulares.
