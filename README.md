# Pulso 3.5

**Painel open source para telas USB.** O Pulso 3.5 monta e exibe um painel de computador em telas USB seriais de 3,5 polegadas. Inclui monitoramento de CPU, RAM, GPU e temperaturas, clima, editor visual de temas e execução contínua na bandeja do Windows.

O programa foi desenvolvido para ser instalado em computadores diferentes. Preferências, temas e cidade do clima ficam no perfil local de cada usuário; não são fixados ao computador de desenvolvimento.

## Recursos

- Relógio e data em português, com opção de mostrar segundos.
- Uso de CPU, RAM e GPU; temperaturas de CPU/GPU quando o hardware e o provedor de sensores oferecem esses dados.
- Clima configurável por cidade; a configuração inicial é Mauá, São Paulo.
- Editor de tema retrato e paisagem com camadas, arrastar e soltar, redimensionamento, imagens de fundo, texto dinâmico e prévia ao vivo.
- Fechar ou minimizar a janela mantém o programa na bandeja e a tela atualizada. **Sair e parar a tela** encerra o controlador.
- Instalador por usuário do Windows e verificação manual de atualizações a partir de releases HTTPS do GitHub.
- Temas exportados em `.tema` podem ser compartilhados com outros computadores.

## Compatibilidade

- Windows x64 e .NET Framework 4.5 ou superior.
- O código de comunicação identifica telas seriais compatíveis pelo VID `1A86` e PID `5722`, usando o painel em 320 × 480 ou 480 × 320. Outras revisões e modelos precisam de validação física.
- A temperatura de CPU pode exigir LibreHardwareMonitor ou OpenHardwareMonitor com a leitura WMI ativada. A temperatura de GPU NVIDIA usa o NVML do driver.
- Clima requer internet; leituras de hardware, temas e comunicação USB funcionam localmente.

## Instalação

Na página **Releases** do GitHub, baixe `Pulso35-Setup-<versão>.exe` e execute. A instalação fica em `%LOCALAPPDATA%\Programs\Pulso35` e não pede privilégios de administrador. Preferências e temas continuam em `%LOCALAPPDATA%\Controlador35`.

Depois de abrir o aplicativo, conecte a tela USB e clique em **Conectar e mostrar**. Para deixá-lo ativo em segundo plano, feche a janela pelo X; use o menu do ícone na bandeja para reabrir ou sair.

## Atualizações

O botão **Atualizações…** consulta a release mais recente deste repositório no GitHub. As releases publicadas pelo workflow incluem o instalador, o pacote ZIP e `latest.json`. O Pulso compara versões, pede confirmação antes de instalar e confere o SHA-256 do pacote.

Para criar uma release, publique uma tag `vMAJOR.MINOR.PATCH`, por exemplo `v1.0.0`; o GitHub Actions compila o código e anexa os arquivos à release.

## Compilar no Windows

Requisitos: PowerShell, compilador do .NET Framework instalado pelo Windows e Inno Setup 6 ou 7 para gerar o instalador.

```powershell
.\Compilar.ps1
.\Preparar-Instalador.ps1
.\Preparar-Atualizacao.ps1 -Notas "Descrição da versão"
```

O aplicativo compilado fica em `dist\app`. A atualização fica em `dist\atualizacao`; publique `latest.json` e o ZIP juntos em HTTPS. O workflow de release recebe a URL absoluta do pacote do próprio GitHub. Os scripts também aceitam `-SkipBuild` para reaproveitar a compilação atual.

O workflow `CI` compila em Windows para alterações e pull requests. `Publicar release` é executado para tags `v*`, monta o instalador com Inno Setup e publica os artefatos. Nenhum segredo de publicação é necessário: o workflow usa o `GITHUB_TOKEN` com acesso de escrita somente a releases.

## Preferências e dados locais

Os dados ficam em `%LOCALAPPDATA%\Controlador35`: tema, imagens, cidade e logs. A desinstalação não remove essa pasta. O aplicativo não configura inicialização automática com o Windows.

## Contribuir e segurança

Leia [CONTRIBUTING.md](CONTRIBUTING.md) antes de abrir pull requests. Problemas de segurança devem ser enviados pelo canal privado da aba **Security** do GitHub, quando habilitado; não publique detalhes de uma vulnerabilidade em issues abertas.

## Licença e marcas

O projeto está sob GNU GPL versão 3 ou, a seu critério, qualquer versão posterior; consulte [LICENSE](LICENSE). A referência pública de protocolo para telas Turing Smart Screen está sob GPL-3.0. Este projeto não é afiliado nem endossado por Turing, XuanFang, Kipye ou fabricantes relacionados; os nomes e marcas pertencem a seus respectivos titulares.

Os pacotes gerados não têm assinatura Authenticode. Releases são montadas pelo GitHub Actions a partir do código publicado.
