# Pulso no Android

O mostrador de 50 divisões e o botão verde identificam a sessão de foco, como no Pulso para Windows. O tempo restante ocupa o centro da tela. A navegação inferior mantém Foco, Lista, Agenda e Histórico acessíveis ao usar o telefone com uma mão.

As cores e os componentes compartilhados ficam em `Ui.java`. O fundo claro usa `#F5F6FA`, o mostrador usa `#293653`, a ação de foco usa `#D9F2C6` e os controles de configuração usam `#424EAB`. O texto usa a família sem serifa do sistema. Os alvos interativos têm pelo menos 48 dp de altura.

Seleção de aplicativos, teclado, seleção de horário e diálogos usam os controles do Android. A tela de configuração apresenta o uso da Acessibilidade antes de abrir as preferências do sistema. A lista, a agenda e o histórico ficam no perfil local do Android.

Na aba Lista, o botão Limite de cada item e o botão Editar limite compartilhado abrem o mesmo formulário nativo com campos de minutos por hora e por dia. O consumo de cada período aparece junto ao item ou ao cartão compartilhado. Quando uma cota se esgota, a tela de bloqueio e o aviso ao sair de um app indicam a causa e o horário de liberação. A edição mantém a posição da lista.
