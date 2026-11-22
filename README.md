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

Para verificar a descoberta de aplicativos e exercitar o seletor com dados fictícios em uma área de trabalho privada do Windows, execute os três comandos abaixo. O último requer Python 3.

```powershell
dotnet build .\tests\Windows\WindowsChecks.csproj -c Release
dotnet run --no-build -c Release --project .\tests\Windows\WindowsChecks.csproj -- catalogo
python .\tests\Windows\test_catalogo_interface.py
```

**Android.** Use Python 3, um JDK completo e o Android SDK com uma plataforma e as Build Tools instaladas. O script aceita o JDK do Android Studio ou `JAVA_HOME` e gera `Android\Pulso-Galaxy.apk`. A chave de assinatura fica em `%LOCALAPPDATA%\PulsoAndroid\Assinatura`. Atualizar uma instalação preservando os dados exige a mesma assinatura.

```powershell
python .\Android\compilar.py
python .\Android\tests\test_tema.py
```

Para testar a seleção por categoria no Android, instale o APK em um emulador descartável chamado `PulsoTeste17` e execute `python .\Android\tests\test_categorias_android.py --serial emulator-5584`, substituindo o serial pelo do seu emulador. O teste usa uma lista fictícia e apaga os dados do Pulso nesse emulador.

## Versões e alterações recentes

<!-- pulso:resumo:inicio -->

**Windows** 1.4.0 · **Android** 1.4 · **Extensão para Chrome e Edge** 1.2.0

- 22/11/2026 — Adiciona saldo por agendamento e aviso de aplicativo em pausa no Pulso 1.3 · [a4b227b](https://github.com/drummLeo/pulso/commit/a4b227bd21d1ace0ab6e671c8b4aec5094cfb729)
- 22/11/2026 — Adiciona o modo escuro ao Pulso 1.2 e documenta o projeto · [95c485b](https://github.com/drummLeo/pulso/commit/95c485b3a8cad1c6982b9c86cbb64c5b6e4f0129)
- 22/11/2026 — Adiciona o Pulso 1.1 para Windows e Android · [96455f8](https://github.com/drummLeo/pulso/commit/96455f80642d58113dcaa4f49ea93c47cd81453f)

<!-- pulso:resumo:fim -->

O GitHub Actions atualiza este resumo quando uma mudança chega à `main`. As instruções de uso acompanham as mudanças de comportamento na mesma entrega. [Histórico completo](https://github.com/drummLeo/pulso/commits/main/) · [Fluxo de atualização](.github/workflows/atualizar-readme.yml)
