# Portal Externo Agro360

O Portal Agro360 é uma superfície separada da administração. Tokens externos recebem apenas `portal.access`; todas as consultas combinam `tenant_id` com o usuário e seus vínculos. Os perfis homologáveis são produtor, cooperado, cliente B2B, comprador, fornecedor, transportador, representante, auditor e técnico parceiro.

## Acesso

1. Um administrador cria o convite em `POST /api/portal/invitations`, selecionando perfil e entidade em um lookup da administração.
2. O serviço gera 384 bits aleatórios, persiste somente SHA-256 e registra `PortalInvitationRequested` na outbox. Sem provedor configurado, a outbox permanece pendente; o sistema não declara que enviou e-mail.
3. O convidado abre `/Portal/Accept`, aceita os termos e cria uma senha forte. Convites expirados, revogados ou usados são rejeitados.
4. Em `/Portal/Login`, informa o slug da organização, e-mail e senha. O JWT resultante não autoriza nenhuma policy administrativa.

## Autoatendimento por perfil

O dashboard é composto de dados reais do usuário vinculado. Marketplace, comunicados e solicitações já estão habilitados. Documentos, certificados, dossiês, pedidos e logística devem ser exibidos somente quando o vínculo externo autorizar a entidade. Uploads reutilizam as regras do módulo documental (extensão, MIME, tamanho e hash); não se deve criar um segundo armazenamento.

Transportadores registram apenas entregas atribuídas. Fornecedores bloqueados não podem cotar e alterações cadastrais sensíveis permanecem em análise interna. Integrações comerciais ainda não homologadas devem conservar a cotação em `REQUESTED`, sem simular pedido ou pagamento.

## Homologação

Crie usuários de cada perfil, vincule entidades reais, confirme RLS entre dois tenants, teste token vencido/revogado, viewport de 360 px, teclado, mensagens de erro e download não autorizado. Revise também a outbox e os eventos de auditoria sem registrar tokens ou senhas.
