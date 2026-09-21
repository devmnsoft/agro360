# Onboarding assistido

O onboarding canônico usa `saas_onboarding_templates`, `saas_onboarding_steps` e `saas_tenant_onboarding_progress`. A migration 097 acrescenta ação recomendada, pré-requisitos, motivo de bloqueio e histórico. O percentual é calculado no backend: etapas obrigatórias concluídas dividido pelas etapas obrigatórias aplicáveis; etapas opcionais não inflam o resultado.

## Checklist mínimo

1. dados do cliente; 2. plano; 3. módulos contratados; 4. administrador; 5. perfis iniciais; 6. propriedades/fazendas; 7. safra ou pecuária inicial; 8. estoque inicial quando contratado; 9. qualidade/compliance quando contratado; 10. treinamento; 11. pendências comerciais; 12. pendências técnicas.

Uma etapa só conclui quando sua condição real é confirmada. Pré-requisito ausente produz estado bloqueado e explicação, nunca percentual fictício. SuperAdmin acompanha globalmente sob autorização; usuário tenant consulta apenas o próprio tenant por RLS. Alterações guardam ator, horário, estado anterior, novo estado e motivo.

## Estado real

Persistência e regras básicas existem, mas a tela atual calcula uma visão parcial no JavaScript a partir de organização/usuários/convites. A projeção completa no backend, os endpoints de transição e a jornada assistida premium permanecem pendentes; portanto onboarding não está homologado.
