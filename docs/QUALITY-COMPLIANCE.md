# Qualidade e Compliance — Sprint 28

A Central usa requisitos parametrizados por tenant; o Agro360 não presume limites ou normas. Cadastre área, severidade, aplicabilidade, validade, responsável, evidências e se a falha crítica bloqueia o lote. Relacionamentos são selecionados nos catálogos autorizados do tenant, nunca digitados como UUID.

## Operação

1. Cadastre e ative o requisito; requisitos inativos não abrem pendências.
2. Publique uma especificação vigente para o produto. A edição de versão ativa deve criar versão nova, preservando inspeções anteriores.
3. Abra a inspeção, selecione produto/lote e registre todos os parâmetros obrigatórios. Resultado crítico fora da tolerância impede aprovação e origina não conformidade/hold.
4. Bloqueio, quarentena, reprovação e cancelamento exigem motivo; liberação exige `compliance.approve` e gera histórico auditável.
5. Na não conformidade, registre causa raiz e CAPA com responsável/prazo. O encerramento exige ações obrigatórias concluídas e evidência ou justificativa.
6. Auditorias exigem escopo e checklist; achado crítico gera não conformidade. Relatório reprovado exige motivo.

## Beneficiamento e exportação

Configure por produto as etapas, tempo/temperatura mínimos, checklist, foto, evidência, responsável e aprovação. O exemplo tucupi deve ser configurado pela organização; nenhum limite normativo é fornecido implicitamente. A prontidão de exportação agrega requisitos, inspeção, lote, documentos e evidências: item obrigatório pendente mantém o dossiê bloqueado e lote retido/reprovado impede certificado.

## Homologação

Valide perfis de leitura, escrita e aprovação; isolamento de tenant; histórico de decisões; arquivo protegido; CSV; telas vazias; teclado; contraste; desktop, tablet e celular. Teste também venda, expedição e certificado com lote bloqueado.
