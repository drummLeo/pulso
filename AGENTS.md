# Documentação do Pulso

Revise o `README.md` em cada modificação do projeto. Se mudar o comportamento, a interface, um requisito, a instalação, a compilação ou os testes descritos, atualize na mesma entrega os passos e exemplos correspondentes. Confira os nomes dos controles no código e as instruções no `Leia-me.txt` da plataforma afetada.

Para alterações internas, descreva o efeito no assunto do commit. O fluxo de documentação leva esse assunto para a lista de alterações recentes na `main`. Use exemplos fictícios nos guias públicos.

A seção delimitada por `<!-- pulso:resumo:inicio -->` e `<!-- pulso:resumo:fim -->` é gerada a partir dos manifestos e do histórico Git. Para atualizá-la localmente, execute `python .github/scripts/atualizar_readme.py`. Para conferir o resultado sem editar o arquivo, use `python .github/scripts/atualizar_readme.py --check`.
