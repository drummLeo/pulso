# Pulso no Android

O mostrador de 50 divisões e o botão verde identificam a sessão de foco, como no Pulso para Windows. O tempo restante ocupa o centro da tela. A navegação inferior mantém Foco, Lista, Agenda e Histórico acessíveis ao usar o telefone com uma mão.

As paletas de `res/values/colors.xml` e `res/values-night/colors.xml` alimentam os componentes de `Ui.java`, os controles nativos e a tela de site em pausa. O fundo claro usa `#F5F6FA`, o mostrador usa `#293653` e a ação de foco usa `#D9F2C6`. O fundo escuro usa `#161A25`, os cartões usam `#202636`, o acento lilás usa `#BBC5FF` e a ação de foco usa `#CBEBAF`. O texto usa a família sem serifa do sistema. Os alvos interativos têm pelo menos 48 dp de altura.

O botão Tema fica no cabeçalho das quatro abas. A opção Sistema acompanha a aparência do Android. Claro e Escuro fixam a aparência escolhida no Pulso. A escolha fica nas preferências locais de aparência e independe dos dados de foco e do serviço de Acessibilidade.

Seleção de aplicativos, teclado, seleção de horário e diálogos usam os controles do Android. A tela de configuração apresenta o uso da Acessibilidade antes de abrir as preferências do sistema. A lista, a agenda e o histórico ficam no perfil local do Android.

O seletor e a lista salva usam os mesmos nomes de categoria do Windows. No seletor, cada grupo oferece seleção em conjunto e cada app pode ser marcado separadamente. A busca filtra os itens visíveis. As escolhas fora do filtro são preservadas até Salvar lista.

Na aba Lista, o botão Limite de cada item e o botão Editar limite compartilhado abrem o mesmo formulário nativo com campos de minutos por hora e por dia. O consumo de cada período aparece junto ao item ou ao cartão compartilhado. A edição mantém a posição da lista. Limites por agendamento abre a Agenda, onde cada horário permite escolher entre Bloqueio imediato e Limitar minutos por hora. O campo de minutos só se aplica à segunda escolha.

Ao tentar usar um app em pausa, o aviso mostra o nome do aplicativo, o motivo e o horário de liberação. O botão Entendi dispensa o aviso. Os sites continuam com a página de pausa e acesso à barra de endereço. As duas superfícies seguem o tema do Pulso.
