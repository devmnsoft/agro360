# Documentos e Evidências — Sprint 23

## Operação real

O módulo `/Documents` usa a API autenticada e PostgreSQL externo para upload, download protegido, histórico de versões, evidências, dossiês, certificados e CSV. Arquivos não ficam no banco: `Storage:RootPath` define o diretório. O padrão é `App_Data/documents` sob a API; em produção use diretório persistente com leitura/escrita apenas pelo processo. O limite padrão é 25 MB e são aceitos PDF, PNG, JPG, JPEG, WEBP, CSV, TXT, XML, DOCX e XLSX. Nomes físicos são UUIDs, o nome original permanece no banco, SHA-256 é calculado após a gravação e o caminho é confinado à raiz.

## Instalação sem Docker

1. Instale .NET 10 e PostgreSQL 16+ externo.
2. Execute `psql "$AGRO360_CONNECTION_STRING" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`.
3. Configure `ConnectionStrings__Agro360`, `Jwt__SigningKey` (32+ bytes) e, opcionalmente, `Storage__RootPath` e `Storage__MaxFileSizeBytes`.
4. Execute `dotnet run --project src/Hosts/Agro360.Api` e `dotnet run --project src/Hosts/Agro360.Web`.

## Fluxos de homologação

- **Upload/vínculo:** abra Documentos, selecione tipo e uma entidade pelo nome (nunca informe ID), escolha arquivo e envie. Confirme hash, metadados, download e nova versão com motivo.
- **Evidência:** crie pela API vinculando documento; na aba Evidências valide ou rejeite. Rejeição exige motivo e decisões são imutavelmente registradas.
- **Dossiê:** crie com tipo, entidade visual e checklist. Aprovação é bloqueada enquanto houver item obrigatório pendente; reprovação exige motivo e fechamento registra autor/data.
- **Certificado:** emita somente com vínculo pertencente ao tenant. O código público permite consulta anônima somente de organização, assunto, rastreabilidade, validade, status e hash. Revogação exige motivo.
- **Segurança:** teste usuário de outro tenant, download sem `documents.download`, extensão perigosa e path traversal; todos devem ser negados. Logs públicos armazenam apenas hash do endereço remoto.
- **CSV:** use “Exportar CSV” nas quatro listagens. Não há infraestrutura PDF consolidada; PDF de certificado/dossiê permanece pendência futura real.
