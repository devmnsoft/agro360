using Agro360.Application.Contracts;
using Agro360.Domain.Governance;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Dapper;

namespace Agro360.Infrastructure.Services;

public sealed class DataGovernanceService(DatabaseExecutor db, ITenantContext tenant) : IDataGovernanceService
{
    public Task<ImportBatch> CreateImportAsync(ImportBatchCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (connection, transaction) =>
    {
        DataGovernanceRules.ValidateImport(command.Module, command.Csv);
        var rows = ParseCsv(command.Csv);
        var headers = rows[0];
        if (headers.Any(string.IsNullOrWhiteSpace) || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new ArgumentException("CSV inválido: cabeçalhos vazios ou duplicados.");

        var errors = ValidateRows(rows, headers);
        var critical = errors.Count(x => x.Severity == "CRITICAL");
        var status = critical > 0 && !command.ConfirmCriticalErrors ? "REQUIRES_CONFIRMATION" : "VALIDATED";
        var id = Guid.NewGuid();
        await connection.ExecuteAsync("""
            insert into governance.data_import_batches(id,tenant_id,module,file_name,status,total_rows,valid_rows,error_rows,confirmed_critical,created_by,updated_by)
            values(@Id,@TenantId,@Module,@FileName,@Status,@TotalRows,@ValidRows,@ErrorRows,@Confirmed,@UserId,@UserId)
            """, new { id, tenant.TenantId, tenant.UserId, Module = command.Module.ToUpperInvariant(), command.FileName, Status = status, TotalRows = rows.Count - 1, ValidRows = rows.Count - 1 - errors.Select(x => x.RowNumber).Distinct().Count(), ErrorRows = errors.Select(x => x.RowNumber).Distinct().Count(), Confirmed = command.ConfirmCriticalErrors }, transaction);
        foreach (var error in errors)
            await connection.ExecuteAsync("insert into governance.data_import_errors(id,tenant_id,batch_id,row_number,column_name,error_code,message,severity,created_by) values(gen_random_uuid(),@TenantId,@Id,@RowNumber,@ColumnName,@Code,@Message,@Severity,@UserId)", new { tenant.TenantId, tenant.UserId, id, error.RowNumber, error.ColumnName, error.Code, error.Message, error.Severity }, transaction);
        await Audit(connection, transaction, "IMPORT_VALIDATED", "data_import_batch", id, $"module={command.Module}; status={status}");
        return new ImportBatch(id, command.Module, command.FileName, status, rows.Count - 1, rows.Count - 1 - errors.Select(x => x.RowNumber).Distinct().Count(), errors.Select(x => x.RowNumber).Distinct().Count(), DateTimeOffset.UtcNow);
    }, ct);

    public Task<PagedResult<ImportBatch>> ImportsAsync(int page, int pageSize, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await c.ExecuteScalarAsync<int>("select count(*) from governance.data_import_batches where tenant_id=@TenantId and deleted_at is null", new { tenant.TenantId }, t);
        var items = (await c.QueryAsync<ImportBatch>("select id,module,file_name filename,status,total_rows totalrows,valid_rows validrows,error_rows errorrows,created_at createdat from governance.data_import_batches where tenant_id=@TenantId and deleted_at is null order by created_at desc limit @PageSize offset @Offset", new { tenant.TenantId, PageSize = pageSize, Offset = (page - 1) * pageSize }, t)).AsList();
        return new PagedResult<ImportBatch>(items, page, pageSize, total);
    }, ct);

    public Task<IReadOnlyList<ImportError>> ErrorsAsync(Guid batchId, CancellationToken ct) => db.InTenantTransactionAsync(async (c,t) =>
        (IReadOnlyList<ImportError>)(await c.QueryAsync<ImportError>("select row_number rownumber,column_name columnname,error_code code,message,severity from governance.data_import_errors where tenant_id=@TenantId and batch_id=@BatchId order by row_number,column_name", new { tenant.TenantId, batchId }, t)).AsList(), ct);

    public Task ReprocessAsync(Guid batchId, CancellationToken ct) => ChangeBatch(batchId, "VALIDATED", "IMPORT_REPROCESSED", null, ct);
    public Task CancelAsync(Guid batchId, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("O cancelamento exige motivo.");
        return ChangeBatch(batchId, "CANCELLED", "IMPORT_CANCELLED", reason, ct);
    }

    public Task<Guid> CreateExportAsync(ExportRequestCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c,t) =>
    {
        if (command.Modules.Count == 0) throw new ArgumentException("Selecione ao menos um módulo.");
        if (command.Format is not ("CSV" or "JSON")) throw new ArgumentException("Formato deve ser CSV ou JSON.");
        var id=Guid.NewGuid();
        await c.ExecuteAsync("insert into governance.data_export_requests(id,tenant_id,modules,period_from,period_to,format,status,justification,requested_by,created_by,updated_by) values(@Id,@TenantId,@Modules,@From,@To,@Format,'QUEUED',@Justification,@UserId,@UserId,@UserId)",new{id,tenant.TenantId,tenant.UserId,Modules=command.Modules.ToArray(),command.From,command.To,command.Format,command.Justification},t);
        await Audit(c,t,"EXPORT_REQUESTED","data_export_request",id,"Exportação sem credenciais, hashes ou segredos."); return id;
    },ct);

    public Task<Guid> CreateLgpdAsync(LgpdRequestCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async(c,t) =>
    {
        if(string.IsNullOrWhiteSpace(command.Type)||string.IsNullOrWhiteSpace(command.SubjectName)) throw new ArgumentException("Tipo e titular são obrigatórios.");
        var document=DataGovernanceRules.NormalizeDocument(command.SubjectDocument); var id=Guid.NewGuid();
        await c.ExecuteAsync("insert into governance.lgpd_requests(id,tenant_id,type,subject_name,subject_document,legal_basis,purpose,status,created_by,updated_by) values(@Id,@TenantId,@Type,@SubjectName,@Document,@LegalBasis,@Purpose,'OPEN',@UserId,@UserId)",new{id,tenant.TenantId,tenant.UserId,command.Type,command.SubjectName,Document=document,command.LegalBasis,command.Purpose},t);
        await Audit(c,t,"LGPD_REQUEST_CREATED","lgpd_request",id,$"type={command.Type}; subject={DataGovernanceRules.MaskDocument(document)}"); return id;
    },ct);

    public Task TransitionLgpdAsync(Guid id,LgpdTransition command,CancellationToken ct)=>db.InTenantTransactionAsync(async(c,t)=>
    { DataGovernanceRules.ValidateLgpdTransition(command.Status,command.Reason); var n=await c.ExecuteAsync("update governance.lgpd_requests set status=@Status,decision_reason=@Reason,handled_by=@UserId,handled_at=now(),updated_by=@UserId,updated_at=now() where tenant_id=@TenantId and id=@Id and deleted_at is null",new{tenant.TenantId,tenant.UserId,id,command.Status,command.Reason},t);if(n==0)throw new KeyNotFoundException("Solicitação LGPD não encontrada.");await Audit(c,t,"LGPD_STATUS_CHANGED","lgpd_request",id,$"status={command.Status}; reason={command.Reason}");},ct);

    public Task ActOnFindingAsync(Guid id,FindingAction command,CancellationToken ct)=>db.InTenantTransactionAsync(async(c,t)=>
    { DataGovernanceRules.ValidateFindingTransition(command.Status,command.Justification);var n=await c.ExecuteAsync("update governance.data_quality_findings set status=@Status,justification=@Justification,reviewed_by=@UserId,reviewed_at=now(),updated_by=@UserId,updated_at=now() where tenant_id=@TenantId and id=@Id and deleted_at is null",new{tenant.TenantId,tenant.UserId,id,command.Status,command.Justification},t);if(n==0)throw new KeyNotFoundException("Inconsistência não encontrada.");await Audit(c,t,"QUALITY_FINDING_CHANGED","data_quality_finding",id,$"status={command.Status}; justification={command.Justification}");},ct);

    private Task ChangeBatch(Guid id,string status,string action,string? reason,CancellationToken ct)=>db.InTenantTransactionAsync(async(c,t)=>{var n=await c.ExecuteAsync("update governance.data_import_batches set status=@Status,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id and status not in ('PROCESSING','COMPLETED')",new{tenant.TenantId,tenant.UserId,id,status},t);if(n==0)throw new InvalidOperationException("Lote não existe ou não pode ser alterado no estado atual.");await Audit(c,t,action,"data_import_batch",id,reason);},ct);
    private async Task Audit(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction t,string action,string entity,Guid id,string? justification)=>await c.ExecuteAsync("insert into governance.advanced_audit_events(id,tenant_id,user_id,module,entity,entity_id,action,origin,correlation_id,justification,created_at) values(gen_random_uuid(),@TenantId,@UserId,'GOVERNANCE',@Entity,@Id,@Action,'API',gen_random_uuid()::text,@Justification,now())",new{tenant.TenantId,tenant.UserId,entity,id,action,justification},t);

    private static List<string[]> ParseCsv(string csv)
    {
        var result=new List<string[]>();
        foreach(var raw in csv.Replace("\r\n","\n").Split('\n',StringSplitOptions.RemoveEmptyEntries))
        { var fields=new List<string>();var field="";var quoted=false;for(var i=0;i<raw.Length;i++){var ch=raw[i];if(ch=='\"'&&quoted&&i+1<raw.Length&&raw[i+1]=='\"'){field+='\"';i++;}else if(ch=='\"')quoted=!quoted;else if(ch==','&&!quoted){fields.Add(field.Trim());field="";}else field+=ch;}if(quoted)throw new ArgumentException("CSV inválido: aspas não foram fechadas.");fields.Add(field.Trim());result.Add(fields.ToArray());}
        if(result.Count<2)throw new ArgumentException("CSV inválido: inclua cabeçalho e dados.");if(result.Skip(1).Any(x=>x.Length!=result[0].Length))throw new ArgumentException("CSV inválido: quantidade de colunas inconsistente.");return result;
    }
    private static List<ImportError> ValidateRows(List<string[]> rows,string[] headers)
    { var errors=new List<ImportError>();var docs=new HashSet<string>();var emails=new HashSet<string>(StringComparer.OrdinalIgnoreCase);for(var r=1;r<rows.Count;r++)for(var c=0;c<headers.Length;c++){var name=headers[c].Trim().ToLowerInvariant();var value=rows[r][c];if(name is "document" or "cpf" or "cnpj"){try{var doc=DataGovernanceRules.NormalizeDocument(value);if(!docs.Add(doc))errors.Add(new(r+1,headers[c],"DUPLICATE_DOCUMENT","Documento duplicado no arquivo.","CRITICAL"));}catch(ArgumentException ex){errors.Add(new(r+1,headers[c],"INVALID_DOCUMENT",ex.Message,"CRITICAL"));}}if(name=="email"&&!string.IsNullOrWhiteSpace(value)){try{var email=DataGovernanceRules.NormalizeEmail(value);if(!emails.Add(email))errors.Add(new(r+1,headers[c],"DUPLICATE_EMAIL","E-mail duplicado no arquivo.","CRITICAL"));}catch(ArgumentException ex){errors.Add(new(r+1,headers[c],"INVALID_EMAIL",ex.Message,"CRITICAL"));}}}return errors; }
}
