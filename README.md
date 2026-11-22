# Pulso

O Pulso é um aplicativo de foco para Windows e Android. Você escolhe os aplicativos e sites que quer pausar durante uma sessão ou limita o tempo que passa neles por hora e por dia. Cada dispositivo guarda sua própria lista, seus agendamentos e seu histórico.

## Começar a usar

| Plataforma | Download | Requisitos |
| --- | --- | --- |
| Windows | [Baixar Pulso.exe](Aplicativo/Pulso.exe?raw=true) | Windows x64 com .NET Desktop Runtime 10 e ASP.NET Core Runtime 10, ambos x64. [Downloads da Microsoft](https://dotnet.microsoft.com/pt-br/download/dotnet/10.0) |
| Android | [Baixar Pulso-Galaxy.apk](Android/Pulso-Galaxy.apk?raw=true) | Android 10 ou posterior. No Galaxy A55, o app acompanha o tema da One UI. |

Os pacotes de cada lançamento também ficam na [página de versões](https://github.com/drummLeo/pulso/releases).

### No Windows

1. Abra o executável. Em **Lista de bloqueio**, escolha uma janela ou um arquivo `.exe` e adicione os domínios que deseja pausar.
2. Para bloquear sites, abra **Navegadores** e clique em **Copiar caminho da extensão**. No gerenciador de extensões do Chrome ou do Edge, ative o **Modo do desenvolvedor**, escolha **Carregar sem compactação** ou **Carregar sem pacote** e selecione a pasta indicada. Repita nos perfis em que deseja usar o bloqueio.
3. Em **Visão geral**, escolha a duração e clique em **Iniciar sessão**. O Pulso minimiza as janelas selecionadas e a extensão mostra uma página de pausa nos sites da lista.

Fechar a janela mantém o Pulso na área de notificação, onde ele continua aplicando os limites e agendamentos. Para encerrar o aplicativo, use **Sair do Pulso** no menu do ícone. Se já usava a extensão, clique em **Recarregar** no cartão do Pulso no gerenciador de extensões após atualizar o executável.

### No Android

1. Instale o APK. Abra o Pulso e toque em **Configurar acessibilidade → Abrir Acessibilidade**. Na One UI, procure **Aplicativos instalados → Pulso • Bloqueio de foco** e ative o serviço.
2. Na aba **Lista**, escolha os aplicativos e adicione os domínios. Em **Foco**, selecione a duração e toque em **Iniciar sessão**.
3. Ao abrir um aplicativo em pausa, o celular volta à tela inicial. No Chrome, Edge e Samsung Internet, o Pulso cobre a página quando identifica um domínio bloqueado na barra de endereço visível.

## Limites, agenda e aparência

- **Limite individual.** No Windows, clique em **Configurar limite** ao lado de um app ou site. No Android, toque em **Limite**. Informe de 1 a 60 minutos por hora, de 1 a 1.440 minutos por dia ou preencha os dois campos.
- **Limite compartilhado.** A lista inteira pode dividir um saldo. Com 10 minutos por hora para um app e 30 minutos por dia para a lista, usar esse app por 6 minutos consome 6 minutos de cada saldo. A hora se renova na próxima hora cheia e o dia, à meia-noite do dispositivo.
- **Sessões recorrentes.** Em **Agendamentos** no Windows ou **Agenda** no Android, escolha os dias, o horário e a duração. Consulte as sessões encerradas em **Histórico**.
- **Tema.** Escolha **Sistema**, **Claro** ou **Escuro** pelo botão **Tema** na barra lateral do Windows ou no cabeçalho do Android. **Sistema** acompanha a aparência escolhida no aparelho.

Os limites medem o uso em primeiro plano. No Windows, a extensão identifica a aba ativa pelo próprio PC. No Android, o serviço de Acessibilidade aplica os bloqueios no aparelho. Um domínio na lista também cobre seus subdomínios. [Guia do Windows](Leia-me.txt) · [Guia do Android](Android/Leia-me.txt)

## Compilar e testar

Execute os comandos na raiz do repositório, em um PowerShell no Windows.

**Windows.** Com o SDK do .NET 10 x64 instalado, compile o executável em `Aplicativo\Pulso.exe` e rode as verificações do motor de foco.

```powershell
.\Compilar.cmd
dotnet run --project .\tests\EngineChecks.csproj
```

**Android.** Use Python 3, um JDK completo e o Android SDK com uma plataforma e as Build Tools instaladas. O script aceita o JDK do Android Studio ou `JAVA_HOME` e gera `Android\Pulso-Galaxy.apk`. A chave de assinatura fica em `%LOCALAPPDATA%\PulsoAndroid\Assinatura`. Atualizar uma instalação preservando os dados exige a mesma assinatura.

```powershell
python .\Android\compilar.py
python .\Android\tests\test_tema.py
```

## Versões e alterações recentes

<!-- pulso:resumo:inicio -->

**Windows** 1.2.0 · **Android** 1.2 · **Extensão para Chrome e Edge** 1.2.0

- 22/11/2026 — Adiciona o modo escuro ao Pulso 1.2 e documenta o projeto · [95c485b](https://github.com/drummLeo/pulso/commit/95c485b3a8cad1c6982b9c86cbb64c5b6e4f0129)
- 22/11/2026 — Adiciona o Pulso 1.1 para Windows e Android · [96455f8](https://github.com/drummLeo/pulso/commit/96455f80642d58113dcaa4f49ea93c47cd81453f)

<!-- pulso:resumo:fim -->

O GitHub Actions atualiza este resumo quando uma mudança chega à `main`. As instruções de uso acompanham as mudanças de comportamento na mesma entrega. [Histórico completo](https://github.com/drummLeo/pulso/commits/main/) · [Fluxo de atualização](.github/workflows/atualizar-readme.yml)
