using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services;

public sealed class FiscalProviderRegistry(IEnumerable<IFiscalProvider> registeredProviders) : IFiscalProviderRegistry
{
    private readonly Dictionary<string, IFiscalProvider> providers = registeredProviders.ToDictionary(x => x.ProviderKey, StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> ProviderKeys => providers.Keys.ToArray();
    public IFiscalProvider? Find(string providerKey) => providers.GetValueOrDefault(providerKey);
}

public sealed class FiscalEmissionService(DatabaseExecutor db, ITenantContext tenant, IFiscalProviderRegistry registry) : IFiscalEmissionService
{
    public Task<FiscalProviderResult> SubmitAsync(Guid documentId,CancellationToken ct) => ExecuteAsync(documentId,"SUBMIT",null,ct);
    public Task<FiscalProviderResult> QueryAsync(Guid documentId,CancellationToken ct) => ExecuteAsync(documentId,"QUERY",null,ct);
    public Task<FiscalProviderResult> CancelAsync(Guid documentId,string reason,CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Cancelamento fiscal exige motivo.","agro360.fiscal_cancel_reason_required");
        return ExecuteAsync(documentId,"CANCEL",reason.Trim(),ct);
    }

    private Task<FiscalProviderResult> ExecuteAsync(Guid documentId,string operation,string? reason,CancellationToken ct) =>
        db.InTenantTransactionAsync(async (connection,transaction) =>
        {
            var document = await connection.QuerySingleOrDefaultAsync<DocumentRow>(new CommandDefinition("select d.id,d.type,d.status,d.provider_key,d.provider_document_id,d.idempotency_key from agro360.fiscal_documents d where d.tenant_id=@TenantId and d.id=@Id and d.deleted_at is null for update",new{tenant.TenantId,Id=documentId},transaction,cancellationToken:ct)) ?? throw new NotFoundException("Documento fiscal",documentId);
            var provider = string.IsNullOrWhiteSpace(document.ProviderKey) ? null : registry.Find(document.ProviderKey);
            if (provider is null)
            {
                await connection.ExecuteAsync(new CommandDefinition("update agro360.fiscal_documents set status='NOT_CONFIGURED',status_reason='ProviderNotConfigured',updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id and status<>'EXTERNALLY_AUTHORIZED'",new{tenant.TenantId,tenant.UserId,Id=document.Id},transaction,cancellationToken:ct));
                await RecordAsync(connection,transaction,document,"PROVIDER_NOT_CONFIGURED",operation,null,"ProviderNotConfigured",ct);
                return new(FiscalProviderOutcome.ProviderUnavailable,PublicMessage:"ProviderNotConfigured");
            }
            if (operation=="SUBMIT" && document.Status is not ("READY_TO_SUBMIT" or "PROVIDER_UNAVAILABLE" or "NOT_CONFIGURED" or "REJECTED")) throw new DomainException("Documento não está pronto para envio.","agro360.fiscal_submit_invalid_status");
            if (operation=="CANCEL" && document.Status!="EXTERNALLY_AUTHORIZED") throw new DomainException("Somente documento autorizado pode solicitar cancelamento externo.","agro360.fiscal_cancel_invalid_status");
            var request=new FiscalProviderRequest(tenant.TenantId,document.Id,document.Type,document.IdempotencyKey,document.ProviderDocumentId,reason);
            FiscalProviderResult result;
            try { result=operation switch { "SUBMIT"=>await provider.SubmitAsync(request,ct),"QUERY"=>await provider.QueryAsync(request,ct),_=>await provider.CancelAsync(request,ct) }; }
            catch (HttpRequestException) { result=new(FiscalProviderOutcome.ProviderUnavailable,PublicMessage:"ProviderUnavailable"); }
            ValidateProviderResult(result);
            await ApplyAsync(connection,transaction,document,operation,result,ct);
            return result;
        },ct);

    private async Task ApplyAsync(Npgsql.NpgsqlConnection c,Npgsql.NpgsqlTransaction t,DocumentRow d,string operation,FiscalProviderResult result,CancellationToken ct)
    {
        var status=result.Outcome switch { FiscalProviderOutcome.Submitted=>"SUBMITTED",FiscalProviderOutcome.Authorized=>"EXTERNALLY_AUTHORIZED",FiscalProviderOutcome.Rejected=>"REJECTED",FiscalProviderOutcome.Cancelled=>"CANCELLED",_=>"PROVIDER_UNAVAILABLE" };
        await c.ExecuteAsync(new CommandDefinition("update agro360.fiscal_documents set status=@Status,provider_document_id=coalesce(@ProviderDocumentId,provider_document_id),number=coalesce(@ExternalNumber,number),access_key=coalesce(@AccessKey,access_key),verification_code=coalesce(@VerificationCode,verification_code),authorized_at=case when @Status='EXTERNALLY_AUTHORIZED' then now() else authorized_at end,cancelled_at=case when @Status='CANCELLED' then now() else cancelled_at end,status_reason=@Message,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id",new{tenant.TenantId,tenant.UserId,d.Id,Status=status,result.ProviderDocumentId,result.ExternalNumber,result.AccessKey,result.VerificationCode,Message=result.PublicMessage},t,cancellationToken:ct));
        await RecordAsync(c,t,d,status,operation,result.ProviderCode,result.PublicMessage,ct);
    }
    private Task<int> RecordAsync(Npgsql.NpgsqlConnection c,Npgsql.NpgsqlTransaction t,DocumentRow d,string status,string operation,string? code,string? message,CancellationToken ct) => c.ExecuteAsync(new CommandDefinition("insert into agro360.fiscal_provider_attempts(id,tenant_id,fiscal_document_id,provider_key,operation_type,attempt_number,status,provider_code,provider_message,started_at,finished_at,trace_id,created_at) values(gen_random_uuid(),@TenantId,@Id,coalesce(@ProviderKey,'NOT_CONFIGURED'),@Operation,(select count(*)+1 from agro360.fiscal_provider_attempts where tenant_id=@TenantId and fiscal_document_id=@Id and operation_type=@Operation),@Status,@Code,@Message,now(),now(),@TraceId,now())",new{tenant.TenantId,d.Id,d.ProviderKey,Operation=operation,Status=status,Code=code,Message=message,TraceId=tenant.UserId.ToString()},t,cancellationToken:ct));
    private static void ValidateProviderResult(FiscalProviderResult result)
    {
        if (result.Outcome==FiscalProviderOutcome.Authorized && (string.IsNullOrWhiteSpace(result.ProviderDocumentId)||string.IsNullOrWhiteSpace(result.ExternalNumber))) throw new DomainException("Provider retornou autorização incompleta.","agro360.fiscal_provider_invalid_authorization");
        if (result.Outcome==FiscalProviderOutcome.Cancelled && string.IsNullOrWhiteSpace(result.ProviderCode)) throw new DomainException("Provider retornou cancelamento sem confirmação.","agro360.fiscal_provider_invalid_cancellation");
    }
    private sealed record DocumentRow(Guid Id,string Type,string Status,string? ProviderKey,string? ProviderDocumentId,string IdempotencyKey);
}
