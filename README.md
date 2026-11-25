# Pulso

O Pulso é um aplicativo de foco para Windows, Linux e Android. Você escolhe os aplicativos e sites que quer pausar durante uma sessão ou limita o tempo que passa neles por hora e por dia. Cada dispositivo guarda sua própria lista, seus agendamentos e seu histórico.

## Começar a usar

| Plataforma | Download | Requisitos |
| --- | --- | --- |
| Windows | [Baixar Pulso.exe](Aplicativo/Pulso.exe?raw=true) | Windows x64 com .NET Desktop Runtime 10 e ASP.NET Core Runtime 10, ambos x64. [Downloads da Microsoft](https://dotnet.microsoft.com/pt-br/download/dotnet/10.0) |
| Linux | [Compilar pacote `.tar.gz`](#compilar-e-testar) · [Versões](https://github.com/drummLeo/pulso/releases) | Linux x64 ou arm64 com .NET Desktop/ASP.NET Core Runtime 10 (ou pacote `--self-contained`), X11 ou KDE Plasma Wayland (em outros compositores Wayland, cobre janelas XWayland e extensões). |
| Android | [Baixar Pulso-Galaxy.apk](Android/Pulso-Galaxy.apk?raw=true) | Android 10 ou posterior. No Galaxy A55, o app acompanha o tema da One UI. |

Os pacotes de cada lançamento também ficam na [página de versões](https://github.com/drummLeo/pulso/releases).

### No Windows

1. Abra o executável. Em **Lista de bloqueio → Buscar aplicativos instalados**, o Pulso procura os programas deste PC, inclusive os que estão fechados. Marque os apps desejados e clique em **Salvar lista**. Para um programa portátil, você também pode escolher a janela aberta ou o arquivo `.exe`. Adicione os domínios na mesma tela.
2. Para bloquear sites, abra **Navegadores** e clique em **Copiar caminho da extensão**. No gerenciador de extensões do Chrome ou do Edge, ative o **Modo do desenvolvedor**, escolha **Carregar sem compactação** ou **Carregar sem pacote** e selecione a pasta indicada. Repita nos perfis em que deseja usar o bloqueio.
3. Em **Visão geral**, escolha a duração e clique em **Iniciar sessão**. Se você abrir um app bloqueado, o Pulso minimiza a janela e mostra um aviso com o motivo e o horário de liberação. Clique em **Entendi** para dispensar o aviso. Nos sites da lista, a extensão mostra uma página de pausa.

Fechar a janela mantém o Pulso na área de notificação, onde ele continua aplicando os limites e agendamentos. Para encerrar o aplicativo, use **Sair do Pulso** no menu do ícone. Se já usava a extensão, clique em **Recarregar** no cartão do Pulso no gerenciador de extensões após atualizar o executável.

### No Linux

1. Compile e instale o pacote (`python3 Linux/compilar.py --install` ou execute `./instalar.sh` após extrair o `.tar.gz`) e abra o **Pulso** pelo menu do sistema ou pelo comando `pulso`. Em **Lista de bloqueio → Buscar aplicativos instalados**, o Pulso procura os programas instalados por arquivos `.desktop` em `XDG_DATA_HOME`, `~/.local/share/applications` e `XDG_DATA_DIRS` (incluindo aplicativos nativos, Flatpak e Snap). Marque os apps desejados e clique em **Salvar lista**. Você também pode usar **Escolher janela aberta** ou **Escolher executável**. Adicione os domínios na mesma tela.
2. Para bloquear sites, abra **Navegadores**. No Chrome ou Edge, clique em **Copiar caminho da extensão** (ou use **Abrir no Chrome** / **Abrir no Edge**), ative o **Modo do desenvolvedor** e carregue `~/.local/share/PulsoFoco/Extensao` (quando instalados via Flatpak, o Pulso libera automaticamente o acesso de leitura do usuário à pasta). No Firefox, clique em **Copiar caminho da extensão (Firefox)**, abra `about:debugging#/runtime/this-firefox` via **Abrir no Firefox**, clique em **Carregar extensão temporária...**, selecione `~/.local/share/PulsoFoco/ExtensaoFirefox/pulso-firefox.xpi` e clique em **Permitir acesso aos sites** no ícone da extensão.
3. Em **Visão geral**, escolha a duração e clique em **Iniciar sessão**. Em sessões X11 e no KDE Plasma Wayland (via KWin), ao tentar abrir um app bloqueado, o Pulso minimiza a janela e exibe o aviso com o motivo e o horário de liberação. Clique em **Entendi** (ou pressione `Esc`) para dispensar o aviso. Em outros compositores Wayland sem API de controle de janelas, a proteção de aplicativos se limita a janelas em X11/XWayland; o bloqueio e a medição de sites pelas extensões funcionam normalmente em X11 e Wayland.

Fechar a janela mantém o Pulso em segundo plano (com ícone na área de notificação quando o ambiente suporta StatusNotifierItem), continuando a aplicação de limites e agendamentos. Para encerrar o aplicativo, use **Sair do Pulso** no menu da bandeja (ou na barra lateral, quando não houver bandeja ativa) ou pressione `Ctrl+Q`. É possível iniciar já oculto com `pulso --minimized`.

### No Android

1. Instale o APK. Abra o Pulso e toque em **Configurar acessibilidade → Abrir Acessibilidade**. Na One UI, procure **Aplicativos instalados → Pulso • Bloqueio de foco** e ative o serviço.
2. Na aba **Lista**, toque em **Escolher aplicativos**, marque os apps e toque em **Salvar lista**. Adicione os domínios na mesma aba. Em **Foco**, selecione a duração e toque em **Iniciar sessão**.
3. Ao abrir um app bloqueado, você verá o aviso **Aplicativo em pausa**, com o motivo e o horário de liberação. Toque em **Entendi** para dispensá-lo. No Chrome, Edge e Samsung Internet, o Pulso cobre a página quando identifica um domínio bloqueado na barra de endereço visível.

### Escolher aplicativos por categoria

Em todas as plataformas, o seletor e a lista de apps escolhidos mostram grupos como **Jogos**, **Redes sociais**, **Navegadores** e **Trabalho e estudo**. Você pode buscar pelo nome do app ou da categoria. Os apps que o Pulso não reconhecer ficam em **Outros**.

Para adicionar todos os jogos encontrados, use **Selecionar todos** no grupo **Jogos** no desktop (Windows e Linux) ou **Marcar** no mesmo grupo do Android. Você pode desmarcar itens individuais antes de salvar. Durante uma busca, a ação do grupo vale só para os resultados visíveis. Apps instalados depois podem ser escolhidos na próxima busca.

### Bloquear sites de pornografia e apostas

No desktop (Windows e Linux), abra **Lista de bloqueio → Sites**. No Galaxy, abra **Lista → Sites**. Se a lista de aplicativos for longa, toque em **Ir para sites** para chegar direto ao cartão. Escolha um modo para **Pornografia** e outro para **Apostas**. As duas categorias começam desativadas, inclusive ao atualizar uma instalação anterior.

| Modo | Quando o bloqueio vale |
| --- | --- |
| **Desativado** | A categoria fica desligada. Os domínios que você cadastrou continuam com suas próprias regras. |
| **Durante o foco** | Durante uma sessão rápida ou um agendamento ativo. Em um agendamento com saldo por hora, os sites da categoria ficam bloqueados desde o início do horário. |
| **Sempre** | Também fora das sessões e dos agendamentos, enquanto a proteção do Pulso estiver funcionando. |

Por exemplo, deixe **Apostas** em **Sempre** e **Pornografia** em **Durante o foco**. No PC, mantenha o Pulso aberto, mesmo que esteja na bandeja, e a extensão conectada no Chrome, no Edge ou (no Linux) no Firefox. Se já usava a extensão, clique em **Recarregar** no gerenciador de extensões de cada perfil. No Galaxy, ative a Acessibilidade do Pulso e use o Chrome, o Edge ou o Samsung Internet com a barra de endereço visível.

O filtro reconhece domínios e seus subdomínios. As listas vêm no executável e no APK, e a comparação ocorre no aparelho. Se um site novo ainda estiver fora do catálogo, você pode adicioná-lo manualmente à sua lista de foco. A categoria **Apostas** também cobre o sufixo brasileiro `.bet.br`. [Ministério da Fazenda](https://www.gov.br/fazenda/pt-br/composicao/orgaos/secretaria-de-premios-e-apostas/apostas-de-quota-fixa)

## Limites, agenda e aparência

- **Limite individual.** No desktop (Windows e Linux), clique em **Configurar limite** ao lado de um app ou site. No Android, toque em **Limite**. Informe de 1 a 60 minutos por hora, de 1 a 1.440 minutos por dia ou preencha os dois campos.
- **Limite compartilhado.** A lista inteira pode dividir um saldo. Com 10 minutos por hora para um app e 30 minutos por dia para a lista, usar esse app por 6 minutos consome 6 minutos de cada saldo. A hora se renova na próxima hora cheia e o dia, à meia-noite do dispositivo.
- **Agendamentos.** Em **Agendamentos** no desktop ou **Agenda** no Android, crie ou edite um horário. Selecione **Limitar minutos por hora**, informe de 1 a 60 minutos e salve. Apps e sites da lista passam a dividir esse saldo somente durante o horário escolhido. Os agendamentos criados em versões anteriores continuam com bloqueio imediato até você editá-los.
- **Tema.** Escolha **Sistema**, **Claro** ou **Escuro** pelo botão **Tema** na barra lateral do desktop ou no cabeçalho do Android. **Sistema** acompanha a aparência escolhida no aparelho.

Por exemplo, crie **Trabalho** das 09h às 12h com 10 minutos por hora e **Noite** das 19h às 21h com 20. Se usar um app por 6 minutos às 09h e um site por mais 4, a lista fica em pausa até as 10h. Às 19h, o saldo de Noite começa com 20 minutos. Ao terminar cada agendamento, seu limite deixa de valer. Limites individuais, o saldo geral e outras regras que estiverem em vigor continuam valendo.

Os limites medem o uso em primeiro plano. No desktop, a extensão identifica a aba ativa pelo próprio computador. No Android, o serviço de Acessibilidade aplica os bloqueios no aparelho. Um domínio na lista também cobre seus subdomínios. [Guia do Windows](Leia-me.txt) · [Guia do Linux](Linux/Leia-me.txt) · [Guia do Android](Android/Leia-me.txt)

## Compilar e testar

Execute os comandos na raiz do repositório.

**Windows.** Em um PowerShell no Windows, com o SDK do .NET 10 x64 instalado, compile o executável em `Aplicativo\Pulso.exe` e rode as verificações do motor de foco.

```powershell
.\Compilar.cmd
dotnet run --project .\tests\EngineChecks.csproj
```

Os verificadores do Windows registram as falhas no terminal. Para testar a apresentação do aviso com o Pulso na bandeja usando uma lista fictícia, execute `dotnet run --project .\tests\Windows\WindowsChecks.csproj -- aviso-bandeja`.

Para verificar a descoberta de aplicativos, os filtros de sites e os seletores em uma área de trabalho privada do Windows, execute os comandos abaixo. O último requer Python 3.

```powershell
dotnet build .\tests\Windows\WindowsChecks.csproj -c Release
dotnet run --no-build -c Release --project .\tests\Windows\WindowsChecks.csproj -- catalogo
dotnet run --no-build -c Release --project .\tests\Windows\WindowsChecks.csproj -- filtros-navegador
python .\tests\Windows\test_catalogo_interface.py
```

Para testar a extensão em um perfil temporário do navegador, compile os verificadores acima. Na primeira execução, instale o Playwright e o Chromium com os dois primeiros comandos.

```powershell
python -m pip install playwright
python -m playwright install chromium
python .\tests\browser_check.py .\tests\Windows\bin\Release\net10.0-windows\Pulso.Verificacao.exe --prazo-conexao
```

O teste usa páginas locais para conferir bloqueio, liberação e reconexão. Para usar o Chrome ou o Edge já instalado, acrescente `--chromium "caminho\do\navegador.exe" --via-protocolo`.

**Linux.** Com o SDK do .NET 10 e Python 3 instalados, compile e empacote a edição Linux em `Linux/dist/pulso-linux-x64.tar.gz` (ou `linux-arm64`) e rode os testes do motor e da plataforma Linux:

```bash
python3 Linux/compilar.py
python3 Linux/compilar.py --self-contained
python3 Linux/compilar.py --install
dotnet run --project tests/EngineChecks.csproj
dotnet run --project tests/Linux/LinuxChecks.csproj
dotnet run --no-build --project tests/Linux/LinuxChecks.csproj -- catalogo-contagem
dotnet run --no-build --project tests/Linux/LinuxChecks.csproj -- kwin-teste
```

O script `Linux/compilar.py` gera a pasta distribuível, o arquivo `.tar.gz`, o atalho `.desktop` e o instalador `instalar.sh` (com `--desinstalar` para remoção). Use `--self-contained` para incluir o runtime do .NET 10 no pacote. Para testar a extensão do navegador no Linux com Playwright, compile em Release (`dotnet build tests/Linux/LinuxChecks.csproj -c Release`) e execute `python3 tests/browser_check.py tests/Linux/bin/Release/net10.0/Pulso.Linux.Verificacao`.

**Android.** Use Python 3, um JDK completo e o Android SDK com uma plataforma e as Build Tools instaladas. O script aceita o JDK do Android Studio ou `JAVA_HOME` e gera `Android\Pulso-Galaxy.apk`. A chave de assinatura fica em `%LOCALAPPDATA%\PulsoAndroid\Assinatura`. Atualizar uma instalação preservando os dados exige a mesma assinatura. Para gerar um Android App Bundle (`Android\Pulso-Galaxy.aab`) para envio ao Google Play, acrescente `--aab` (com `--keystore` e a senha na variável de ambiente `PULSO_ANDROID_SIGN` caso use uma chave de upload separada).

```powershell
python .\Android\compilar.py
python .\Android\compilar.py --aab
python .\Android\tests\test_tema.py
```

Para testar a seleção por categoria no Android, instale o APK em um emulador descartável chamado `PulsoTeste17` e execute `python .\Android\tests\test_categorias_android.py --serial emulator-5584`, substituindo o serial pelo do seu emulador. O teste usa uma lista fictícia e apaga os dados do Pulso nesse emulador.

Para testar os filtros de sites, use o mesmo AVD com uma imagem AOSP do Android e execute `python .\Android\tests\test_sites_android.py --serial emulator-5584`. O teste usa barras de endereço de exemplo, instala os pacotes de teste que estiverem ausentes e verifica os bloqueios com páginas locais.

As compilações usam as listas de domínios já incluídas em `shared/`. Para reproduzi-las, execute `python tools/gerar_listas_sites.py`. A opção `--check` confere os arquivos com as revisões de origem. O gerador usa as categorias Adult e Gambling de ShadowWhisperer, remove subdomínios redundantes e exclui sufixos de registro e hospedagem identificados pela Public Suffix List. O sufixo reservado `.bet.br` é incluído explicitamente. [Listas de origem](https://github.com/ShadowWhisperer/BlockLists/tree/8ed495030a7169b626a81b45761b84b8c36bf8e5) · [Licença Unlicense](https://github.com/ShadowWhisperer/BlockLists/blob/8ed495030a7169b626a81b45761b84b8c36bf8e5/LICENSE) · [Public Suffix List](https://publicsuffix.org/list/)

## Versões e alterações recentes

<!-- pulso:resumo:inicio -->

**Windows** 1.5.0 · **Linux** 1.5.0 · **Android** 1.5 · **Extensão para Chrome e Edge** 1.5.0

- 25/11/2026 — Adiciona versão nativa do Pulso para Linux com extensão para Firefox (#36) · [427992e](https://github.com/drummLeo/pulso/commit/427992e210366c1ceedadc0872184777a7f1547a)
- 25/11/2026 — Adiciona geração de Android App Bundle (.aab) no script de compilação · [6a1ed51](https://github.com/drummLeo/pulso/commit/6a1ed51a1f490f4d5900219353431a946ede4f39)
- 22/11/2026 — Adiciona filtros de sites de pornografia e apostas no Pulso 1.5 · [2dd64bb](https://github.com/drummLeo/pulso/commit/2dd64bb05e86abf6d5513bb99a7791e2d272d081)
- 22/11/2026 — Adiciona busca de aplicativos instalados e seleção por categoria no Pulso 1.4 · [688cfb8](https://github.com/drummLeo/pulso/commit/688cfb8c792d211daa267c1b97b1d5783bee7a49)
- 22/11/2026 — Adiciona saldo por agendamento e aviso de aplicativo em pausa no Pulso 1.3 · [a4b227b](https://github.com/drummLeo/pulso/commit/a4b227bd21d1ace0ab6e671c8b4aec5094cfb729)

<!-- pulso:resumo:fim -->

O GitHub Actions atualiza este resumo quando uma mudança chega à `main`. As instruções de uso acompanham as mudanças de comportamento na mesma entrega. [Histórico completo](https://github.com/drummLeo/pulso/commits/main/) · [Fluxo de atualização](.github/workflows/atualizar-readme.yml)
