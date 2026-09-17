using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agro360.Application.Contracts;
using Agro360.Domain.Compliance;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Npgsql;

namespace Agro360.Infrastructure.Services;

public sealed class InspectionService(DatabaseExecutor db, ITenantContext tenant) : IInspectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string ScheduleTz = "America/Sao_Paulo";

    public Task<IReadOnlyList<InspectionModelListItem>> ListModelsAsync(string? process, string? status, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            var rows = await c.QueryAsync<InspectionModelListItem>(new CommandDefinition("""
                select m.id, m.code, m.name, m.process_code ProcessCode, m.status,
                       coalesce(m.current_published_version, 0) PublishedVersionNumber, m.updated_at UpdatedAt
                from agro360.quality_inspection_models m
                where m.tenant_id=@TenantId and m.deleted_at is null
                  and (@Process is null or m.process_code=@Process)
                  and (@Status is null or m.status=@Status)
                order by m.process_code, m.precedence, m.name
                """, new { tenant.TenantId, Process = NullIfEmpty(process), Status = NullIfEmpty(status) }, t, cancellationToken: ct));
            return (IReadOnlyList<InspectionModelListItem>)rows.ToArray();
        }, ct);

    public Task<InspectionModelDetail> GetModelAsync(Guid id, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            var model = await c.QuerySingleOrDefaultAsync<ModelHeaderRow>(new CommandDefinition("""
                select id, code, name, purpose Description, process_code ProcessCode, status, precedence,
                       allow_manual_selection AllowManualSelection, unit_id UnitId, product_category ProductCategory,
                       product_id ProductId, row_version RowVersion
                from agro360.quality_inspection_models
                where tenant_id=@TenantId and id=@Id and deleted_at is null
                """, new { tenant.TenantId, Id = id }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Modelo de inspeção", id);

            var versions = (await c.QueryAsync<InspectionModelVersionSummary>(new CommandDefinition("""
                select id, version VersionNumber, status, valid_from ValidFrom, valid_until ValidUntil,
                       change_reason ChangeReason, row_version RowVersion, updated_at UpdatedAt
                from agro360.quality_inspection_model_versions
                where tenant_id=@TenantId and model_id=@Id
                order by version desc
                """, new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).ToArray();

            return new InspectionModelDetail(
                model.Id, model.Code, model.Name, model.Description, model.ProcessCode, model.Status,
                model.Precedence, model.AllowManualSelection, model.UnitId, model.ProductCategory,
                model.ProductId, model.RowVersion, versions);
        }, ct);

    public Task<Guid> CreateModelAsync(InspectionModelCreateCommand command, CancellationToken ct)
    {
        InspectionModelRules.EnsureValidProcessCode(command.ProcessCode);
        Guard.Required(command.Code, nameof(command.Code), 40);
        Guard.Required(command.Name, nameof(command.Name), 180);
        if (command.Precedence <= 0) throw new ArgumentException("Precedência deve ser positiva.", nameof(command));

        return db.InTenantTransactionAsync(async (c, t) =>
        {
            var modelId = Guid.CreateVersion7();
            var versionId = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition("""
                insert into agro360.quality_inspection_models(
                    id, tenant_id, code, name, purpose, process_code, product_category, product_id, unit_id,
                    instructions, review_responsible_id, status, allow_manual_selection, precedence,
                    created_by, updated_by)
                values(
                    @ModelId, @TenantId, @Code, @Name, @Description, @ProcessCode, @ProductCategory, @ProductId, @UnitId,
                    @Instructions, @ReviewResponsibleId, 'ACTIVE', @AllowManualSelection, @Precedence,
                    @UserId, @UserId);

                insert into agro360.quality_inspection_model_versions(
                    id, tenant_id, model_id, version, status, valid_from, change_reason,
                    instructions, review_responsible_id, created_by, updated_by)
                values(
                    @VersionId, @TenantId, @ModelId, 1, 'DRAFT', current_date, 'Versão inicial',
                    @Instructions, @ReviewResponsibleId, @UserId, @UserId);

                insert into agro360.quality_inspection_model_audits(
                    id, tenant_id, model_id, version_id, action, before_data, after_data, actor_id)
                values(
                    gen_random_uuid(), @TenantId, @ModelId, @VersionId, 'CREATE_MODEL',
                    '{}'::jsonb, jsonb_build_object('code', @Code, 'processCode', @ProcessCode), @UserId);
                """, new
            {
                ModelId = modelId,
                VersionId = versionId,
                tenant.TenantId,
                command.Code,
                command.Name,
                command.Description,
                ProcessCode = command.ProcessCode.ToUpperInvariant(),
                command.ProductCategory,
                command.ProductId,
                command.UnitId,
                command.Instructions,
                command.ReviewResponsibleId,
                command.AllowManualSelection,
                command.Precedence,
                UserId = tenant.UserId
            }, t, cancellationToken: ct));
            return modelId;
        }, ct);
    }

    public Task<InspectionVersionDetail> GetVersionAsync(Guid versionId, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            var header = await c.QuerySingleOrDefaultAsync<VersionHeaderRow>(new CommandDefinition("""
                select id, model_id ModelId, version VersionNumber, status, valid_from ValidFrom, valid_until ValidUntil,
                       change_reason ChangeReason, instructions, row_version RowVersion
                from agro360.quality_inspection_model_versions
                where tenant_id=@TenantId and id=@Id
                """, new { tenant.TenantId, Id = versionId }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Versão de modelo", versionId);

            var sections = (await c.QueryAsync<SectionDbRow>(new CommandDefinition("""
                select id, code StableKey, name, instructions Description, sequence SortOrder
                from agro360.quality_inspection_model_sections
                where tenant_id=@TenantId and version_id=@VersionId
                order by sequence
                """, new { tenant.TenantId, VersionId = versionId }, t, cancellationToken: ct))).ToArray();

            var criteria = (await c.QueryAsync<CriterionDbRow>(new CommandDefinition("""
                select id, section_id SectionId, stable_key StableKey, name, explanation Guidance, criterion_type CriterionType,
                       required, criticality, allow_not_applicable AllowNotApplicable,
                       not_applicable_requires_justification RequireNaJustification,
                       on_fail_require_review RequireReview, unit, weight, sequence SortOrder,
                       options::text OptionsJson, approval_condition::text ApprovalConditionJson,
                       on_fail_create_nc OnFailCreateNc, on_fail_restriction_type OnFailRestrictionType,
                       on_fail_create_action OnFailCreateAction
                from agro360.quality_inspection_model_criteria
                where tenant_id=@TenantId and version_id=@VersionId
                order by sequence
                """, new { tenant.TenantId, VersionId = versionId }, t, cancellationToken: ct))).ToArray();

            var sectionDtos = sections.Select(s =>
            {
                var crits = criteria.Where(x => x.SectionId == s.Id).Select(cr =>
                    new InspectionCriterionRuntimeDto(
                        cr.Id, cr.StableKey, cr.Name, cr.Guidance, cr.CriterionType, cr.Required,
                        IsCritical(cr.Criticality), cr.AllowNotApplicable, cr.RequireNaJustification,
                        cr.RequireReview, cr.Unit, cr.Weight, cr.SortOrder, cr.ApprovalConditionJson,
                        ParseOptions(cr.OptionsJson), null)).ToArray();
                return new InspectionSectionRuntimeDto(s.Id, s.StableKey, s.Name, s.Description, s.SortOrder, crits);
            }).ToArray();

            return new InspectionVersionDetail(
                header.Id, header.ModelId, header.VersionNumber, header.Status, header.ValidFrom, header.ValidUntil,
                header.ChangeReason, header.Instructions, header.RowVersion, sectionDtos);
        }, ct);

    public Task UpdateModelAsync(Guid id, InspectionModelUpdateCommand command, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            Guard.Required(command.Name, nameof(command.Name), 180);
            if (command.Precedence <= 0) throw new ArgumentException("Precedência deve ser positiva.", nameof(command));

            var current = await c.QuerySingleOrDefaultAsync<(string Status, long RowVersion)>(new CommandDefinition("""
                select status, row_version from agro360.quality_inspection_models
                where tenant_id=@TenantId and id=@Id and deleted_at is null for update
                """, new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            if (current == default) throw new NotFoundException("Modelo de inspeção", id);
            EnsureOcc(command.ExpectedRowVersion, current.RowVersion);

            var n = await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_models
                set name=@Name, purpose=@Description, precedence=@Precedence,
                    allow_manual_selection=@AllowManualSelection, unit_id=@UnitId,
                    product_category=@ProductCategory, product_id=@ProductId,
                    instructions=@Instructions, review_responsible_id=@ReviewResponsibleId,
                    row_version=row_version+1, updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Id and row_version=@ExpectedRowVersion and deleted_at is null
                """, new
            {
                tenant.TenantId,
                Id = id,
                command.Name,
                command.Description,
                command.Precedence,
                command.AllowManualSelection,
                command.UnitId,
                command.ProductCategory,
                command.ProductId,
                command.Instructions,
                command.ReviewResponsibleId,
                command.ExpectedRowVersion,
                UserId = tenant.UserId
            }, t, cancellationToken: ct));
            if (n == 0) throw new ConflictException("O modelo foi alterado por outro usuário. Recarregue e tente novamente.");

            await AuditAsync(c, t, id, null, "UPDATE_MODEL", ct);
        }, ct);

    public Task<Guid> CreateDraftVersionAsync(Guid modelId, Guid? fromVersionId, string? changeReason, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            var model = await c.QuerySingleOrDefaultAsync<(string Status, long RowVersion)>(new CommandDefinition("""
                select status, row_version from agro360.quality_inspection_models
                where tenant_id=@TenantId and id=@ModelId and deleted_at is null for update
                """, new { tenant.TenantId, ModelId = modelId }, t, cancellationToken: ct));
            if (model == default) throw new NotFoundException("Modelo de inspeção", modelId);
            if (model.Status.Equals("INACTIVE", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Não é possível criar versão em modelo inativo.");

            var sourceId = fromVersionId ?? await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
                select id from agro360.quality_inspection_model_versions
                where tenant_id=@TenantId and model_id=@ModelId
                order by case when status='PUBLISHED' then 0 when status='IN_REVIEW' then 1 else 2 end, version desc
                limit 1
                """, new { tenant.TenantId, ModelId = modelId }, t, cancellationToken: ct));

            var nextVersion = await c.ExecuteScalarAsync<int>(new CommandDefinition("""
                select coalesce(max(version),0)+1 from agro360.quality_inspection_model_versions
                where tenant_id=@TenantId and model_id=@ModelId
                """, new { tenant.TenantId, ModelId = modelId }, t, cancellationToken: ct));

            var versionId = Guid.CreateVersion7();
            string? instructions = null;
            Guid? reviewResponsibleId = null;
            if (sourceId is Guid src)
            {
                var header = await c.QuerySingleOrDefaultAsync<(string? Instructions, Guid? ReviewResponsibleId)>(new CommandDefinition("""
                    select instructions, review_responsible_id from agro360.quality_inspection_model_versions
                    where tenant_id=@TenantId and id=@Id and model_id=@ModelId
                    """, new { tenant.TenantId, Id = src, ModelId = modelId }, t, cancellationToken: ct));
                if (header == default) throw new NotFoundException("Versão de modelo", src);
                instructions = header.Instructions;
                reviewResponsibleId = header.ReviewResponsibleId;
            }
            else
            {
                var modelHeader = await c.QuerySingleAsync<(string? Instructions, Guid? ReviewResponsibleId)>(new CommandDefinition("""
                    select instructions, review_responsible_id from agro360.quality_inspection_models
                    where tenant_id=@TenantId and id=@ModelId
                    """, new { tenant.TenantId, ModelId = modelId }, t, cancellationToken: ct));
                instructions = modelHeader.Instructions;
                reviewResponsibleId = modelHeader.ReviewResponsibleId;
            }

            await c.ExecuteAsync(new CommandDefinition("""
                insert into agro360.quality_inspection_model_versions(
                    id, tenant_id, model_id, version, status, valid_from, change_reason,
                    instructions, review_responsible_id, created_by, updated_by)
                values(
                    @VersionId, @TenantId, @ModelId, @VersionNumber, 'DRAFT', current_date, @ChangeReason,
                    @Instructions, @ReviewResponsibleId, @UserId, @UserId)
                """, new
            {
                VersionId = versionId,
                tenant.TenantId,
                ModelId = modelId,
                VersionNumber = nextVersion,
                ChangeReason = changeReason,
                Instructions = instructions,
                ReviewResponsibleId = reviewResponsibleId,
                UserId = tenant.UserId
            }, t, cancellationToken: ct));

            if (sourceId is Guid copyFrom)
                await CopyVersionContentAsync(c, t, copyFrom, versionId, ct);

            await AuditAsync(c, t, modelId, versionId, "CREATE_DRAFT_VERSION", ct);
            return versionId;
        }, ct);

    public Task UpdateDraftVersionAsync(Guid versionId, InspectionDraftVersionUpdateCommand command, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            if (command.Sections is null || command.Sections.Count == 0)
                throw new ArgumentException("Informe ao menos uma seção.", nameof(command));

            var version = await c.QuerySingleOrDefaultAsync<VersionLockRow>(new CommandDefinition("""
                select id, model_id ModelId, status, row_version RowVersion
                from agro360.quality_inspection_model_versions
                where tenant_id=@TenantId and id=@Id for update
                """, new { tenant.TenantId, Id = versionId }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Versão de modelo", versionId);

            InspectionModelRules.EnsureVersionEditable(version.Status);
            EnsureOcc(command.ExpectedRowVersion, version.RowVersion);
            ValidateDraftSections(command.Sections);

            await c.ExecuteAsync(new CommandDefinition("""
                delete from agro360.quality_inspection_model_criteria
                where tenant_id=@TenantId and version_id=@VersionId;
                delete from agro360.quality_inspection_model_sections
                where tenant_id=@TenantId and version_id=@VersionId;
                """, new { tenant.TenantId, VersionId = versionId }, t, cancellationToken: ct));

            await InsertSectionsAsync(c, t, versionId, command.Sections, ct);
            var contentHash = ComputeContentHash(command.Sections);

            var n = await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_model_versions
                set change_reason=coalesce(@ChangeReason, change_reason),
                    valid_from=coalesce(@ValidFrom, valid_from),
                    valid_until=@ValidUntil,
                    content_hash=@ContentHash,
                    row_version=row_version+1,
                    updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@VersionId and row_version=@ExpectedRowVersion and status='DRAFT'
                """, new
            {
                tenant.TenantId,
                VersionId = versionId,
                command.ChangeReason,
                command.ValidFrom,
                command.ValidUntil,
                ContentHash = contentHash,
                command.ExpectedRowVersion,
                UserId = tenant.UserId
            }, t, cancellationToken: ct));
            if (n == 0) throw new ConflictException("A versão foi alterada por outro usuário. Recarregue e tente novamente.");

            await AuditAsync(c, t, version.ModelId, versionId, "UPDATE_DRAFT_VERSION", ct);
        }, ct);

    public Task SubmitForReviewAsync(Guid versionId, long expectedRowVersion, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            var version = await LockVersionAsync(c, t, versionId, ct);
            EnsureOcc(expectedRowVersion, version.RowVersion);
            InspectionModelRules.EnsureVersionTransition(version.Status, "IN_REVIEW");

            var counts = await c.QuerySingleAsync<(int Sections, int Criteria)>(new CommandDefinition("""
                select
                  (select count(*) from agro360.quality_inspection_model_sections where tenant_id=@TenantId and version_id=@Id)::int Sections,
                  (select count(*) from agro360.quality_inspection_model_criteria where tenant_id=@TenantId and version_id=@Id)::int Criteria
                """, new { tenant.TenantId, Id = versionId }, t, cancellationToken: ct));
            if (counts.Sections == 0 || counts.Criteria == 0)
                throw new InvalidOperationException("Envio para revisão exige seções e critérios.");

            var n = await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_model_versions
                set status='IN_REVIEW', row_version=row_version+1, updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Id and row_version=@Expected and status='DRAFT'
                """, new { tenant.TenantId, Id = versionId, Expected = expectedRowVersion, UserId = tenant.UserId }, t, cancellationToken: ct));
            if (n == 0) throw new ConflictException("A versão foi alterada por outro usuário. Recarregue e tente novamente.");
            await AuditAsync(c, t, version.ModelId, versionId, "SUBMIT_REVIEW", ct);
        }, ct);

    public Task PublishVersionAsync(Guid versionId, InspectionPublishVersionCommand command, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            var version = await LockVersionAsync(c, t, versionId, ct);
            EnsureOcc(command.ExpectedRowVersion, version.RowVersion);

            var counts = await c.QuerySingleAsync<(int Sections, int Criteria)>(new CommandDefinition("""
                select
                  (select count(*) from agro360.quality_inspection_model_sections where tenant_id=@TenantId and version_id=@Id)::int Sections,
                  (select count(*) from agro360.quality_inspection_model_criteria where tenant_id=@TenantId and version_id=@Id)::int Criteria
                """, new { tenant.TenantId, Id = versionId }, t, cancellationToken: ct));
            InspectionModelRules.EnsureCanPublish(version.Status, counts.Sections > 0, counts.Criteria > 0, command.ValidFrom);

            await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_model_versions
                set status='SUPERSEDED', superseded_at=now(), superseded_by=@UserId,
                    row_version=row_version+1, updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and model_id=@ModelId and status='PUBLISHED' and id<>@Id
                """, new { tenant.TenantId, ModelId = version.ModelId, Id = versionId, UserId = tenant.UserId }, t, cancellationToken: ct));

            var n = await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_model_versions
                set status='PUBLISHED', valid_from=@ValidFrom, valid_until=@ValidUntil,
                    change_reason=coalesce(@ChangeReason, change_reason),
                    published_at=now(), published_by=@UserId,
                    row_version=row_version+1, updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Id and row_version=@Expected and status='IN_REVIEW'
                """, new
            {
                tenant.TenantId,
                Id = versionId,
                command.ValidFrom,
                command.ValidUntil,
                command.ChangeReason,
                Expected = command.ExpectedRowVersion,
                UserId = tenant.UserId
            }, t, cancellationToken: ct));
            if (n == 0) throw new ConflictException("A versão foi alterada ou já não está em revisão.");

            var versionNumber = await c.ExecuteScalarAsync<int>(new CommandDefinition("""
                select version from agro360.quality_inspection_model_versions where tenant_id=@TenantId and id=@Id
                """, new { tenant.TenantId, Id = versionId }, t, cancellationToken: ct));

            await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_models
                set current_published_version=@VersionNumber, row_version=row_version+1,
                    updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@ModelId and deleted_at is null
                """, new { tenant.TenantId, ModelId = version.ModelId, VersionNumber = versionNumber, UserId = tenant.UserId }, t, cancellationToken: ct));

            await AuditAsync(c, t, version.ModelId, versionId, "PUBLISH_VERSION", ct);
        }, ct);

    public Task InactivateModelAsync(Guid modelId, InspectionInactivateCommand command, CancellationToken ct)
    {
        Guard.Required(command.Reason, nameof(command.Reason), 500);
        return db.InTenantTransactionAsync(async (c, t) =>
        {
            var n = await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_models
                set status='INACTIVE', deletion_reason=@Reason, row_version=row_version+1,
                    updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Id and deleted_at is null and status<>'INACTIVE'
                """, new { tenant.TenantId, Id = modelId, command.Reason, UserId = tenant.UserId }, t, cancellationToken: ct));
            if (n == 0) throw new NotFoundException("Modelo de inspeção", modelId);
            await AuditAsync(c, t, modelId, null, "INACTIVATE_MODEL", ct);
        }, ct);
    }

    public Task InactivateVersionAsync(Guid versionId, InspectionInactivateCommand command, CancellationToken ct)
    {
        Guard.Required(command.Reason, nameof(command.Reason), 500);
        return db.InTenantTransactionAsync(async (c, t) =>
        {
            var version = await LockVersionAsync(c, t, versionId, ct);
            if (version.Status.Equals("PUBLISHED", StringComparison.OrdinalIgnoreCase))
                InspectionModelRules.EnsureVersionTransition(version.Status, "INACTIVE");
            else if (!version.Status.Equals("IN_REVIEW", StringComparison.OrdinalIgnoreCase) &&
                     !version.Status.Equals("DRAFT", StringComparison.OrdinalIgnoreCase) &&
                     !version.Status.Equals("PUBLISHED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Não é possível inativar versão em estado {version.Status}.");

            var n = await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_model_versions
                set status='INACTIVE', change_reason=@Reason, row_version=row_version+1,
                    updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Id and status in ('DRAFT','IN_REVIEW','PUBLISHED')
                """, new { tenant.TenantId, Id = versionId, command.Reason, UserId = tenant.UserId }, t, cancellationToken: ct));
            if (n == 0) throw new ConflictException("A versão não pode ser inativada no estado atual.");

            if (version.Status.Equals("PUBLISHED", StringComparison.OrdinalIgnoreCase))
            {
                await c.ExecuteAsync(new CommandDefinition("""
                    update agro360.quality_inspection_models
                    set current_published_version=null, row_version=row_version+1, updated_at=now(), updated_by=@UserId
                    where tenant_id=@TenantId and id=@ModelId
                      and current_published_version=(select version from agro360.quality_inspection_model_versions where tenant_id=@TenantId and id=@Id)
                    """, new { tenant.TenantId, ModelId = version.ModelId, Id = versionId, UserId = tenant.UserId }, t, cancellationToken: ct));
            }

            await AuditAsync(c, t, version.ModelId, versionId, "INACTIVATE_VERSION", ct);
        }, ct);
    }

    public Task<InspectionModelResolution> ResolveApplicableModelsAsync(InspectionSelectionContext context, CancellationToken ct)
    {
        InspectionModelRules.EnsureValidProcessCode(context.ProcessCode);
        return db.InTenantTransactionAsync(
            (c, t) => ResolveApplicableModelsCoreAsync(c, t, tenant.TenantId, context, ct), ct);
    }

    public Task<PagedResult<InspectionRunListItem>> ListRunsAsync(InspectionRunFilter filters, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            var page = Math.Max(1, filters.Page);
            var pageSize = Math.Clamp(filters.PageSize, 1, 100);
            using var grid = await c.QueryMultipleAsync(new CommandDefinition("""
                select count(*)
                from agro360.quality_inspection_runs r
                where r.tenant_id=@TenantId and r.deleted_at is null
                  and (@ProcessCode is null or r.process_code=@ProcessCode)
                  and (@Status is null or r.status=@Status)
                  and (@OverallResult is null or r.overall_result=@OverallResult)
                  and (@ModelId is null or r.model_id=@ModelId)
                  and (@ProductId is null or r.product_id=@ProductId)
                  and (@LotId is null or r.lot_id=@LotId)
                  and (@UnitId is null or r.unit_id=@UnitId)
                  and (@InspectorId is null or r.inspector_id=@InspectorId)
                  and (@From is null or r.started_at::date>=@From)
                  and (@To is null or r.started_at::date<=@To);

                select r.id, r.number, r.process_code ProcessCode, r.status, r.overall_result OverallResult,
                       m.name ModelName, r.model_version_number ModelVersionNumber,
                       p.name ProductName, l.code LotName, u.name InspectorName,
                       r.started_at StartedAt, r.completed_at CompletedAt, r.last_saved_at LastSavedAt
                from agro360.quality_inspection_runs r
                join agro360.quality_inspection_models m on m.tenant_id=r.tenant_id and m.id=r.model_id
                join agro360.identity_users u on u.tenant_id=r.tenant_id and u.id=r.inspector_id
                left join agro360.inventory_products p on p.tenant_id=r.tenant_id and p.id=r.product_id
                left join agro360.storage_lots l on l.tenant_id=r.tenant_id and l.id=r.lot_id
                where r.tenant_id=@TenantId and r.deleted_at is null
                  and (@ProcessCode is null or r.process_code=@ProcessCode)
                  and (@Status is null or r.status=@Status)
                  and (@OverallResult is null or r.overall_result=@OverallResult)
                  and (@ModelId is null or r.model_id=@ModelId)
                  and (@ProductId is null or r.product_id=@ProductId)
                  and (@LotId is null or r.lot_id=@LotId)
                  and (@UnitId is null or r.unit_id=@UnitId)
                  and (@InspectorId is null or r.inspector_id=@InspectorId)
                  and (@From is null or r.started_at::date>=@From)
                  and (@To is null or r.started_at::date<=@To)
                order by r.started_at desc
                limit @Take offset @Skip
                """, new
            {
                tenant.TenantId,
                ProcessCode = NullIfEmpty(filters.ProcessCode),
                Status = NullIfEmpty(filters.Status),
                OverallResult = NullIfEmpty(filters.OverallResult),
                filters.ModelId,
                filters.ProductId,
                filters.LotId,
                filters.UnitId,
                filters.InspectorId,
                filters.From,
                filters.To,
                Take = pageSize,
                Skip = (page - 1) * pageSize
            }, t, cancellationToken: ct));

            var total = await grid.ReadSingleAsync<long>();
            var items = (await grid.ReadAsync<InspectionRunListItem>()).ToArray();
            return new PagedResult<InspectionRunListItem>(items, page, pageSize, total);
        }, ct);

    public Task<InspectionRunDetail> GetRunAsync(Guid id, CancellationToken ct) =>
        db.InTenantTransactionAsync((c, t) => LoadRunDetailAsync(c, t, tenant.TenantId, id, ct), ct);

    public Task<Guid> StartRunAsync(InspectionStartRunCommand command, CancellationToken ct)
    {
        InspectionModelRules.EnsureValidProcessCode(command.ProcessCode);
        var mode = Guard.Required(command.SelectionMode, nameof(command.SelectionMode), 40).ToUpperInvariant();
        if (mode is not ("AUTOMATIC" or "MANUAL" or "FORCED_CHOICE"))
            throw new ArgumentException("Modo de seleção inválido.", nameof(command));
        Guard.Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 120);

        return db.InTenantTransactionAsync(async (c, t) =>
        {
            var existing = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
                select id from agro360.quality_inspection_runs
                where tenant_id=@TenantId and idempotency_key=@Key and deleted_at is null
                """, new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
            if (existing.HasValue) return existing.Value;

            Guid modelId;
            Guid versionId;
            int versionNumber;
            DateOnly validFrom;
            DateOnly? validUntil;
            string modelStatus;
            string versionStatus;
            bool allowManual;
            string selectionRule;

            if (command.ModelVersionId is Guid explicitVersion)
            {
                var resolved = await c.QuerySingleOrDefaultAsync<StartVersionRow>(new CommandDefinition("""
                    select m.id ModelId, m.status ModelStatus, m.allow_manual_selection AllowManual,
                           v.id VersionId, v.version VersionNumber, v.status VersionStatus,
                           v.valid_from ValidFrom, v.valid_until ValidUntil
                    from agro360.quality_inspection_model_versions v
                    join agro360.quality_inspection_models m on m.tenant_id=v.tenant_id and m.id=v.model_id and m.deleted_at is null
                    where v.tenant_id=@TenantId and v.id=@VersionId
                    """, new { tenant.TenantId, VersionId = explicitVersion }, t, cancellationToken: ct))
                    ?? throw new NotFoundException("Versão de modelo", explicitVersion);

                if (mode == "MANUAL" && !resolved.AllowManual)
                    throw new InvalidOperationException("Seleção manual não permitida para este modelo.");

                modelId = resolved.ModelId;
                versionId = resolved.VersionId;
                versionNumber = resolved.VersionNumber;
                validFrom = resolved.ValidFrom;
                validUntil = resolved.ValidUntil;
                modelStatus = resolved.ModelStatus;
                versionStatus = resolved.VersionStatus;
                allowManual = resolved.AllowManual;
                selectionRule = mode == "FORCED_CHOICE"
                    ? "FORCED_CHOICE após ambiguidade"
                    : mode == "MANUAL" ? "MANUAL" : "Versão explícita";
            }
            else
            {
                var resolution = await ResolveApplicableModelsCoreAsync(
                    c, t, tenant.TenantId,
                    new InspectionSelectionContext(
                        command.ProcessCode, command.UnitId, null, command.ProductId,
                        DateOnly.FromDateTime(DateTime.UtcNow)),
                    ct);

                if (resolution.Ambiguous)
                {
                    if (mode == "AUTOMATIC")
                        throw new ConflictException(resolution.RuleExplanation, "inspection.ambiguous_model");
                    if (mode == "MANUAL" && !resolution.Candidates.Any())
                        throw new ConflictException("Nenhum modelo candidato para seleção manual.");
                    throw new ConflictException(
                        resolution.RuleExplanation + " Use FORCED_CHOICE com ModelVersionId.",
                        "inspection.ambiguous_model");
                }

                if (resolution.RecommendedModelId is null || resolution.RecommendedVersionId is null)
                    throw new InvalidOperationException("Nenhum modelo aplicável para iniciar a inspeção.");

                var resolved = await c.QuerySingleAsync<StartVersionRow>(new CommandDefinition("""
                    select m.id ModelId, m.status ModelStatus, m.allow_manual_selection AllowManual,
                           v.id VersionId, v.version VersionNumber, v.status VersionStatus,
                           v.valid_from ValidFrom, v.valid_until ValidUntil
                    from agro360.quality_inspection_model_versions v
                    join agro360.quality_inspection_models m on m.tenant_id=v.tenant_id and m.id=v.model_id
                    where v.tenant_id=@TenantId and v.id=@VersionId
                    """, new { tenant.TenantId, VersionId = resolution.RecommendedVersionId.Value }, t, cancellationToken: ct));

                if (mode == "MANUAL" && !resolved.AllowManual)
                    throw new InvalidOperationException("Seleção manual não permitida para este modelo.");

                modelId = resolved.ModelId;
                versionId = resolved.VersionId;
                versionNumber = resolved.VersionNumber;
                validFrom = resolved.ValidFrom;
                validUntil = resolved.ValidUntil;
                modelStatus = resolved.ModelStatus;
                versionStatus = resolved.VersionStatus;
                allowManual = resolved.AllowManual;
                selectionRule = resolution.RuleExplanation;
            }

            _ = allowManual;
            InspectionModelRules.EnsureCanStartInspection(
                modelStatus, versionStatus, validFrom, validUntil, DateOnly.FromDateTime(DateTime.UtcNow));

            var inspectorOk = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
                select exists(
                  select 1 from agro360.identity_users
                  where tenant_id=@TenantId and id=@InspectorId and deleted_at is null and status='ACTIVE')
                """, new { tenant.TenantId, command.InspectorId }, t, cancellationToken: ct));
            if (!inspectorOk) throw new DomainException("Inspetor inativo ou inexistente.", "inspection.inspector_inactive");

            var runId = Guid.CreateVersion7();
            var number = await c.ExecuteScalarAsync<string>(new CommandDefinition("""
                select 'INS-'||to_char(now(),'YYYYMMDD')||'-'||lpad(nextval('agro360.quality_inspection_run_number_seq')::text,6,'0')
                """, transaction: t, cancellationToken: ct));

            await c.ExecuteAsync(new CommandDefinition("""
                insert into agro360.quality_inspection_runs(
                    id, tenant_id, number, model_id, model_version_id, model_version_number, process_code,
                    origin_type, origin_id, product_id, lot_id, unit_id, inspector_id, status,
                    selection_rule, selection_mode, idempotency_key, created_by, updated_by)
                values(
                    @Id, @TenantId, @Number, @ModelId, @VersionId, @VersionNumber, @ProcessCode,
                    coalesce(@OriginType,'MANUAL'), @OriginId, @ProductId, @LotId, @UnitId, @InspectorId, 'IN_PROGRESS',
                    @SelectionRule, @SelectionMode, @IdempotencyKey, @UserId, @UserId);

                insert into agro360.quality_inspection_model_audits(
                    id, tenant_id, model_id, version_id, action, before_data, after_data, actor_id)
                values(
                    gen_random_uuid(), @TenantId, @ModelId, @VersionId, 'START_RUN',
                    '{}'::jsonb, jsonb_build_object('runId', @Id::text, 'number', @Number), @UserId);
                """, new
            {
                Id = runId,
                tenant.TenantId,
                Number = number,
                ModelId = modelId,
                VersionId = versionId,
                VersionNumber = versionNumber,
                ProcessCode = command.ProcessCode.ToUpperInvariant(),
                command.OriginType,
                command.OriginId,
                command.ProductId,
                command.LotId,
                command.UnitId,
                command.InspectorId,
                SelectionRule = selectionRule,
                SelectionMode = mode,
                command.IdempotencyKey,
                UserId = tenant.UserId
            }, t, cancellationToken: ct));

            return runId;
        }, ct);
    }

    public Task<InspectionSaveAnswersResult> SaveAnswersAsync(Guid runId, InspectionSaveAnswersCommand command, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            var run = await LockRunAsync(c, t, tenant.TenantId, runId, ct);
            EnsureOcc(command.ExpectedRowVersion, run.RowVersion);
            if (run.Status is not ("IN_PROGRESS" or "PENDING_REVIEW"))
                throw new InvalidOperationException("Somente inspeções em andamento ou em revisão aceitam respostas.");

            var criteria = (await c.QueryAsync<CriterionDbRow>(new CommandDefinition("""
                select id, stable_key StableKey, name, explanation Guidance, criterion_type CriterionType,
                       required, criticality, allow_not_applicable AllowNotApplicable,
                       not_applicable_requires_justification RequireNaJustification,
                       on_fail_require_review RequireReview, unit, weight, sequence SortOrder,
                       options::text OptionsJson, approval_condition::text ApprovalConditionJson,
                       on_fail_create_nc OnFailCreateNc, on_fail_restriction_type OnFailRestrictionType,
                       on_fail_create_action OnFailCreateAction
                from agro360.quality_inspection_model_criteria
                where tenant_id=@TenantId and version_id=@VersionId
                """, new { TenantId = tenant.TenantId, VersionId = run.ModelVersionId }, t, cancellationToken: ct)))
                .ToDictionary(x => x.Id);

            foreach (var answer in command.Answers)
            {
                if (!criteria.TryGetValue(answer.CriterionId, out var criterion))
                    throw new ArgumentException($"Critério {answer.CriterionId} não pertence à versão da inspeção.");

                var definition = ToDefinition(criterion);
                var input = ToAnswerInput(answer);
                InspectionModelRules.EnsureAnswerValid(definition, input);
                var conformity = InspectionModelRules.EvaluateCriterionConformity(definition, input);
                var conforming = conformity.Outcome switch
                {
                    CriterionOutcome.Conforming => (bool?)true,
                    CriterionOutcome.NonConforming => false,
                    _ => null
                };

                await c.ExecuteAsync(new CommandDefinition("""
                    insert into agro360.quality_inspection_answers(
                        id, tenant_id, run_id, criterion_id, stable_key, not_applicable,
                        text_value, number_value, number_unit, date_value, choice_values, pass_fail,
                        evidence_document_id, justification, observation, conforming, answered_by,
                        created_by, updated_by)
                    values(
                        gen_random_uuid(), @TenantId, @RunId, @CriterionId, @StableKey, @IsNotApplicable,
                        @TextValue, @NumberValue, @NumberUnit, @DateValue, cast(@ChoiceValues as jsonb), @PassFail,
                        @DocumentEvidenceId, @NotApplicableJustification, @Notes, @Conforming, @UserId,
                        @UserId, @UserId)
                    on conflict (tenant_id, run_id, criterion_id) do update set
                        not_applicable=excluded.not_applicable,
                        text_value=excluded.text_value,
                        number_value=excluded.number_value,
                        number_unit=excluded.number_unit,
                        date_value=excluded.date_value,
                        choice_values=excluded.choice_values,
                        pass_fail=excluded.pass_fail,
                        evidence_document_id=excluded.evidence_document_id,
                        justification=excluded.justification,
                        observation=excluded.observation,
                        conforming=excluded.conforming,
                        answered_by=excluded.answered_by,
                        recorded_at=now(),
                        updated_at=now(),
                        updated_by=excluded.updated_by
                    """, new
                {
                    TenantId = tenant.TenantId,
                    RunId = runId,
                    answer.CriterionId,
                    criterion.StableKey,
                    answer.IsNotApplicable,
                    answer.TextValue,
                    answer.NumberValue,
                    answer.NumberUnit,
                    answer.DateValue,
                    ChoiceValues = answer.ChoiceValues is null ? null : JsonSerializer.Serialize(answer.ChoiceValues, JsonOptions),
                    PassFail = answer.PassFailValue is null ? null : answer.PassFailValue.Value ? "PASS" : "FAIL",
                    answer.DocumentEvidenceId,
                    answer.NotApplicableJustification,
                    answer.Notes,
                    Conforming = conforming,
                    UserId = tenant.UserId
                }, t, cancellationToken: ct));
            }

            var n = await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_runs
                set last_saved_at=now(), row_version=row_version+1, updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Id and row_version=@Expected
                """, new { TenantId = tenant.TenantId, Id = runId, Expected = command.ExpectedRowVersion, UserId = tenant.UserId }, t, cancellationToken: ct));
            if (n == 0)
                throw new ConflictException("A inspeção foi alterada por outro usuário. Recarregue e tente novamente.");

            var bumped = await c.QuerySingleAsync<(long RowVersion, DateTimeOffset LastSavedAt)>(new CommandDefinition("""
                select row_version, last_saved_at from agro360.quality_inspection_runs
                where tenant_id=@TenantId and id=@Id
                """, new { TenantId = tenant.TenantId, Id = runId }, t, cancellationToken: ct));

            return new InspectionSaveAnswersResult(bumped.RowVersion, bumped.LastSavedAt);
        }, ct);

    public Task<InspectionCompleteRunResult> CompleteRunAsync(Guid runId, InspectionCompleteRunCommand command, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            var run = await LockRunAsync(c, t, tenant.TenantId, runId, ct);
            EnsureOcc(command.ExpectedRowVersion, run.RowVersion);
            if (run.Status is not ("IN_PROGRESS" or "PENDING_REVIEW"))
                throw new InvalidOperationException("Somente inspeções em andamento ou em revisão podem ser concluídas.");

            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey) &&
                run.Status == "COMPLETED")
            {
                return new InspectionCompleteRunResult(
                    run.OverallResult ?? "INCONCLUSIVE",
                    Array.Empty<string>(),
                    run.WeightedScorePercent,
                    run.RowVersion,
                    new InspectionEffectSummary(false, false, ["Replay: inspeção já concluída."]));
            }

            var criteria = (await c.QueryAsync<CriterionDbRow>(new CommandDefinition("""
                select id, stable_key StableKey, name, explanation Guidance, criterion_type CriterionType,
                       required, criticality, allow_not_applicable AllowNotApplicable,
                       not_applicable_requires_justification RequireNaJustification,
                       on_fail_require_review RequireReview, unit, weight, sequence SortOrder,
                       options::text OptionsJson, approval_condition::text ApprovalConditionJson,
                       on_fail_create_nc OnFailCreateNc, on_fail_restriction_type OnFailRestrictionType,
                       on_fail_create_action OnFailCreateAction
                from agro360.quality_inspection_model_criteria
                where tenant_id=@TenantId and version_id=@VersionId
                """, new { TenantId = tenant.TenantId, VersionId = run.ModelVersionId }, t, cancellationToken: ct))).ToArray();

            var answers = (await c.QueryAsync<AnswerDbRow>(new CommandDefinition("""
                select criterion_id CriterionId, stable_key StableKey, not_applicable IsNotApplicable,
                       justification NotApplicableJustification, pass_fail PassFail, text_value TextValue,
                       number_value NumberValue, number_unit NumberUnit, date_value DateValue,
                       choice_values::text ChoiceValuesJson, evidence_document_id DocumentEvidenceId,
                       observation Notes, conforming
                from agro360.quality_inspection_answers
                where tenant_id=@TenantId and run_id=@RunId
                """, new { TenantId = tenant.TenantId, RunId = runId }, t, cancellationToken: ct)))
                .ToDictionary(x => x.CriterionId);

            var states = new List<InspectionCriterionAnswerState>();
            var evaluated = new List<(CriterionDbRow Criterion, CriterionOutcome Outcome)>();
            foreach (var criterion in criteria)
            {
                answers.TryGetValue(criterion.Id, out var answer);
                var definition = ToDefinition(criterion);
                var input = answer is null
                    ? new InspectionAnswerInput()
                    : ToAnswerInputFromDb(answer);
                if (answer is not null)
                    InspectionModelRules.EnsureAnswerValid(definition, input);
                else if (criterion.Required)
                    InspectionModelRules.EnsureAnswerValid(definition, input);

                var result = answer is null
                    ? new CriterionConformityResult(CriterionOutcome.Unanswered, "Sem resposta.")
                    : InspectionModelRules.EvaluateCriterionConformity(definition, input);
                states.Add(new InspectionCriterionAnswerState(
                    criterion.StableKey, criterion.Required, IsCritical(criterion.Criticality),
                    criterion.RequireReview, result.Outcome, criterion.Weight));
                evaluated.Add((Criterion: criterion, Outcome: result.Outcome));
            }

            var overall = InspectionModelRules.ComputeOverallResult(states);
            var determiningJson = JsonSerializer.Serialize(overall.DeterminingStableKeys, JsonOptions);

            var effectMessages = new List<string>();
            var ncSuggested = false;
            var lotHold = false;

            if (overall.Result == "NON_CONFORMING")
            {
                foreach (var (criterion, outcome) in evaluated.Where(x => x.Outcome == CriterionOutcome.NonConforming))
                {
                    if (criterion.OnFailCreateNc)
                    {
                        var applied = await ApplyNcEffectAsync(c, t, run, criterion, ct);
                        if (applied)
                        {
                            ncSuggested = true;
                            effectMessages.Add($"NC criada para {criterion.StableKey}.");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(criterion.OnFailRestrictionType) && run.LotId.HasValue)
                    {
                        var applied = await ApplyRestrictionEffectAsync(c, t, run, criterion, ct);
                        if (applied)
                        {
                            lotHold = true;
                            effectMessages.Add($"Restrição {criterion.OnFailRestrictionType} aplicada ao lote.");
                        }
                    }

                    if (criterion.OnFailCreateAction)
                    {
                        var applied = await ApplyActionEffectAsync(c, t, run, criterion, ct);
                        if (applied) effectMessages.Add($"Ação imediata criada para {criterion.StableKey}.");
                    }

                    if (criterion.RequireReview)
                        await EnsureEffectAsync(c, t, run.Id, criterion.Id, "REVIEW", null, null, null, "PENDING", ct);
                }
            }
            else if (overall.Result == "INCONCLUSIVE")
            {
                foreach (var (criterion, outcome) in evaluated.Where(x =>
                             x.Criterion.RequireReview &&
                             x.Outcome is CriterionOutcome.Inconclusive or CriterionOutcome.Unanswered))
                {
                    await EnsureEffectAsync(c, t, run.Id, criterion.Id, "REVIEW", null, null, null, "PENDING", ct);
                    effectMessages.Add($"Revisão pendente para {criterion.StableKey}.");
                }
            }

            var n = await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_runs
                set status='COMPLETED', overall_result=@Result, result_summary=@Summary,
                    determining_criteria=cast(@Determining as jsonb),
                    weighted_score_percent=@Score,
                    completed_at=now(), completed_by=@UserId,
                    row_version=row_version+1, updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Id and row_version=@Expected
                  and status in ('IN_PROGRESS','PENDING_REVIEW')
                """, new
            {
                TenantId = tenant.TenantId,
                Id = runId,
                Result = overall.Result,
                Summary = overall.ScoringNote,
                Determining = determiningJson,
                Score = overall.WeightedScorePercent,
                Expected = command.ExpectedRowVersion,
                UserId = tenant.UserId
            }, t, cancellationToken: ct));
            if (n == 0) throw new ConflictException("A inspeção foi alterada durante a conclusão.");

            var newVersion = command.ExpectedRowVersion + 1;
            await AuditAsync(c, t, run.ModelId, run.ModelVersionId, "COMPLETE_RUN", ct);

            return new InspectionCompleteRunResult(
                overall.Result,
                overall.DeterminingStableKeys,
                overall.WeightedScorePercent,
                newVersion,
                new InspectionEffectSummary(ncSuggested, lotHold, effectMessages));
        }, ct);

    public Task CancelRunAsync(Guid runId, InspectionCancelRunCommand command, CancellationToken ct)
    {
        Guard.Required(command.Reason, nameof(command.Reason), 500);
        return db.InTenantTransactionAsync(async (c, t) =>
        {
            var run = await LockRunAsync(c, t, tenant.TenantId, runId, ct);
            EnsureOcc(command.ExpectedRowVersion, run.RowVersion);
            if (run.Status is "COMPLETED" or "CANCELLED")
                throw new InvalidOperationException("Inspeção já finalizada não pode ser cancelada.");

            var n = await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_runs
                set status='CANCELLED', cancellation_reason=@Reason, cancelled_at=now(), cancelled_by=@UserId,
                    row_version=row_version+1, updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Id and row_version=@Expected
                  and status in ('IN_PROGRESS','PENDING_REVIEW')
                """, new
            {
                TenantId = tenant.TenantId,
                Id = runId,
                command.Reason,
                Expected = command.ExpectedRowVersion,
                UserId = tenant.UserId
            }, t, cancellationToken: ct));
            if (n == 0) throw new ConflictException("A inspeção foi alterada durante o cancelamento.");
            await AuditAsync(c, t, run.ModelId, run.ModelVersionId, "CANCEL_RUN", ct);
        }, ct);
    }

    public Task<Guid> CreateReinspectionAsync(Guid parentRunId, InspectionReinspectionCommand command, CancellationToken ct)
    {
        Guard.Required(command.Reason, nameof(command.Reason), 500);
        Guard.Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 120);
        return db.InTenantTransactionAsync(async (c, t) =>
        {
            var existing = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
                select id from agro360.quality_inspection_runs
                where tenant_id=@TenantId and idempotency_key=@Key and deleted_at is null
                """, new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
            if (existing.HasValue) return existing.Value;

            var parent = await LockRunAsync(c, t, tenant.TenantId, parentRunId, ct);
            InspectionModelRules.EnsureReinspection(parent.Status == "COMPLETED", command.Reason);

            Guid versionId = parent.ModelVersionId;
            int versionNumber = parent.ModelVersionNumber;
            if (command.UseLatestPublishedVersion)
            {
                var latest = await c.QuerySingleOrDefaultAsync<(Guid Id, int Version)>(new CommandDefinition("""
                    select id, version from agro360.quality_inspection_model_versions
                    where tenant_id=@TenantId and model_id=@ModelId and status='PUBLISHED'
                    """, new { tenant.TenantId, ModelId = parent.ModelId }, t, cancellationToken: ct));
                if (latest != default)
                {
                    versionId = latest.Id;
                    versionNumber = latest.Version;
                }
            }

            var runId = Guid.CreateVersion7();
            var number = await c.ExecuteScalarAsync<string>(new CommandDefinition("""
                select 'INS-'||to_char(now(),'YYYYMMDD')||'-'||lpad(nextval('agro360.quality_inspection_run_number_seq')::text,6,'0')
                """, transaction: t, cancellationToken: ct));

            await c.ExecuteAsync(new CommandDefinition("""
                insert into agro360.quality_inspection_runs(
                    id, tenant_id, number, model_id, model_version_id, model_version_number, process_code,
                    origin_type, origin_id, origin_label, product_id, lot_id, unit_id, inspector_id, status,
                    selection_rule, selection_mode, parent_run_id, reinspection_reason,
                    idempotency_key, created_by, updated_by)
                values(
                    @Id, @TenantId, @Number, @ModelId, @VersionId, @VersionNumber, @ProcessCode,
                    @OriginType, @OriginId, @OriginLabel, @ProductId, @LotId, @UnitId, @InspectorId, 'IN_PROGRESS',
                    @SelectionRule, @SelectionMode, @ParentId, @Reason,
                    @IdempotencyKey, @UserId, @UserId)
                """, new
            {
                Id = runId,
                tenant.TenantId,
                Number = number,
                ModelId = parent.ModelId,
                VersionId = versionId,
                VersionNumber = versionNumber,
                parent.ProcessCode,
                OriginType = parent.OriginType,
                OriginId = parent.OriginId,
                OriginLabel = $"REINSPECAO:{parent.Number}",
                parent.ProductId,
                parent.LotId,
                parent.UnitId,
                InspectorId = parent.InspectorId,
                SelectionRule = "REINSPECTION",
                SelectionMode = parent.SelectionMode,
                ParentId = parentRunId,
                command.Reason,
                command.IdempotencyKey,
                UserId = tenant.UserId
            }, t, cancellationToken: ct));

            await AuditAsync(c, t, parent.ModelId, versionId, "CREATE_REINSPECTION", ct);
            return runId;
        }, ct);
    }

    public Task<InspectionRunCompareResult> CompareRunsAsync(Guid runId, Guid otherRunId, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            var left = await LoadCompareSideAsync(c, t, tenant.TenantId, runId, ct);
            var right = await LoadCompareSideAsync(c, t, tenant.TenantId, otherRunId, ct);

            var items = new List<InspectionRunCompareItem>();
            var rightByKey = right.ToDictionary(x => x.StableKey, StringComparer.OrdinalIgnoreCase);
            foreach (var l in left)
            {
                if (!rightByKey.TryGetValue(l.StableKey, out var r))
                {
                    items.Add(new InspectionRunCompareItem(
                        l.StableKey, l.CriterionType, l.Name, l.Outcome, null, l.ValueSummary, null, false));
                    continue;
                }

                var meaningMatches =
                    l.CriterionType.Equals(r.CriterionType, StringComparison.OrdinalIgnoreCase) &&
                    l.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase);
                items.Add(new InspectionRunCompareItem(
                    l.StableKey, l.CriterionType, l.Name, l.Outcome, r.Outcome,
                    l.ValueSummary, r.ValueSummary, meaningMatches));
            }

            foreach (var r in right.Where(x => left.All(l => !l.StableKey.Equals(x.StableKey, StringComparison.OrdinalIgnoreCase))))
            {
                items.Add(new InspectionRunCompareItem(
                    r.StableKey, r.CriterionType, r.Name, null, r.Outcome, null, r.ValueSummary, false));
            }

            return new InspectionRunCompareResult(runId, otherRunId, items);
        }, ct);

    public Task<IReadOnlyList<InspectionScheduleListItem>> ListSchedulesAsync(CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            var rows = await c.QueryAsync<InspectionScheduleListItem>(new CommandDefinition("""
                select s.id, s.name, s.process_code ProcessCode, s.model_id ModelId, m.name ModelName,
                       s.schedule_type Cadence, s.status,
                       (s.next_run_at at time zone @Tz)::date NextDueOn,
                       s.updated_at UpdatedAt
                from agro360.quality_inspection_schedules s
                join agro360.quality_inspection_models m on m.tenant_id=s.tenant_id and m.id=s.model_id
                where s.tenant_id=@TenantId and s.deleted_at is null
                order by s.name
                """, new { tenant.TenantId, Tz = ScheduleTz }, t, cancellationToken: ct));
            return (IReadOnlyList<InspectionScheduleListItem>)rows.ToArray();
        }, ct);

    public Task<Guid> SaveScheduleAsync(Guid? id, InspectionScheduleCommand command, CancellationToken ct)
    {
        Guard.Required(command.Name, nameof(command.Name), 180);
        InspectionModelRules.EnsureValidProcessCode(command.ProcessCode);
        var cadence = Guard.Required(command.Cadence, nameof(command.Cadence), 40).ToUpperInvariant();
        if (cadence is not ("ONCE" or "PERIODIC" or "EVENT"))
            throw new ArgumentException("Cadência inválida.", nameof(command));
        if (cadence == "PERIODIC" && (!command.IntervalDays.HasValue || command.IntervalDays <= 0))
            throw new ArgumentException("PERIODIC exige interval_days > 0.", nameof(command));
        if (cadence == "EVENT" && string.IsNullOrWhiteSpace(command.EventCode))
            throw new ArgumentException("EVENT exige event_code.", nameof(command));

        return db.InTenantTransactionAsync(async (c, t) =>
        {
            var modelExists = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
                select exists(select 1 from agro360.quality_inspection_models
                  where tenant_id=@TenantId and id=@ModelId and deleted_at is null)
                """, new { tenant.TenantId, command.ModelId }, t, cancellationToken: ct));
            if (!modelExists) throw new NotFoundException("Modelo de inspeção", command.ModelId);

            DateTimeOffset? nextRunAt = null;
            if (cadence != "EVENT")
            {
                var start = command.StartsOn ?? DateOnly.FromDateTime(DateTime.UtcNow);
                nextRunAt = ToScheduleTimestamp(start);
            }

            if (id is Guid scheduleId)
            {
                var current = await c.QuerySingleOrDefaultAsync<long?>(new CommandDefinition("""
                    select row_version from agro360.quality_inspection_schedules
                    where tenant_id=@TenantId and id=@Id and deleted_at is null for update
                    """, new { tenant.TenantId, Id = scheduleId }, t, cancellationToken: ct));
                if (current is null) throw new NotFoundException("Agenda de inspeção", scheduleId);
                if (command.ExpectedRowVersion.HasValue)
                    EnsureOcc(command.ExpectedRowVersion.Value, current.Value);

                var n = await c.ExecuteAsync(new CommandDefinition("""
                    update agro360.quality_inspection_schedules
                    set name=@Name, model_id=@ModelId, process_code=@ProcessCode, schedule_type=@Cadence,
                        interval_days=@IntervalDays, event_code=@EventCode, unit_id=@UnitId,
                        product_category=@ProductCategory, product_id=@ProductId,
                        starts_on=@StartsOn, ends_on=@EndsOn, responsible_id=@ResponsibleId,
                        notes=@Notes, allow_catchup=@AllowCatchUp, next_run_at=coalesce(@NextRunAt, next_run_at),
                        row_version=row_version+1, updated_at=now(), updated_by=@UserId
                    where tenant_id=@TenantId and id=@Id and deleted_at is null
                      and (@Expected is null or row_version=@Expected)
                    """, new
                {
                    tenant.TenantId,
                    Id = scheduleId,
                    command.Name,
                    command.ModelId,
                    ProcessCode = command.ProcessCode.ToUpperInvariant(),
                    Cadence = cadence,
                    command.IntervalDays,
                    command.EventCode,
                    command.UnitId,
                    command.ProductCategory,
                    command.ProductId,
                    command.StartsOn,
                    command.EndsOn,
                    command.ResponsibleId,
                    command.Notes,
                    command.AllowCatchUp,
                    NextRunAt = nextRunAt,
                    Expected = command.ExpectedRowVersion,
                    UserId = tenant.UserId
                }, t, cancellationToken: ct));
                if (n == 0) throw new ConflictException("A agenda foi alterada por outro usuário.");
                return scheduleId;
            }

            var newId = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition("""
                insert into agro360.quality_inspection_schedules(
                    id, tenant_id, name, model_id, process_code, schedule_type, timezone,
                    interval_days, event_code, responsible_id, unit_id, product_id, product_category,
                    notes, starts_on, ends_on, status, next_run_at, allow_catchup, created_by, updated_by)
                values(
                    @Id, @TenantId, @Name, @ModelId, @ProcessCode, @Cadence, @Tz,
                    @IntervalDays, @EventCode, @ResponsibleId, @UnitId, @ProductId, @ProductCategory,
                    @Notes, @StartsOn, @EndsOn, 'ACTIVE', @NextRunAt, @AllowCatchUp, @UserId, @UserId)
                """, new
            {
                Id = newId,
                tenant.TenantId,
                command.Name,
                command.ModelId,
                ProcessCode = command.ProcessCode.ToUpperInvariant(),
                Cadence = cadence,
                Tz = ScheduleTz,
                command.IntervalDays,
                command.EventCode,
                command.ResponsibleId,
                command.UnitId,
                command.ProductId,
                command.ProductCategory,
                command.Notes,
                command.StartsOn,
                command.EndsOn,
                NextRunAt = nextRunAt,
                command.AllowCatchUp,
                UserId = tenant.UserId
            }, t, cancellationToken: ct));
            return newId;
        }, ct);
    }

    public Task InactivateScheduleAsync(Guid id, InspectionScheduleInactivateCommand command, CancellationToken ct)
    {
        Guard.Required(command.Reason, nameof(command.Reason), 500);
        return db.InTenantTransactionAsync(async (c, t) =>
        {
            var n = await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_schedules
                set status='INACTIVE', deletion_reason=@Reason, row_version=row_version+1,
                    updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Id and deleted_at is null and status<>'INACTIVE'
                """, new { tenant.TenantId, Id = id, command.Reason, UserId = tenant.UserId }, t, cancellationToken: ct));
            if (n == 0) throw new NotFoundException("Agenda de inspeção", id);
        }, ct);
    }

    public Task<InspectionDueScheduleGenerationResult> GenerateDueSchedulesAsync(DateOnly asOf, CancellationToken ct) =>
        GenerateDueSchedulesCoreAsync(tenant.TenantId, asOf, tenant.UserId, ct);

    public Task<InspectionDueScheduleGenerationResult> GenerateDueSchedulesForTenantAsync(
        Guid tenantId, DateOnly asOf, CancellationToken ct) =>
        GenerateDueSchedulesCoreAsync(tenantId, asOf, actorId: null, ct);

    private Task<InspectionDueScheduleGenerationResult> GenerateDueSchedulesCoreAsync(
        Guid tenantId, DateOnly asOf, Guid? actorId, CancellationToken ct) =>
        db.InTenantTransactionAsync(tenantId, async (c, t) =>
        {
            var due = (await c.QueryAsync<ScheduleDueRow>(new CommandDefinition("""
                select s.id, s.name, s.model_id ModelId, s.process_code ProcessCode, s.schedule_type ScheduleType,
                       s.interval_days IntervalDays, s.allow_catchup AllowCatchUp, s.next_run_at NextRunAt,
                       s.responsible_id ResponsibleId, s.unit_id UnitId, s.product_id ProductId,
                       s.product_category ProductCategory, s.created_by CreatedBy, s.ends_on EndsOn,
                       v.id VersionId, v.version VersionNumber,
                       case when u.id is null then false when u.status='ACTIVE' and u.deleted_at is null then true else false end ResponsibleActive
                from agro360.quality_inspection_schedules s
                join agro360.quality_inspection_models m
                  on m.tenant_id=s.tenant_id and m.id=s.model_id and m.deleted_at is null and m.status='ACTIVE'
                join agro360.quality_inspection_model_versions v
                  on v.tenant_id=m.tenant_id and v.model_id=m.id and v.status='PUBLISHED'
                left join agro360.identity_users u
                  on u.tenant_id=s.tenant_id and u.id=s.responsible_id
                where s.tenant_id=@TenantId and s.deleted_at is null and s.status='ACTIVE'
                  and s.schedule_type in ('ONCE','PERIODIC')
                  and s.next_run_at is not null
                  and s.next_run_at <= ((@AsOf::text || ' 23:59:59.999')::timestamp at time zone @Tz)
                  and (s.ends_on is null or s.ends_on >= @AsOf)
                for update of s skip locked
                """, new { TenantId = tenantId, AsOf = asOf, Tz = ScheduleTz }, t, cancellationToken: ct))).ToArray();

            var generated = 0;
            var skipped = 0;
            var runIds = new List<Guid>();

            foreach (var schedule in due)
            {
                var slot = schedule.NextRunAt!.Value;
                var asOfEnd = ToScheduleTimestamp(asOf).AddDays(1).AddTicks(-1);

                if (!schedule.AllowCatchUp)
                {
                    while (schedule.ScheduleType == "PERIODIC" &&
                           schedule.IntervalDays is int days && days > 0 &&
                           slot <= asOfEnd)
                    {
                        var candidate = slot.AddDays(days);
                        if (candidate > asOfEnd)
                            break;
                        slot = candidate;
                    }
                }

                var generationKey = slot.ToOffset(TimeSpan.Zero).ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
                // Prefer local wall-clock key from DB timezone
                generationKey = await c.ExecuteScalarAsync<string>(new CommandDefinition("""
                    select to_char((@Slot at time zone @Tz), 'YYYYMMDDHH24MI')
                    """, new { Slot = slot, Tz = ScheduleTz }, t, cancellationToken: ct)) ?? generationKey;

                var exists = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
                    select exists(
                      select 1 from agro360.quality_inspection_runs
                      where tenant_id=@TenantId and schedule_id=@ScheduleId and schedule_generation_key=@Key)
                    """, new { TenantId = tenantId, ScheduleId = schedule.Id, Key = generationKey }, t, cancellationToken: ct));

                if (exists)
                {
                    skipped++;
                }
                else
                {
                    var inspectorId = schedule.ResponsibleId;
                    var originLabel = schedule.Name;
                    var createdBy = actorId ?? schedule.CreatedBy ?? schedule.ResponsibleId;

                    if (schedule.ResponsibleId is Guid responsible)
                    {
                        if (!schedule.ResponsibleActive)
                        {
                            inspectorId = createdBy ?? responsible;
                            originLabel = "PENDENCIA_ATRIBUICAO: usuario inativo";
                        }
                    }
                    else
                    {
                        inspectorId = createdBy;
                    }

                    if (inspectorId is null)
                    {
                        skipped++;
                        await AdvanceScheduleAsync(c, t, tenantId, schedule, slot, asOfEnd, ct);
                        continue;
                    }

                    var runId = Guid.CreateVersion7();
                    var number = await c.ExecuteScalarAsync<string>(new CommandDefinition("""
                        select 'INS-'||to_char(now(),'YYYYMMDD')||'-'||lpad(nextval('agro360.quality_inspection_run_number_seq')::text,6,'0')
                        """, transaction: t, cancellationToken: ct));

                    await c.ExecuteAsync(new CommandDefinition("""
                        insert into agro360.quality_inspection_runs(
                            id, tenant_id, number, model_id, model_version_id, model_version_number, process_code,
                            origin_type, origin_label, product_id, unit_id, inspector_id, status,
                            selection_rule, selection_mode, schedule_id, schedule_generation_key,
                            created_by, updated_by)
                        values(
                            @Id, @TenantId, @Number, @ModelId, @VersionId, @VersionNumber, @ProcessCode,
                            'SCHEDULE', @OriginLabel, @ProductId, @UnitId, @InspectorId, 'IN_PROGRESS',
                            'SCHEDULE', 'AUTOMATIC', @ScheduleId, @GenerationKey,
                            @CreatedBy, @CreatedBy)
                        """, new
                    {
                        Id = runId,
                        TenantId = tenantId,
                        Number = number,
                        schedule.ModelId,
                        VersionId = schedule.VersionId,
                        VersionNumber = schedule.VersionNumber,
                        schedule.ProcessCode,
                        OriginLabel = originLabel,
                        schedule.ProductId,
                        schedule.UnitId,
                        InspectorId = inspectorId.Value,
                        ScheduleId = schedule.Id,
                        GenerationKey = generationKey,
                        CreatedBy = createdBy ?? inspectorId.Value
                    }, t, cancellationToken: ct));

                    generated++;
                    runIds.Add(runId);
                }

                await AdvanceScheduleAsync(c, t, tenantId, schedule, slot, asOfEnd, ct);
            }

            return new InspectionDueScheduleGenerationResult(generated, skipped, runIds);
        }, ct);

    // --- helpers ---

    private static async Task AdvanceScheduleAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid tenantId, ScheduleDueRow schedule,
        DateTimeOffset generatedSlot, DateTimeOffset asOfEnd, CancellationToken ct)
    {
        DateTimeOffset? next = null;
        string? status = null;
        if (schedule.ScheduleType == "ONCE")
        {
            status = "INACTIVE";
            next = null;
        }
        else if (schedule.ScheduleType == "PERIODIC" && schedule.IntervalDays is int days && days > 0)
        {
            next = generatedSlot.AddDays(days);
            if (!schedule.AllowCatchUp)
            {
                while (next <= asOfEnd)
                    next = next.Value.AddDays(days);
            }

            if (schedule.EndsOn.HasValue && DateOnly.FromDateTime(next.Value.UtcDateTime) > schedule.EndsOn.Value)
            {
                status = "INACTIVE";
                next = null;
            }
        }

        await c.ExecuteAsync(new CommandDefinition("""
            update agro360.quality_inspection_schedules
            set next_run_at=@Next,
                status=coalesce(@Status, status),
                last_generated_at=now(),
                last_generation_key=to_char((@Slot at time zone @Tz), 'YYYYMMDDHH24MI'),
                row_version=row_version+1,
                updated_at=now()
            where tenant_id=@TenantId and id=@Id
            """, new
        {
            TenantId = tenantId,
            Id = schedule.Id,
            Next = next,
            Status = status,
            Slot = generatedSlot,
            Tz = ScheduleTz
        }, t, cancellationToken: ct));
    }

    private static async Task<InspectionModelResolution> ResolveApplicableModelsCoreAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid tenantId, InspectionSelectionContext context, CancellationToken ct)
    {
        var rows = (await c.QueryAsync<CandidateRow>(new CommandDefinition("""
            select m.id ModelId, m.code, m.name, m.precedence, m.allow_manual_selection AllowManualSelection,
                   m.product_id ProductId, m.product_category ProductCategory, m.unit_id UnitId,
                   v.id VersionId, v.version VersionNumber, v.valid_from ValidFrom, v.valid_until ValidUntil
            from agro360.quality_inspection_models m
            join agro360.quality_inspection_model_versions v
              on v.tenant_id=m.tenant_id and v.model_id=m.id and v.status='PUBLISHED'
            where m.tenant_id=@TenantId and m.deleted_at is null and m.status='ACTIVE'
              and m.process_code=@ProcessCode
              and v.valid_from<=@ReferenceDate
              and (v.valid_until is null or v.valid_until>=@ReferenceDate)
              and (m.product_id is null or @ProductId is null or m.product_id=@ProductId)
              and (m.product_category is null or @ProductCategory is null or lower(m.product_category)=lower(@ProductCategory))
              and (m.unit_id is null or @UnitId is null or m.unit_id=@UnitId)
            """, new
        {
            TenantId = tenantId,
            ProcessCode = context.ProcessCode.ToUpperInvariant(),
            context.ReferenceDate,
            context.ProductId,
            context.ProductCategory,
            context.UnitId
        }, t, cancellationToken: ct))).ToArray();

        var candidates = new List<InspectionModelCandidate>();
        foreach (var row in rows)
        {
            var score = 0;
            var parts = new List<string> { "processo" };
            if (row.ProductId.HasValue && context.ProductId.HasValue && row.ProductId == context.ProductId)
            {
                score += 4;
                parts.Add("produto");
            }
            else if (row.ProductId.HasValue)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.ProductCategory) &&
                !string.IsNullOrWhiteSpace(context.ProductCategory) &&
                row.ProductCategory.Equals(context.ProductCategory, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
                parts.Add("categoria");
            }
            else if (!string.IsNullOrWhiteSpace(row.ProductCategory) &&
                     (string.IsNullOrWhiteSpace(context.ProductCategory) ||
                      !row.ProductCategory.Equals(context.ProductCategory, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (row.UnitId.HasValue && context.UnitId.HasValue && row.UnitId == context.UnitId)
            {
                score += 1;
                parts.Add("unidade");
            }
            else if (row.UnitId.HasValue)
            {
                continue;
            }

            candidates.Add(new InspectionModelCandidate(
                row.ModelId, row.Code, row.Name, row.VersionId, row.VersionNumber,
                row.Precedence, score, $"Match: {string.Join("+", parts)} (score {score})"));
        }

        var selection = InspectionModelRules.SelectApplicableModel(
            candidates.Select(x => new ModelSelectionCandidate(x.ModelId, x.Precedence, x.SpecificityScore)));

        if (selection.Ambiguous)
        {
            var allowManual = rows.Any(r => selection.TiedModelIds.Contains(r.ModelId) && r.AllowManualSelection);
            if (!allowManual)
            {
                return new InspectionModelResolution(
                    candidates, null, null, true,
                    selection.Explanation + " Seleção manual não permitida pelos modelos empatados.");
            }
        }

        Guid? recommendedVersion = null;
        if (selection.SelectedModelId is Guid selected)
            recommendedVersion = candidates.First(x => x.ModelId == selected).VersionId;

        return new InspectionModelResolution(
            candidates,
            selection.SelectedModelId,
            recommendedVersion,
            selection.Ambiguous,
            selection.Explanation);
    }

    private static async Task<InspectionRunDetail> LoadRunDetailAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid tenantId, Guid id, CancellationToken ct)
    {
        var run = await c.QuerySingleOrDefaultAsync<RunDetailRow>(new CommandDefinition("""
            select r.id, r.number, r.process_code ProcessCode, r.status, r.overall_result OverallResult,
                   r.determining_criteria::text DeterminingJson, r.weighted_score_percent WeightedScorePercent,
                   r.model_id ModelId, r.model_version_id ModelVersionId, m.name ModelName,
                   r.model_version_number ModelVersionNumber, r.origin_type OriginType, r.origin_id OriginId,
                   r.product_id ProductId, p.name ProductName, r.lot_id LotId, l.code LotName,
                   r.unit_id UnitId, null::text UnitName, r.inspector_id InspectorId, u.name InspectorName,
                   r.selection_mode SelectionMode, r.parent_run_id ParentRunId, r.reinspection_reason ReinspectionReason,
                   r.row_version RowVersion, r.started_at StartedAt, r.last_saved_at LastSavedAt, r.completed_at CompletedAt
            from agro360.quality_inspection_runs r
            join agro360.quality_inspection_models m on m.tenant_id=r.tenant_id and m.id=r.model_id
            join agro360.identity_users u on u.tenant_id=r.tenant_id and u.id=r.inspector_id
            left join agro360.inventory_products p on p.tenant_id=r.tenant_id and p.id=r.product_id
            left join agro360.storage_lots l on l.tenant_id=r.tenant_id and l.id=r.lot_id
            where r.tenant_id=@TenantId and r.id=@Id and r.deleted_at is null
            """, new { TenantId = tenantId, Id = id }, t, cancellationToken: ct))
            ?? throw new NotFoundException("Inspeção", id);

        var sections = (await c.QueryAsync<SectionDbRow>(new CommandDefinition("""
            select id, code StableKey, name, instructions Description, sequence SortOrder
            from agro360.quality_inspection_model_sections
            where tenant_id=@TenantId and version_id=@VersionId
            order by sequence
            """, new { TenantId = tenantId, VersionId = run.ModelVersionId }, t, cancellationToken: ct))).ToArray();

        var criteria = (await c.QueryAsync<CriterionDbRow>(new CommandDefinition("""
            select id, section_id SectionId, stable_key StableKey, name, explanation Guidance, criterion_type CriterionType,
                   required, criticality, allow_not_applicable AllowNotApplicable,
                   not_applicable_requires_justification RequireNaJustification,
                   on_fail_require_review RequireReview, unit, weight, sequence SortOrder,
                   options::text OptionsJson, approval_condition::text ApprovalConditionJson,
                   on_fail_create_nc OnFailCreateNc, on_fail_restriction_type OnFailRestrictionType,
                   on_fail_create_action OnFailCreateAction
            from agro360.quality_inspection_model_criteria
            where tenant_id=@TenantId and version_id=@VersionId
            order by sequence
            """, new { TenantId = tenantId, VersionId = run.ModelVersionId }, t, cancellationToken: ct))).ToArray();

        var answers = (await c.QueryAsync<AnswerDbRow>(new CommandDefinition("""
            select criterion_id CriterionId, stable_key StableKey, not_applicable IsNotApplicable,
                   justification NotApplicableJustification, pass_fail PassFail, text_value TextValue,
                   number_value NumberValue, number_unit NumberUnit, date_value DateValue,
                   choice_values::text ChoiceValuesJson, evidence_document_id DocumentEvidenceId,
                   observation Notes, conforming
            from agro360.quality_inspection_answers
            where tenant_id=@TenantId and run_id=@RunId
            """, new { TenantId = tenantId, RunId = id }, t, cancellationToken: ct)))
            .ToDictionary(x => x.CriterionId);

        var sectionDtos = sections.Select(s =>
        {
            var crits = criteria.Where(x => x.SectionId == s.Id).Select(cr =>
            {
                answers.TryGetValue(cr.Id, out var ans);
                return new InspectionCriterionRuntimeDto(
                    cr.Id, cr.StableKey, cr.Name, cr.Guidance, cr.CriterionType, cr.Required,
                    IsCritical(cr.Criticality), cr.AllowNotApplicable, cr.RequireNaJustification,
                    cr.RequireReview, cr.Unit, cr.Weight, cr.SortOrder, cr.ApprovalConditionJson,
                    ParseOptions(cr.OptionsJson),
                    ans is null ? null : ToAnswerDto(ans));
            }).ToArray();
            return new InspectionSectionRuntimeDto(s.Id, s.StableKey, s.Name, s.Description, s.SortOrder, crits);
        }).ToArray();

        var total = criteria.Length;
        var answered = answers.Count;
        var requiredPending = criteria.Count(cr => cr.Required && !answers.ContainsKey(cr.Id));
        var criticalNc = criteria.Count(cr =>
            IsCritical(cr.Criticality) &&
            answers.TryGetValue(cr.Id, out var a) &&
            a.Conforming == false);
        var percent = total == 0 ? 0m : decimal.Round(answered * 100m / total, 2, MidpointRounding.AwayFromZero);

        var determining = string.IsNullOrWhiteSpace(run.DeterminingJson)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<string[]>(run.DeterminingJson, JsonOptions) ?? [];

        return new InspectionRunDetail(
            run.Id, run.Number, run.ProcessCode, run.Status, run.OverallResult, determining,
            run.WeightedScorePercent, run.ModelId, run.ModelVersionId, run.ModelName, run.ModelVersionNumber,
            run.OriginType, run.OriginId, run.ProductId, run.ProductName, run.LotId, run.LotName,
            run.UnitId, run.UnitName, run.InspectorId, run.InspectorName, run.SelectionMode,
            run.ParentRunId, run.ReinspectionReason, run.RowVersion, run.StartedAt, run.LastSavedAt,
            run.CompletedAt,
            new InspectionRunProgress(total, answered, requiredPending, criticalNc, percent),
            sectionDtos);
    }

    private async Task<bool> ApplyNcEffectAsync(
        NpgsqlConnection c, NpgsqlTransaction t, RunLockRow run, CriterionDbRow criterion, CancellationToken ct)
    {
        var already = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
            select exists(select 1 from agro360.quality_inspection_effects
              where tenant_id=@TenantId and run_id=@RunId and criterion_id=@CriterionId and effect_type='NC')
            """, new { TenantId = tenant.TenantId, RunId = run.Id, CriterionId = criterion.Id }, t, cancellationToken: ct));
        if (already) return false;

        var subjectId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            select id from agro360.compliance_subjects
            where tenant_id=@TenantId and type='INSPECTION' and name=@Name
            limit 1
            """, new { TenantId = tenant.TenantId, Name = run.Number }, t, cancellationToken: ct));
        if (subjectId is null)
        {
            subjectId = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition("""
                insert into agro360.compliance_subjects(id, tenant_id, type, name, external_reference)
                values(@Id, @TenantId, 'INSPECTION', @Name, @RunId::text)
                """, new { Id = subjectId, TenantId = tenant.TenantId, Name = run.Number, RunId = run.Id }, t, cancellationToken: ct));
        }

        var ncId = Guid.CreateVersion7();
        var severity = IsCritical(criterion.Criticality) ? "CRITICAL" : "HIGH";
        var rootCause = $"Falha no critério {criterion.StableKey}: {criterion.Name}";
        var corrective = $"Corrigir não conformidade do critério {criterion.StableKey}.";
        await c.ExecuteAsync(new CommandDefinition("""
            insert into agro360.compliance_non_conformities(
                id, tenant_id, title, classification, severity, origin, entity_type, entity_id,
                root_cause, corrective_action, responsible_id, due_on, status, product_id, lot_id, unit_id,
                idempotency_key, created_by)
            values(
                @Id, @TenantId, @Title, 'QUALITY', @Severity, @Origin, 'INSPECTION', @SubjectId,
                @RootCause, @Corrective, @ResponsibleId, current_date + 7, 'OPEN', @ProductId, @LotId, @UnitId,
                @IdempotencyKey, @UserId)
            """, new
        {
            Id = ncId,
            TenantId = tenant.TenantId,
            Title = $"NC inspeção {run.Number} / {criterion.StableKey}",
            Severity = severity,
            Origin = run.ProcessCode,
            SubjectId = subjectId.Value,
            RootCause = rootCause,
            Corrective = corrective,
            ResponsibleId = run.InspectorId,
            run.ProductId,
            run.LotId,
            run.UnitId,
            IdempotencyKey = $"insp:{run.Id:N}:{criterion.Id:N}:NC",
            UserId = tenant.UserId
        }, t, cancellationToken: ct));

        await EnsureEffectAsync(c, t, run.Id, criterion.Id, "NC", ncId, null, null, "APPLIED", ct);
        return true;
    }

    private async Task<bool> ApplyRestrictionEffectAsync(
        NpgsqlConnection c, NpgsqlTransaction t, RunLockRow run, CriterionDbRow criterion, CancellationToken ct)
    {
        var already = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
            select exists(select 1 from agro360.quality_inspection_effects
              where tenant_id=@TenantId and run_id=@RunId and criterion_id=@CriterionId and effect_type='RESTRICTION')
            """, new { TenantId = tenant.TenantId, RunId = run.Id, CriterionId = criterion.Id }, t, cancellationToken: ct));
        if (already) return false;

        Guid? ncId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            select non_conformity_id from agro360.quality_inspection_effects
            where tenant_id=@TenantId and run_id=@RunId and criterion_id=@CriterionId and effect_type='NC'
            """, new { TenantId = tenant.TenantId, RunId = run.Id, CriterionId = criterion.Id }, t, cancellationToken: ct));

        if (ncId is null && criterion.OnFailCreateNc)
        {
            // NC should already exist from prior step; if not, skip linking.
        }

        var restrictionId = Guid.CreateVersion7();
        var type = criterion.OnFailRestrictionType!;
        await c.ExecuteAsync(new CommandDefinition("""
            insert into agro360.compliance_lot_restrictions(
                id, tenant_id, lot_id, non_conformity_id, type, reason, applied_by)
            values(@Id, @TenantId, @LotId, @NcId, @Type, @Reason, @UserId)
            """, new
        {
            Id = restrictionId,
            TenantId = tenant.TenantId,
            LotId = run.LotId!.Value,
            NcId = ncId,
            Type = type,
            Reason = $"Restrição automática da inspeção {run.Number} ({criterion.StableKey})",
            UserId = tenant.UserId
        }, t, cancellationToken: ct));

        if (type.Equals("BLOCK_LOT", StringComparison.OrdinalIgnoreCase))
        {
            await c.ExecuteAsync(new CommandDefinition("""
                update agro360.storage_lots set status='BLOCKED'
                where tenant_id=@TenantId and id=@LotId
                """, new { TenantId = tenant.TenantId, LotId = run.LotId.Value }, t, cancellationToken: ct));
        }

        await EnsureEffectAsync(c, t, run.Id, criterion.Id, "RESTRICTION", ncId, restrictionId, null, "APPLIED", ct);
        return true;
    }

    private async Task<bool> ApplyActionEffectAsync(
        NpgsqlConnection c, NpgsqlTransaction t, RunLockRow run, CriterionDbRow criterion, CancellationToken ct)
    {
        var already = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
            select exists(select 1 from agro360.quality_inspection_effects
              where tenant_id=@TenantId and run_id=@RunId and criterion_id=@CriterionId and effect_type='ACTION')
            """, new { TenantId = tenant.TenantId, RunId = run.Id, CriterionId = criterion.Id }, t, cancellationToken: ct));
        if (already) return false;

        var ncId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            select non_conformity_id from agro360.quality_inspection_effects
            where tenant_id=@TenantId and run_id=@RunId and criterion_id=@CriterionId and effect_type='NC'
            """, new { TenantId = tenant.TenantId, RunId = run.Id, CriterionId = criterion.Id }, t, cancellationToken: ct));
        if (ncId is null) return false;

        var actionId = Guid.CreateVersion7();
        await c.ExecuteAsync(new CommandDefinition("""
            insert into agro360.compliance_nc_actions(
                id, tenant_id, non_conformity_id, type, description, responsible_id, due_on, priority,
                expected_result, evidence_required, mandatory, status, created_by)
            values(
                @Id, @TenantId, @NcId, 'IMMEDIATE_CORRECTION', @Description, @ResponsibleId, current_date + 3,
                @Priority, @Expected, false, true, 'OPEN', @UserId)
            """, new
        {
            Id = actionId,
            TenantId = tenant.TenantId,
            NcId = ncId.Value,
            Description = $"Correção imediata do critério {criterion.StableKey}",
            ResponsibleId = run.InspectorId,
            Priority = IsCritical(criterion.Criticality) ? "CRITICAL" : "HIGH",
            Expected = "Critério reavaliado em conformidade",
            UserId = tenant.UserId
        }, t, cancellationToken: ct));

        await EnsureEffectAsync(c, t, run.Id, criterion.Id, "ACTION", ncId, null, actionId, "APPLIED", ct);
        return true;
    }

    private async Task EnsureEffectAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid runId, Guid criterionId, string effectType,
        Guid? ncId, Guid? restrictionId, Guid? actionId, string status, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition("""
            insert into agro360.quality_inspection_effects(
                id, tenant_id, run_id, criterion_id, effect_type, non_conformity_id, restriction_id, action_id,
                status, created_by, updated_by)
            values(
                gen_random_uuid(), @TenantId, @RunId, @CriterionId, @EffectType, @NcId, @RestrictionId, @ActionId,
                @Status, @UserId, @UserId)
            on conflict (tenant_id, run_id, criterion_id, effect_type) do nothing
            """, new
        {
            TenantId = tenant.TenantId,
            RunId = runId,
            CriterionId = criterionId,
            EffectType = effectType,
            NcId = ncId,
            RestrictionId = restrictionId,
            ActionId = actionId,
            Status = status,
            UserId = tenant.UserId
        }, t, cancellationToken: ct));
    }

    private static async Task CopyVersionContentAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid fromVersionId, Guid toVersionId, CancellationToken ct)
    {
        var sections = (await c.QueryAsync<(Guid Id, string Code, string Name, int Sequence, string? Instructions)>(
            new CommandDefinition("""
                select id, code, name, sequence, instructions
                from agro360.quality_inspection_model_sections
                where tenant_id=current_setting('app.tenant_id')::uuid and version_id=@From
                order by sequence
                """, new { From = fromVersionId }, t, cancellationToken: ct))).ToArray();

        var tenantId = await c.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "select current_setting('app.tenant_id')::uuid", transaction: t, cancellationToken: ct));

        foreach (var section in sections)
        {
            var newSectionId = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition("""
                insert into agro360.quality_inspection_model_sections(
                    id, tenant_id, version_id, code, name, sequence, instructions, created_by, updated_by)
                select @NewId, tenant_id, @ToVersion, code, name, sequence, instructions, created_by, created_by
                from agro360.quality_inspection_model_sections
                where tenant_id=@TenantId and id=@OldId
                """, new { NewId = newSectionId, ToVersion = toVersionId, TenantId = tenantId, OldId = section.Id }, t, cancellationToken: ct));

            await c.ExecuteAsync(new CommandDefinition("""
                insert into agro360.quality_inspection_model_criteria(
                    id, tenant_id, version_id, section_id, stable_key, name, explanation, criterion_type,
                    required, unit, options, approval_condition, require_justification, require_evidence,
                    criticality, allow_not_applicable, not_applicable_requires_justification, weight,
                    on_fail_create_nc, on_fail_restriction_type, on_fail_create_action, on_fail_require_review,
                    sequence, created_by, updated_by)
                select gen_random_uuid(), tenant_id, @ToVersion, @NewSectionId, stable_key, name, explanation, criterion_type,
                       required, unit, options, approval_condition, require_justification, require_evidence,
                       criticality, allow_not_applicable, not_applicable_requires_justification, weight,
                       on_fail_create_nc, on_fail_restriction_type, on_fail_create_action, on_fail_require_review,
                       sequence, created_by, created_by
                from agro360.quality_inspection_model_criteria
                where tenant_id=@TenantId and version_id=@From and section_id=@OldSection
                """, new
            {
                ToVersion = toVersionId,
                NewSectionId = newSectionId,
                TenantId = tenantId,
                From = fromVersionId,
                OldSection = section.Id
            }, t, cancellationToken: ct));
        }
    }

    private async Task InsertSectionsAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid versionId,
        IReadOnlyList<InspectionSectionDraftCommand> sections, CancellationToken ct)
    {
        foreach (var section in sections.OrderBy(s => s.SortOrder))
        {
            var sectionId = section.Id ?? Guid.CreateVersion7();
            var code = Guard.Required(section.StableKey, nameof(section.StableKey), 40);
            await c.ExecuteAsync(new CommandDefinition("""
                insert into agro360.quality_inspection_model_sections(
                    id, tenant_id, version_id, code, name, sequence, instructions, created_by, updated_by)
                values(@Id, @TenantId, @VersionId, @Code, @Name, @SortOrder, @Description, @UserId, @UserId)
                """, new
            {
                Id = sectionId,
                tenant.TenantId,
                VersionId = versionId,
                Code = code,
                Name = Guard.Required(section.Name, nameof(section.Name), 160),
                section.SortOrder,
                section.Description,
                UserId = tenant.UserId
            }, t, cancellationToken: ct));

            var seq = 0;
            foreach (var criterion in section.Criteria.OrderBy(x => x.SortOrder))
            {
                seq++;
                InspectionModelRules.EnsureValidCriterionType(criterion.CriterionType);
                var criterionId = criterion.Id ?? Guid.CreateVersion7();
                var optionsJson = JsonSerializer.Serialize(
                    (criterion.Options ?? Array.Empty<InspectionCriterionOptionCommand>())
                    .OrderBy(o => o.SortOrder)
                    .Select(o => new { value = o.Value, label = o.Label, sortOrder = o.SortOrder }),
                    JsonOptions);
                var approvalJson = string.IsNullOrWhiteSpace(criterion.ApprovalConditionJson)
                    ? "{}"
                    : criterion.ApprovalConditionJson!;

                await c.ExecuteAsync(new CommandDefinition("""
                    insert into agro360.quality_inspection_model_criteria(
                        id, tenant_id, version_id, section_id, stable_key, name, explanation, criterion_type,
                        required, unit, options, approval_condition, criticality, allow_not_applicable,
                        not_applicable_requires_justification, weight, on_fail_require_review, sequence,
                        created_by, updated_by)
                    values(
                        @Id, @TenantId, @VersionId, @SectionId, @StableKey, @Name, @Guidance, @CriterionType,
                        @Required, @Unit, cast(@Options as jsonb), cast(@Approval as jsonb), @Criticality,
                        @AllowNotApplicable, @RequireNaJustification, @Weight, @RequireReview, @Sequence,
                        @UserId, @UserId)
                    """, new
                {
                    Id = criterionId,
                    tenant.TenantId,
                    VersionId = versionId,
                    SectionId = sectionId,
                    StableKey = Guard.Required(criterion.StableKey, nameof(criterion.StableKey), 80),
                    Name = Guard.Required(criterion.Name, nameof(criterion.Name), 180),
                    criterion.Guidance,
                    CriterionType = criterion.CriterionType.ToUpperInvariant(),
                    criterion.Required,
                    criterion.Unit,
                    Options = optionsJson,
                    Approval = approvalJson,
                    Criticality = criterion.Critical ? "CRITICAL" : "NORMAL",
                    criterion.AllowNotApplicable,
                    criterion.RequireNaJustification,
                    criterion.Weight,
                    criterion.RequireReview,
                    Sequence = criterion.SortOrder != 0 ? criterion.SortOrder : seq,
                    UserId = tenant.UserId
                }, t, cancellationToken: ct));
            }
        }
    }

    private static void ValidateDraftSections(IReadOnlyList<InspectionSectionDraftCommand> sections)
    {
        var sectionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var criterionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections)
        {
            if (!sectionKeys.Add(section.StableKey))
                throw new ArgumentException($"Seção duplicada: {section.StableKey}");
            if (section.Criteria is null || section.Criteria.Count == 0)
                throw new ArgumentException($"Seção {section.StableKey} sem critérios.");
            foreach (var criterion in section.Criteria)
            {
                if (!criterionKeys.Add(criterion.StableKey))
                    throw new ArgumentException($"Critério duplicado: {criterion.StableKey}");
                InspectionModelRules.EnsureValidCriterionType(criterion.CriterionType);
            }
        }
    }

    private static string ComputeContentHash(IReadOnlyList<InspectionSectionDraftCommand> sections)
    {
        var canonical = sections
            .OrderBy(s => s.SortOrder).ThenBy(s => s.StableKey, StringComparer.Ordinal)
            .Select(s => new
            {
                s.StableKey,
                s.Name,
                s.Description,
                s.SortOrder,
                Criteria = s.Criteria
                    .OrderBy(c => c.SortOrder).ThenBy(c => c.StableKey, StringComparer.Ordinal)
                    .Select(c => new
                    {
                        c.StableKey,
                        c.Name,
                        c.Guidance,
                        CriterionType = c.CriterionType.ToUpperInvariant(),
                        c.Required,
                        c.Critical,
                        c.AllowNotApplicable,
                        c.RequireNaJustification,
                        c.RequireReview,
                        c.Unit,
                        c.Weight,
                        c.SortOrder,
                        c.ApprovalConditionJson,
                        Options = (c.Options ?? Array.Empty<InspectionCriterionOptionCommand>())
                            .OrderBy(o => o.SortOrder).Select(o => new { o.Value, o.Label, o.SortOrder })
                    })
            });
        var json = JsonSerializer.Serialize(canonical, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private async Task AuditAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid modelId, Guid? versionId, string action, CancellationToken ct) =>
        await c.ExecuteAsync(new CommandDefinition("""
            insert into agro360.quality_inspection_model_audits(
                id, tenant_id, model_id, version_id, action, before_data, after_data, actor_id)
            values(gen_random_uuid(), @TenantId, @ModelId, @VersionId, @Action, '{}'::jsonb, '{}'::jsonb, @UserId)
            """, new { tenant.TenantId, ModelId = modelId, VersionId = versionId, Action = action, UserId = tenant.UserId }, t, cancellationToken: ct));

    private async Task<VersionLockRow> LockVersionAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid versionId, CancellationToken ct) =>
        await c.QuerySingleOrDefaultAsync<VersionLockRow>(new CommandDefinition("""
            select id, model_id ModelId, status, row_version RowVersion
            from agro360.quality_inspection_model_versions
            where tenant_id=@TenantId and id=@Id for update
            """, new { tenant.TenantId, Id = versionId }, t, cancellationToken: ct))
        ?? throw new NotFoundException("Versão de modelo", versionId);

    private static async Task<RunLockRow> LockRunAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid tenantId, Guid runId, CancellationToken ct) =>
        await c.QuerySingleOrDefaultAsync<RunLockRow>(new CommandDefinition("""
            select id, number, model_id ModelId, model_version_id ModelVersionId, model_version_number ModelVersionNumber,
                   process_code ProcessCode, status, overall_result OverallResult, weighted_score_percent WeightedScorePercent,
                   origin_type OriginType, origin_id OriginId, product_id ProductId, lot_id LotId, unit_id UnitId,
                   inspector_id InspectorId, selection_mode SelectionMode, row_version RowVersion
            from agro360.quality_inspection_runs
            where tenant_id=@TenantId and id=@Id and deleted_at is null for update
            """, new { TenantId = tenantId, Id = runId }, t, cancellationToken: ct))
        ?? throw new NotFoundException("Inspeção", runId);

    private static async Task<IReadOnlyList<CompareSide>> LoadCompareSideAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid tenantId, Guid runId, CancellationToken ct)
    {
        var rows = await c.QueryAsync<CompareSide>(new CommandDefinition("""
            select cr.stable_key StableKey, cr.criterion_type CriterionType, cr.name,
                   case when a.not_applicable then 'NOT_APPLICABLE'
                        when a.conforming is true then 'CONFORMING'
                        when a.conforming is false then 'NON_CONFORMING'
                        when a.id is null then 'UNANSWERED'
                        else 'INCONCLUSIVE' end Outcome,
                   coalesce(a.pass_fail, a.text_value, a.number_value::text, a.date_value::text, a.choice_values::text) ValueSummary
            from agro360.quality_inspection_runs r
            join agro360.quality_inspection_model_criteria cr
              on cr.tenant_id=r.tenant_id and cr.version_id=r.model_version_id
            left join agro360.quality_inspection_answers a
              on a.tenant_id=r.tenant_id and a.run_id=r.id and a.criterion_id=cr.id
            where r.tenant_id=@TenantId and r.id=@RunId and r.deleted_at is null
            order by cr.stable_key
            """, new { TenantId = tenantId, RunId = runId }, t, cancellationToken: ct));
        var list = rows.ToArray();
        if (list.Length == 0)
        {
            var exists = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
                select exists(select 1 from agro360.quality_inspection_runs where tenant_id=@TenantId and id=@RunId and deleted_at is null)
                """, new { TenantId = tenantId, RunId = runId }, t, cancellationToken: ct));
            if (!exists) throw new NotFoundException("Inspeção", runId);
        }

        return list;
    }

    private static InspectionCriterionDefinition ToDefinition(CriterionDbRow criterion)
    {
        var optionValues = ParseOptions(criterion.OptionsJson).Select(o => o.Value).ToArray();
        ApprovalCondition? approval = null;
        if (!string.IsNullOrWhiteSpace(criterion.ApprovalConditionJson) &&
            criterion.ApprovalConditionJson != "{}")
        {
            approval = JsonSerializer.Deserialize<ApprovalCondition>(criterion.ApprovalConditionJson, JsonOptions);
        }

        return new InspectionCriterionDefinition(
            criterion.StableKey,
            criterion.CriterionType,
            criterion.Required,
            IsCritical(criterion.Criticality),
            criterion.AllowNotApplicable,
            criterion.RequireNaJustification,
            criterion.RequireReview,
            criterion.Unit,
            optionValues,
            approval,
            criterion.Weight);
    }

    private static InspectionAnswerInput ToAnswerInput(InspectionAnswerSaveItem answer) =>
        new(answer.IsNotApplicable, answer.NotApplicableJustification, answer.PassFailValue, answer.TextValue,
            answer.NumberValue, answer.NumberUnit, answer.DateValue, answer.ChoiceValues, answer.DocumentEvidenceId,
            answer.DocumentEvidenceId.HasValue);

    private static InspectionAnswerInput ToAnswerInputFromDb(AnswerDbRow answer)
    {
        bool? passFail = answer.PassFail switch
        {
            "PASS" => true,
            "FAIL" => false,
            _ => null
        };
        IReadOnlyList<string>? choices = string.IsNullOrWhiteSpace(answer.ChoiceValuesJson)
            ? null
            : JsonSerializer.Deserialize<string[]>(answer.ChoiceValuesJson, JsonOptions);
        return new InspectionAnswerInput(
            answer.IsNotApplicable, answer.NotApplicableJustification, passFail, answer.TextValue,
            answer.NumberValue, answer.NumberUnit, answer.DateValue, choices, answer.DocumentEvidenceId,
            answer.DocumentEvidenceId.HasValue);
    }

    private static InspectionAnswerDto ToAnswerDto(AnswerDbRow answer)
    {
        bool? passFail = answer.PassFail switch
        {
            "PASS" => true,
            "FAIL" => false,
            _ => null
        };
        IReadOnlyList<string>? choices = string.IsNullOrWhiteSpace(answer.ChoiceValuesJson)
            ? null
            : JsonSerializer.Deserialize<string[]>(answer.ChoiceValuesJson, JsonOptions);
        var outcome = answer.IsNotApplicable ? "NOT_APPLICABLE"
            : answer.Conforming == true ? "CONFORMING"
            : answer.Conforming == false ? "NON_CONFORMING"
            : "INCONCLUSIVE";
        return new InspectionAnswerDto(
            answer.CriterionId, answer.StableKey, answer.IsNotApplicable, answer.NotApplicableJustification,
            passFail, answer.TextValue, answer.NumberValue, answer.NumberUnit, answer.DateValue, choices,
            answer.DocumentEvidenceId, outcome, answer.Notes);
    }

    private static IReadOnlyList<InspectionCriterionOptionCommand> ParseOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return Array.Empty<InspectionCriterionOptionCommand>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<InspectionCriterionOptionCommand>();
            var list = new List<InspectionCriterionOptionCommand>();
            var i = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var v = el.GetString() ?? "";
                    list.Add(new InspectionCriterionOptionCommand(v, v, i++));
                }
                else if (el.ValueKind == JsonValueKind.Object)
                {
                    var value = el.TryGetProperty("value", out var v) ? v.GetString() ?? "" :
                        el.TryGetProperty("Value", out var v2) ? v2.GetString() ?? "" : "";
                    var label = el.TryGetProperty("label", out var l) ? l.GetString() ?? value :
                        el.TryGetProperty("Label", out var l2) ? l2.GetString() ?? value : value;
                    var sort = el.TryGetProperty("sortOrder", out var s) && s.TryGetInt32(out var so) ? so :
                        el.TryGetProperty("SortOrder", out var s2) && s2.TryGetInt32(out var so2) ? so2 : i;
                    list.Add(new InspectionCriterionOptionCommand(value, label, sort));
                    i++;
                }
            }

            return list;
        }
        catch (JsonException)
        {
            return Array.Empty<InspectionCriterionOptionCommand>();
        }
    }

    private static bool IsCritical(string criticality) =>
        criticality.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase) ||
        criticality.Equals("HIGH", StringComparison.OrdinalIgnoreCase);

    private static void EnsureOcc(long expected, long actual)
    {
        if (expected != actual)
            throw new ConflictException("O registro foi alterado por outro usuário. Recarregue e tente novamente.");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static DateTimeOffset ToScheduleTimestamp(DateOnly date)
    {
        // Store as timestamptz representing midnight in America/Sao_Paulo.
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows()
                ? "E. South America Standard Time"
                : ScheduleTz);
            var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, tz), TimeSpan.Zero);
        }
        catch (TimeZoneNotFoundException)
        {
            return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        }
    }

    private sealed record ModelHeaderRow(
        Guid Id, string Code, string Name, string? Description, string ProcessCode, string Status,
        int Precedence, bool AllowManualSelection, Guid? UnitId, string? ProductCategory, Guid? ProductId,
        long RowVersion);

    private sealed record VersionLockRow(Guid Id, Guid ModelId, string Status, long RowVersion);
    private sealed record VersionHeaderRow(
        Guid Id, Guid ModelId, int VersionNumber, string Status, DateOnly? ValidFrom, DateOnly? ValidUntil,
        string? ChangeReason, string? Instructions, long RowVersion);

    private sealed record CandidateRow(
        Guid ModelId, string Code, string Name, int Precedence, bool AllowManualSelection,
        Guid? ProductId, string? ProductCategory, Guid? UnitId, Guid VersionId, int VersionNumber,
        DateOnly ValidFrom, DateOnly? ValidUntil);

    private sealed record StartVersionRow(
        Guid ModelId, string ModelStatus, bool AllowManual, Guid VersionId, int VersionNumber,
        string VersionStatus, DateOnly ValidFrom, DateOnly? ValidUntil);

    private sealed record RunLockRow(
        Guid Id, string Number, Guid ModelId, Guid ModelVersionId, int ModelVersionNumber, string ProcessCode,
        string Status, string? OverallResult, decimal? WeightedScorePercent, string OriginType, Guid? OriginId,
        Guid? ProductId, Guid? LotId, Guid? UnitId, Guid InspectorId, string SelectionMode, long RowVersion);

    private sealed record RunDetailRow(
        Guid Id, string Number, string ProcessCode, string Status, string? OverallResult, string? DeterminingJson,
        decimal? WeightedScorePercent, Guid ModelId, Guid ModelVersionId, string ModelName, int ModelVersionNumber,
        string? OriginType, Guid? OriginId, Guid? ProductId, string? ProductName, Guid? LotId, string? LotName,
        Guid? UnitId, string? UnitName, Guid InspectorId, string InspectorName, string SelectionMode,
        Guid? ParentRunId, string? ReinspectionReason, long RowVersion, DateTimeOffset StartedAt,
        DateTimeOffset LastSavedAt, DateTimeOffset? CompletedAt);

    private sealed class SectionDbRow
    {
        public Guid Id { get; init; }
        public string StableKey { get; init; } = "";
        public string Name { get; init; } = "";
        public string? Description { get; init; }
        public int SortOrder { get; init; }
    }

    private sealed class CriterionDbRow
    {
        public Guid Id { get; init; }
        public Guid SectionId { get; init; }
        public string StableKey { get; init; } = "";
        public string Name { get; init; } = "";
        public string? Guidance { get; init; }
        public string CriterionType { get; init; } = "";
        public bool Required { get; init; }
        public string Criticality { get; init; } = "NORMAL";
        public bool AllowNotApplicable { get; init; }
        public bool RequireNaJustification { get; init; }
        public bool RequireReview { get; init; }
        public string? Unit { get; init; }
        public decimal? Weight { get; init; }
        public int SortOrder { get; init; }
        public string? OptionsJson { get; init; }
        public string? ApprovalConditionJson { get; init; }
        public bool OnFailCreateNc { get; init; }
        public string? OnFailRestrictionType { get; init; }
        public bool OnFailCreateAction { get; init; }
    }

    private sealed class AnswerDbRow
    {
        public Guid CriterionId { get; init; }
        public string StableKey { get; init; } = "";
        public bool IsNotApplicable { get; init; }
        public string? NotApplicableJustification { get; init; }
        public string? PassFail { get; init; }
        public string? TextValue { get; init; }
        public decimal? NumberValue { get; init; }
        public string? NumberUnit { get; init; }
        public DateOnly? DateValue { get; init; }
        public string? ChoiceValuesJson { get; init; }
        public Guid? DocumentEvidenceId { get; init; }
        public string? Notes { get; init; }
        public bool? Conforming { get; init; }
    }

    private sealed record ScheduleDueRow(
        Guid Id, string Name, Guid ModelId, string ProcessCode, string ScheduleType, int? IntervalDays,
        bool AllowCatchUp, DateTimeOffset? NextRunAt, Guid? ResponsibleId, Guid? UnitId, Guid? ProductId,
        string? ProductCategory, Guid? CreatedBy, DateOnly? EndsOn, Guid VersionId, int VersionNumber,
        bool ResponsibleActive);

    private sealed record CompareSide(
        string StableKey, string CriterionType, string Name, string? Outcome, string? ValueSummary);
}
