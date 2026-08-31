# Campo Mobile PWA — Sprint 43

## Escopo operacional

`/field` é a área mobile-first para produtor, técnico, operadores agrícola e pecuário, almoxarife, compras, qualidade, produção, logística, manutenção, auditor e gestor. O navegador instala o shell via `manifest.webmanifest`; o service worker guarda somente recursos estáticos e catálogo de seleção. Mutações offline permanecem na outbox IndexedDB até confirmação do servidor.

Não existe aplicativo nativo nesta entrega. Câmera, localização e armazenamento usam APIs do navegador e sua permissão real. Localização negada é mostrada como indisponível; coordenadas nunca são fabricadas. A assinatura é **gerencial simples**, com hash SHA-256 do conteúdo, e não é assinatura digital ICP-Brasil.

## Segurança e tenant

Todas as tabelas `field_mobile.*` possuem `tenant_id`, RLS forçada e políticas baseadas em `platform.current_tenant_id()`. APIs exigem autenticação e políticas `field-mobile.read`, `field-mobile.write`, `field-mobile.approve`, `field-mobile.sync`, `field-mobile.conflicts.resolve` ou `field-mobile.reports`. Atalhos são derivados do perfil persistido e da permissão efetiva; navegar diretamente não contorna a policy.

Entidades são escolhidas pelo catálogo autenticado (`/api/mobile/bootstrap`), nunca por GUID digitado. Tokens QR armazenam apenas hash/token opaco e vínculo persistido; a resolução consulta no tenant da sessão e sinaliza inativo, bloqueado ou expirado.

## Checklists e evidências

Modelos ativos exigem itens não vazios. Uma versão aprovada é imutável: alterações criam a próxima versão. A conclusão valida respostas obrigatórias, evidência, observação em não conformidade, assinatura e localização configuradas. Execuções concluídas são preservadas; cancelamento/reprovação exigem motivo.

Fotos e documentos aceitos são JPG, PNG, WebP e PDF, com limite de 10 MB e SHA-256. Metadado não representa upload: tipos de arquivo exigem `storage_reference`. O armazenamento documental definitivo deve ser configurado pelo adaptador de documentos; a PWA mantém o blob apenas na outbox enquanto não houver confirmação.

## Sincronização e conflitos

Cada operação recebe chave idempotente por usuário/tenant. Retentativa consulta a chave antes de materializar e preserva payload, tentativas e erro sanitizado. Divergência de `row_version` cria conflito; não existe política “último a salvar ganha”. Resolver requer decisão, usuário, data e comentário, e gera histórico auditável.

O service worker solicita sincronização quando suportada. Em navegadores sem Background Sync, o cliente envia ao retornar online ou pelo botão **Sincronizar**. Limpar dados do navegador antes da confirmação pode eliminar rascunhos locais; esta limitação é comunicada ao operador.

## CSV

Os relatórios permitidos são checklists, execuções, respostas, evidências, QR Codes, ocorrências, pendências, conflitos, assinaturas, localizações e atividades por usuário/módulo. Filtros são aplicados no SQL junto com tenant e permissão. Documento do assinante, payload offline e conteúdo de arquivo não são exportados.

## Instalação sem Docker

1. Configure `ConnectionStrings__Agro360` para PostgreSQL externo.
2. Aplique `database/agro360-postgres-full.sql` ou `database/migrations/043_sprint43_field_mobile.sql`.
3. Execute API e Web com `dotnet run --project src/Hosts/Agro360.Api` e `dotnet run --project src/Hosts/Agro360.Web`.
4. Abra `/field` em HTTPS (necessário para câmera/geolocalização fora de localhost).

## Pendências reais

- Leitura de QR por câmera depende de `BarcodeDetector`; quando ausente, permanece entrada manual do token impresso, sem leitura simulada.
- ICP-Brasil, carimbo do tempo e validação de certificado exigem provedor homologado e não estão habilitados.
- Push nativo/background permanente varia por navegador; o envio manual e o evento `online` são o fallback real.
- Armazenamento externo exige o provider documental configurado pelo tenant; não há bucket ou arquivo fictício.
