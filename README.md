# Pulso

O Pulso é um aplicativo de foco para Windows e Android. Você escolhe os aplicativos e sites que quer pausar durante uma sessão ou limita o tempo que passa neles por hora e por dia. Cada dispositivo guarda sua própria lista, seus agendamentos e seu histórico.

## Começar a usar

| Plataforma | Download | Requisitos |
| --- | --- | --- |
| Windows | [Baixar Pulso.exe](Aplicativo/Pulso.exe?raw=true) | Windows x64 com .NET Desktop Runtime 10 e ASP.NET Core Runtime 10, ambos x64. [Downloads da Microsoft](https://dotnet.microsoft.com/pt-br/download/dotnet/10.0) |
| Android | [Baixar Pulso-Galaxy.apk](Android/Pulso-Galaxy.apk?raw=true) | Android 10 ou posterior. No Galaxy A55, o app acompanha o tema da One UI. |

Os pacotes de cada lançamento também ficam na [página de versões](https://github.com/drummLeo/pulso/releases).

### No Windows

1. Abra o executável. Em **Lista de bloqueio → Buscar aplicativos instalados**, o Pulso procura os programas deste PC, inclusive os que estão fechados. Marque os apps desejados e clique em **Salvar lista**. Para um programa portátil, você também pode escolher a janela aberta ou o arquivo `.exe`. Adicione os domínios na mesma tela.
2. Para bloquear sites, abra **Navegadores** e clique em **Copiar caminho da extensão**. No gerenciador de extensões do Chrome ou do Edge, ative o **Modo do desenvolvedor**, escolha **Carregar sem compactação** ou **Carregar sem pacote** e selecione a pasta indicada. Repita nos perfis em que deseja usar o bloqueio.
3. Em **Visão geral**, escolha a duração e clique em **Iniciar sessão**. Se você abrir um app bloqueado, o Pulso minimiza a janela e mostra um aviso com o motivo e o horário de liberação. Clique em **Entendi** para dispensar o aviso. Nos sites da lista, a extensão mostra uma página de pausa.

Fechar a janela mantém o Pulso na área de notificação, onde ele continua aplicando os limites e agendamentos. Para encerrar o aplicativo, use **Sair do Pulso** no menu do ícone. Se já usava a extensão, clique em **Recarregar** no cartão do Pulso no gerenciador de extensões após atualizar o executável.

### No Android

1. Instale o APK. Abra o Pulso e toque em **Configurar acessibilidade → Abrir Acessibilidade**. Na One UI, procure **Aplicativos instalados → Pulso • Bloqueio de foco** e ative o serviço.
2. Na aba **Lista**, toque em **Escolher aplicativos**, marque os apps e toque em **Salvar lista**. Adicione os domínios na mesma aba. Em **Foco**, selecione a duração e toque em **Iniciar sessão**.
3. Ao abrir um app bloqueado, você verá o aviso **Aplicativo em pausa**, com o motivo e o horário de liberação. Toque em **Entendi** para dispensá-lo. No Chrome, Edge e Samsung Internet, o Pulso cobre a página quando identifica um domínio bloqueado na barra de endereço visível.

### Escolher aplicativos por categoria

Nos dois aparelhos, o seletor e a lista de apps escolhidos mostram grupos como **Jogos**, **Redes sociais**, **Navegadores** e **Trabalho e estudo**. Você pode buscar pelo nome do app ou da categoria. Os apps que o Pulso não reconhecer ficam em **Outros**.

Para adicionar todos os jogos encontrados, use **Selecionar todos** no grupo **Jogos** do Windows ou **Marcar** no mesmo grupo do Android. Você pode desmarcar itens individuais antes de salvar. Durante uma busca, a ação do grupo vale só para os resultados visíveis. Apps instalados depois podem ser escolhidos na próxima busca.

### Bloquear sites de pornografia e apostas

No Windows, abra **Lista de bloqueio → Sites**. No Galaxy, abra **Lista → Sites**. Se a lista de aplicativos for longa, toque em **Ir para sites** para chegar direto ao cartão. Escolha um modo para **Pornografia** e outro para **Apostas**. As duas categorias começam desativadas, inclusive ao atualizar uma instalação anterior.

| Modo | Quando o bloqueio vale |
| --- | --- |
| **Desativado** | A categoria fica desligada. Os domínios que você cadastrou continuam com suas próprias regras. |
| **Durante o foco** | Durante uma sessão rápida ou um agendamento ativo. Em um agendamento com saldo por hora, os sites da categoria ficam bloqueados desde o início do horário. |
| **Sempre** | Também fora das sessões e dos agendamentos, enquanto a proteção do Pulso estiver funcionando. |

Por exemplo, deixe **Apostas** em **Sempre** e **Pornografia** em **Durante o foco**. No PC, mantenha o Pulso aberto, mesmo que esteja na bandeja, e a extensão conectada no Chrome ou no Edge. Se já usava a extensão, clique em **Recarregar** no gerenciador de extensões de cada perfil. No Galaxy, ative a Acessibilidade do Pulso e use o Chrome, o Edge ou o Samsung Internet com a barra de endereço visível.

O filtro reconhece domínios e seus subdomínios. As listas vêm no executável e no APK, e a comparação ocorre no aparelho. Se um site novo ainda estiver fora do catálogo, você pode adicioná-lo manualmente à sua lista de foco. A categoria **Apostas** também cobre o sufixo brasileiro `.bet.br`. [Ministério da Fazenda](https://www.gov.br/fazenda/pt-br/composicao/orgaos/secretaria-de-premios-e-apostas/apostas-de-quota-fixa)

## Limites, agenda e aparência

- **Limite individual.** No Windows, clique em **Configurar limite** ao lado de um app ou site. No Android, toque em **Limite**. Informe de 1 a 60 minutos por hora, de 1 a 1.440 minutos por dia ou preencha os dois campos.
- **Limite compartilhado.** A lista inteira pode dividir um saldo. Com 10 minutos por hora para um app e 30 minutos por dia para a lista, usar esse app por 6 minutos consome 6 minutos de cada saldo. A hora se renova na próxima hora cheia e o dia, à meia-noite do dispositivo.
- **Agendamentos.** Em **Agendamentos** no Windows ou **Agenda** no Android, crie ou edite um horário. Selecione **Limitar minutos por hora**, informe de 1 a 60 minutos e salve. Apps e sites da lista passam a dividir esse saldo somente durante o horário escolhido. Os agendamentos criados em versões anteriores continuam com bloqueio imediato até você editá-los.
- **Tema.** Escolha **Sistema**, **Claro** ou **Escuro** pelo botão **Tema** na barra lateral do Windows ou no cabeçalho do Android. **Sistema** acompanha a aparência escolhida no aparelho.

Por exemplo, crie **Trabalho** das 09h às 12h com 10 minutos por hora e **Noite** das 19h às 21h com 20. Se usar um app por 6 minutos às 09h e um site por mais 4, a lista fica em pausa até as 10h. Às 19h, o saldo de Noite começa com 20 minutos. Ao terminar cada agendamento, seu limite deixa de valer. Limites individuais, o saldo geral e outras regras que estiverem em vigor continuam valendo.

Os limites medem o uso em primeiro plano. No Windows, a extensão identifica a aba ativa pelo próprio PC. No Android, o serviço de Acessibilidade aplica os bloqueios no aparelho. Um domínio na lista também cobre seus subdomínios. [Guia do Windows](Leia-me.txt) · [Guia do Android](Android/Leia-me.txt)

## Compilar e testar

Execute os comandos na raiz do repositório, em um PowerShell no Windows.

**Windows.** Com o SDK do .NET 10 x64 instalado, compile o executável em `Aplicativo\Pulso.exe` e rode as verificações do motor de foco.

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

**Windows** 1.5.0 · **Android** 1.5 · **Extensão para Chrome e Edge** 1.5.0

- 25/11/2026 — Adiciona geração de Android App Bundle (.aab) no script de compilação · [6a1ed51](https://github.com/drummLeo/pulso/commit/6a1ed51a1f490f4d5900219353431a946ede4f39)
- 22/11/2026 — Adiciona filtros de sites de pornografia e apostas no Pulso 1.5 · [2dd64bb](https://github.com/drummLeo/pulso/commit/2dd64bb05e86abf6d5513bb99a7791e2d272d081)
- 22/11/2026 — Adiciona busca de aplicativos instalados e seleção por categoria no Pulso 1.4 · [688cfb8](https://github.com/drummLeo/pulso/commit/688cfb8c792d211daa267c1b97b1d5783bee7a49)
- 22/11/2026 — Adiciona saldo por agendamento e aviso de aplicativo em pausa no Pulso 1.3 · [a4b227b](https://github.com/drummLeo/pulso/commit/a4b227bd21d1ace0ab6e671c8b4aec5094cfb729)
- 22/11/2026 — Adiciona o modo escuro ao Pulso 1.2 e documenta o projeto · [95c485b](https://github.com/drummLeo/pulso/commit/95c485b3a8cad1c6982b9c86cbb64c5b6e4f0129)

<!-- pulso:resumo:fim -->

O GitHub Actions atualiza este resumo quando uma mudança chega à `main`. As instruções de uso acompanham as mudanças de comportamento na mesma entrega. [Histórico completo](https://github.com/drummLeo/pulso/commits/main/) · [Fluxo de atualização](.github/workflows/atualizar-readme.yml)
