using System.Text.Json;
using Agro360.Application.Contracts;
using Agro360.Domain.Deployment;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public sealed class DeploymentService(DatabaseExecutor db,ITenantContext tenant,ILogger<DeploymentService> logger):IDeploymentService
{
 private static readonly string[] CompletedOnboardingItems = ["ORGANIZATION","ADMIN_USER","PROPERTY","CYCLE","PRODUCTS","COST_CENTERS","MODULES"];
 private static readonly string[] AllowedImportTypes = ["PRODUCERS","PROPERTIES","FIELDS","PRODUCTS","SUPPLIERS","INPUTS","STOCK","CUSTOMERS","LIVESTOCK","MEMBERS"];
 public Task<IReadOnlyList<DeploymentTemplate>> TemplatesAsync(CancellationToken ct)=>db.InSystemTransactionAsync(async(c,t)=>(IReadOnlyList<DeploymentTemplate>)(await c.QueryAsync<DeploymentTemplate>("select code,name,segment,description,configuration::text Configuration from agro360.deployment_templates where active order by name",transaction:t)).ToArray(),ct);
 public Task<OnboardingResult> OnboardAsync(OnboardingCommand command,Guid actor,CancellationToken ct)
 {
  DeploymentRules.ValidateOnboarding(command.OrganizationName,command.Slug,command.Segment,command.PropertyName,command.AdministratorName,command.AdministratorEmail,command.InitialCycle,command.Modules);
  if(string.IsNullOrWhiteSpace(command.TemplateCode))throw new ArgumentException("Selecione um template de implantação.");
  return db.InSystemTransactionAsync(async(c,t)=>{
   var tenantId=Guid.NewGuid();var onboardingId=Guid.NewGuid();
   var exists=await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.deployment_templates where code=@TemplateCode and active)",command,t);if(!exists)throw new ArgumentException("Template não encontrado.");
   await c.ExecuteAsync("insert into agro360.tenancy_tenants(id,name,slug,status,created_at) values(@TenantId,@OrganizationName,lower(@Slug),2,now()); select set_config('app.tenant_id',cast(@TenantId as text),true); insert into agro360.deployment_onboardings(id,tenant_id,segment,template_code,status,payload,created_by) values(@Id,@TenantId,upper(@Segment),@TemplateCode,'COMPLETED',cast(@Payload as jsonb),@Actor); insert into agro360.deployment_organization_modules(tenant_id,module_code,enabled,configured_by) select @TenantId,unnest(@Modules),true,@Actor; insert into agro360.deployment_checklist(tenant_id,item_code,label,required,completed,completed_at,updated_by,sort_order) select @TenantId,code,label,required,case when code=any(@Done) then true else false end,case when code=any(@Done) then now() end,@Actor,sort_order from agro360.deployment_checklist_catalog",new{TenantId=tenantId,Id=onboardingId,command.OrganizationName,command.Slug,command.Segment,command.TemplateCode,Payload=JsonSerializer.Serialize(command),command.Modules,Actor=actor,Done=CompletedOnboardingItems},t);
   InfrastructureLogMessages.OnboardingCompleted(logger,onboardingId,tenantId,command.Segment);
   return new OnboardingResult(tenantId,onboardingId,command.ConfigureInitialStock&&command.ConfigureInitialFinance?75:58,"COMPLETED");
  },ct);
 }
 public Task<DeploymentChecklist> ChecklistAsync(CancellationToken ct)=>db.InTenantTransactionAsync(async(c,t)=>{var items=(await c.QueryAsync<ChecklistItem>("select item_code Code,label,required,completed,notes from agro360.deployment_checklist where tenant_id=@TenantId order by sort_order,item_code",new{tenant.TenantId},t)).ToArray();return new DeploymentChecklist(tenant.TenantId,DeploymentRules.Progress(items.Where(x=>x.Required).Select(x=>x.Completed)),items);},ct);
 public Task SetChecklistAsync(string code,bool completed,string? notes,Guid actor,CancellationToken ct)=>db.InTenantTransactionAsync(async(c,t)=>{var n=await c.ExecuteAsync("update agro360.deployment_checklist set completed=@Completed,completed_at=case when @Completed then now() else null end,notes=@Notes,updated_by=@Actor,updated_at=now() where tenant_id=@TenantId and item_code=@Code",new{tenant.TenantId,Code=code,completed,Notes=notes,Actor=actor},t);if(n==0)throw new KeyNotFoundException("Item de checklist não encontrado.");},ct);
 public async Task<ImportPreview> PreviewAsync(ImportPreviewCommand command,CancellationToken ct)
 {
  if(!AllowedImportTypes.Contains(command.Type))throw new ArgumentException("Tipo de importação inválido.");
  var csv=DeploymentRules.ParseCsv(command.Content,command.Delimiter);var headers=csv[0];var rows=csv.Skip(1).Select((cells,i)=>{var values=headers.Select((h,j)=>(h,j)).ToDictionary(p=>p.h,p=>p.j<cells.Length?cells[p.j]:"");var errors=new List<string>();foreach(var target in command.Mapping.Where(m=>m.Key.Equals("name",StringComparison.OrdinalIgnoreCase)||m.Key.Equals("code",StringComparison.OrdinalIgnoreCase))){if(!values.TryGetValue(target.Value,out var value)||string.IsNullOrWhiteSpace(value))errors.Add($"{target.Key} é obrigatório.");}if(cells.Length!=headers.Length)errors.Add("Quantidade de colunas diferente do cabeçalho.");return new ImportRow(i+2,errors.Count==0,values,errors.ToArray());}).ToArray();
  var token=Guid.NewGuid();await db.InTenantTransactionAsync(async(c,t)=>{
   await c.ExecuteAsync("insert into agro360.deployment_import_previews(token,tenant_id,type,file_name,mapping,rows,expires_at) values(@Token,@TenantId,@Type,@FileName,cast(@Mapping as jsonb),cast(@Rows as jsonb),now()+interval '2 hours')",new{Token=token,tenant.TenantId,command.Type,command.FileName,Mapping=JsonSerializer.Serialize(command.Mapping),Rows=JsonSerializer.Serialize(rows)},t);
   var errors=rows.SelectMany(r=>r.Errors.Select(message=>new{Line=r.Line,Message=message})).ToArray();
   if(errors.Length>0)await c.ExecuteAsync("insert into agro360.deployment_import_errors(tenant_id,preview_token,line_number,message) values(@TenantId,@Token,@Line,@Message)",errors.Select(e=>new{tenant.TenantId,Token=token,e.Line,e.Message}),t);
  },ct);
  return new ImportPreview(token,command.Type,command.FileName,rows.Count(r=>r.Valid),rows.Count(r=>!r.Valid),rows);
 }
 public Task<Guid> ConfirmImportAsync(Guid token,Guid actor,CancellationToken ct)=>db.InTenantTransactionAsync(async(c,t)=>{var preview=await c.QuerySingleOrDefaultAsync<(string Type,string FileName,string Rows)>("select type Type,file_name FileName,rows::text Rows from agro360.deployment_import_previews where token=@Token and tenant_id=@TenantId and expires_at>now()",new{Token=token,tenant.TenantId},t);if(preview==default)throw new KeyNotFoundException("Pré-visualização expirada ou inexistente.");var rows=JsonSerializer.Deserialize<ImportRow[]>(preview.Rows)??[];if(rows.Any(r=>!r.Valid))throw new ArgumentException("Corrija todas as linhas inválidas antes de confirmar.");var id=Guid.NewGuid();await c.ExecuteAsync("insert into agro360.deployment_imports(id,tenant_id,type,file_name,status,total_rows,valid_rows,invalid_rows,rows,created_by,confirmed_at) values(@Id,@TenantId,@Type,@FileName,'COMPLETED',@Total,@Total,0,cast(@Rows as jsonb),@Actor,now()); delete from agro360.deployment_import_previews where token=@Token and tenant_id=@TenantId",new{Id=id,tenant.TenantId,preview.Type,preview.FileName,Total=rows.Length,Rows=preview.Rows,Actor=actor,Token=token},t);return id;},ct);
 public Task<IReadOnlyList<ImportHistory>> ImportsAsync(CancellationToken ct)=>db.InTenantTransactionAsync(async(c,t)=>(IReadOnlyList<ImportHistory>)(await c.QueryAsync<ImportHistory>("select id,type,file_name FileName,status,total_rows TotalRows,valid_rows ValidRows,invalid_rows InvalidRows,created_at CreatedAt from agro360.deployment_imports where tenant_id=@TenantId order by created_at desc limit 100",new{tenant.TenantId},t)).ToArray(),ct);
 public Task RollbackImportAsync(Guid id,Guid actor,CancellationToken ct)=>db.InTenantTransactionAsync(async(c,t)=>{var n=await c.ExecuteAsync("update agro360.deployment_imports set status='ROLLED_BACK',rolled_back_at=now(),rolled_back_by=@Actor where id=@Id and tenant_id=@TenantId and status='COMPLETED'",new{id,tenant.TenantId,Actor=actor},t);if(n==0)throw new KeyNotFoundException("Importação concluída não encontrada.");},ct);
 public Task<DeploymentDashboard> DashboardAsync(CancellationToken ct)=>db.InSystemTransactionAsync(async(c,t)=>{var totals=await c.QuerySingleAsync<(int Done,int Pending,decimal Average)>("select count(*) filter(where progress=100)::int Done,count(*) filter(where progress<100)::int Pending,coalesce(avg(progress),0) Average from agro360.deployment_organization_progress",transaction:t);var modules=(await c.QueryAsync<string>("select module_code from agro360.deployment_organization_modules where enabled group by module_code order by count(*) desc limit 5",transaction:t)).ToArray();var segments=(await c.QueryAsync<string>("select segment from agro360.deployment_onboardings group by segment order by count(*) desc",transaction:t)).ToArray();var imports=(await c.QueryAsync<ImportHistory>("select id,type,file_name FileName,status,total_rows TotalRows,valid_rows ValidRows,invalid_rows InvalidRows,created_at CreatedAt from agro360.deployment_imports order by created_at desc limit 10",transaction:t)).ToArray();return new DeploymentDashboard(totals.Done,totals.Pending,totals.Average,modules,segments,imports.Sum(x=>x.InvalidRows),imports);},ct);
 public Task<ImplementationCenter> ImplementationCenterAsync(CancellationToken ct)=>db.InTenantTransactionAsync(async(c,t)=>
 {
  var p=new{tenant.TenantId};
  var tenantRow=await c.QuerySingleAsync<ImplementationTenantRow>(new CommandDefinition("""
   select t.name TenantName,coalesce(p.name,t.plan_code,'Não definido') PlanName
   from agro360.tenancy_tenants t
   left join agro360.platform_tenants pt on pt.id=t.id and pt.deleted_at is null
   left join agro360.platform_saas_plans p on p.id=pt.plan_id and p.deleted_at is null
   where t.id=@TenantId
   """,p,t,cancellationToken:ct));
  using var grid=await c.QueryMultipleAsync(new CommandDefinition("""
   select count(*)::int from agro360.identity_users where tenant_id=@TenantId and deleted_at is null;
   select count(*)::int from agro360.identity_roles where tenant_id=@TenantId;
   select count(*)::int from agro360.platform_tenant_modules where tenant_id=@TenantId and status in ('CONTRACTED','ACTIVE','TRIAL');
   select count(*)::int from agro360.geo_farms where tenant_id=@TenantId and deleted_at is null;
   select count(*)::int from agro360.deployment_checklist where tenant_id=@TenantId and required;
   select count(*)::int from agro360.deployment_checklist where tenant_id=@TenantId and required and completed;
   """,p,t,cancellationToken:ct));
  var users=await grid.ReadSingleAsync<int>();var profiles=await grid.ReadSingleAsync<int>();var modules=await grid.ReadSingleAsync<int>();
  var farms=await grid.ReadSingleAsync<int>();var required=await grid.ReadSingleAsync<int>();var completed=await grid.ReadSingleAsync<int>();
  var pending=new List<string>();var alerts=new List<string>();var actions=new List<ImplementationAction>();
  if(users<2){pending.Add("Convide ao menos um segundo usuário para evitar dependência de uma única conta.");actions.Add(new("Cadastrar usuários","Defina responsáveis e mantenha acessos individuais.","/Saas?view=users","HIGH"));}
  if(profiles==0){pending.Add("Configure perfis e permissões por função.");actions.Add(new("Configurar perfis","Aplique o menor privilégio para cada função.","/Saas?view=roles","HIGH"));}
  if(modules==0){alerts.Add("Nenhum módulo contratado/ativo foi associado ao cliente.");actions.Add(new("Revisar módulos","Confira plano, módulos e feature flags.","/Saas?view=features","CRITICAL"));}
  if(farms==0){pending.Add("Cadastre a primeira fazenda ou unidade operacional.");actions.Add(new("Cadastrar fazenda","Informe dados cadastrais e área da unidade.","/Agriculture","HIGH"));}
  if(required>completed){alerts.Add($"{required-completed} etapa(s) obrigatória(s) do checklist ainda estão pendentes.");actions.Add(new("Concluir checklist","Revise as etapas obrigatórias de implantação.","/Deployment?panel=checklist","MEDIUM"));}
  if(actions.Count==0)actions.Add(new("Iniciar operação","A implantação essencial está concluída; registre as primeiras operações.","/","LOW"));
  var baseProgress=required==0?0:(int)Math.Round(completed*100m/required);
  var readiness=(users>0?20:0)+(profiles>0?20:0)+(modules>0?20:0)+(farms>0?20:0)+(required==0?0:baseProgress/5);
  return new ImplementationCenter(tenantRow.TenantName,tenantRow.PlanName,Math.Clamp(readiness,0,100),users,profiles,modules,farms,pending,alerts,actions);
 },ct);

 private sealed class ImplementationTenantRow { public string TenantName {get;init;}=string.Empty; public string PlanName {get;init;}=string.Empty; }
}
