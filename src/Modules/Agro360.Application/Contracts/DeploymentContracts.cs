namespace Agro360.Application.Contracts;

public sealed record OnboardingCommand(string OrganizationName,string Slug,string Segment,string PropertyName,string AdministratorName,string AdministratorEmail,string InitialCycle,string TemplateCode,string[] Modules,string[] CostCenters,string[] Products,bool ConfigureInitialStock,bool ConfigureInitialFinance);
public sealed record OnboardingResult(Guid TenantId,Guid OnboardingId,int Progress,string Status);
public sealed record DeploymentTemplate(string Code,string Name,string Segment,string Description,string Configuration);
public sealed record ChecklistItem(string Code,string Label,bool Required,bool Completed,string? Notes);
public sealed record DeploymentChecklist(Guid TenantId,int Progress,IReadOnlyList<ChecklistItem> Items);
public sealed record ImportPreviewCommand(string Type,string FileName,string Content,char Delimiter,Dictionary<string,string> Mapping);
public sealed record ImportRow(int Line,bool Valid,Dictionary<string,string> Values,string[] Errors);
public sealed record ImportPreview(Guid Token,string Type,string FileName,int ValidRows,int InvalidRows,IReadOnlyList<ImportRow> Rows);
public sealed record ImportHistory(Guid Id,string Type,string FileName,string Status,int TotalRows,int ValidRows,int InvalidRows,DateTimeOffset CreatedAt);
public sealed record DeploymentDashboard(int ImplementedOrganizations,int PendingOrganizations,decimal AverageProgress,IReadOnlyList<string> MostUsedModules,IReadOnlyList<string> Segments,int ImportErrors,IReadOnlyList<ImportHistory> LatestImports);

public interface IDeploymentService
{
 Task<IReadOnlyList<DeploymentTemplate>> TemplatesAsync(CancellationToken ct);
 Task<OnboardingResult> OnboardAsync(OnboardingCommand command,Guid actor,CancellationToken ct);
 Task<DeploymentChecklist> ChecklistAsync(CancellationToken ct);
 Task SetChecklistAsync(string code,bool completed,string? notes,Guid actor,CancellationToken ct);
 Task<ImportPreview> PreviewAsync(ImportPreviewCommand command,CancellationToken ct);
 Task<Guid> ConfirmImportAsync(Guid token,Guid actor,CancellationToken ct);
 Task<IReadOnlyList<ImportHistory>> ImportsAsync(CancellationToken ct);
 Task RollbackImportAsync(Guid id,Guid actor,CancellationToken ct);
 Task<DeploymentDashboard> DashboardAsync(CancellationToken ct);
}
