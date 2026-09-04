# Fiscal & Compliance 360 — workflow seguro

Fluxo de saída: rascunho → validação → pronto para envio → submissão idempotente → consulta → retorno oficial. Sem provider configurado, o documento permanece pendente e a tentativa informa `ProviderNotConfigured`.

Documentos autorizados não aceitam mutação administrativa para cancelado. O endpoint de cancelamento exige motivo e chama o adapter; somente confirmação externa atualiza o estado. A trilha de tentativas é append-only e segregada por tenant.

O cadastro manual existente representa recepção/controle de um documento externo. O endpoint genérico de status foi limitado à rejeição de documentos ainda não autorizados e não pode mais atribuir autorização ou cancelamento.

Perfis são independentes por tenant/filial. A configuração do provider armazena apenas `credential_reference`; o banco rejeita nomes comuns de segredos em `configuration_json`.
