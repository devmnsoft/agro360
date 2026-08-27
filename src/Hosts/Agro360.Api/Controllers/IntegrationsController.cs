using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController,Authorize(Policy=Permissions.IntegrationsRead)]
public sealed class IntegrationsController(IIntegrationService service,ILogger<IntegrationsController> logger):ControllerBase
{
 [HttpGet("api/integrations")]public Task<IReadOnlyList<IntegrationItem>> List(CancellationToken ct)=>service.ListAsync(ct);
 [HttpPost("api/integrations"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<IActionResult> Create(IntegrationCommand x,CancellationToken ct)=>Created(()=>service.SaveAsync(null,x,ct),"api/integrations");
 [HttpPut("api/integrations/{id:guid}"),Authorize(Policy=Permissions.IntegrationsWrite)]public async Task<IActionResult> Update(Guid id,IntegrationCommand x,CancellationToken ct){await Boundary("update integration",()=>service.SaveAsync(id,x,ct));return NoContent();}
 [HttpPost("api/integrations/{id:guid}/activate"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<IActionResult> Activate(Guid id,CancellationToken ct)=>Action(()=>service.SetStatusAsync(id,true,ct),"activate");
 [HttpPost("api/integrations/{id:guid}/pause"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<IActionResult> Pause(Guid id,CancellationToken ct)=>Action(()=>service.SetStatusAsync(id,false,ct),"pause");
 [HttpGet("api/integrations/{id:guid}/logs")]public Task<object> Logs(Guid id,CancellationToken ct)=>service.LogsAsync(id,ct);
 [HttpGet("api/api-keys")]public Task<object> Keys(CancellationToken ct)=>service.ApiKeysAsync(ct);
 [HttpPost("api/api-keys"),Authorize(Policy=Permissions.IntegrationsWrite)]public async Task<IActionResult> Key(ApiKeyCommand x,CancellationToken ct)=>Ok(await Boundary("create API key",()=>service.CreateApiKeyAsync(x,ct)));
 [HttpPost("api/api-keys/{id:guid}/revoke"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<IActionResult> RevokeKey(Guid id,CancellationToken ct)=>Action(()=>service.RevokeApiKeyAsync(id,ct),"revoke API key");
 [HttpGet("api/webhooks")]public Task<object> Webhooks(CancellationToken ct)=>service.WebhooksAsync(ct);
 [HttpPost("api/webhooks"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<IActionResult> Webhook(WebhookCommand x,CancellationToken ct)=>Created(()=>service.CreateWebhookAsync(x,ct),"api/webhooks");
 [HttpGet("api/webhooks/events")]public Task<object> Events(CancellationToken ct)=>service.WebhookEventsAsync(ct);
 [HttpPost("api/webhooks/events/{id:guid}/retry"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<IActionResult> Retry(Guid id,CancellationToken ct)=>Action(()=>service.RetryWebhookAsync(id,ct),"retry webhook");
 [HttpPost("api/webhooks/events/{id:guid}/cancel"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<IActionResult> Cancel(Guid id,CancellationToken ct)=>Action(()=>service.CancelWebhookAsync(id,ct),"cancel webhook");
 [HttpGet("api/imports")]public Task<object> Imports(CancellationToken ct)=>service.ImportsAsync(ct);
 [HttpPost("api/imports"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<IActionResult> Import(ImportCommand x,CancellationToken ct)=>Created(()=>service.CreateImportAsync(x,ct),"api/imports");
 [HttpPost("api/imports/{id:guid}/validate"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<object> Validate(Guid id,CancellationToken ct)=>service.ValidateImportAsync(id,ct);
 [HttpPost("api/imports/{id:guid}/confirm"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<IActionResult> Confirm(Guid id,CancellationToken ct)=>Action(()=>service.ConfirmImportAsync(id,ct),"confirm import");
 [HttpGet("api/exports")]public async Task<IActionResult> Export([FromQuery]string entity,CancellationToken ct)=>File(await service.ExportAsync(entity,ct),"text/csv; charset=utf-8",$"{entity}.csv");
 [HttpGet("api/fiscal-documents")]public Task<object> Documents(CancellationToken ct)=>service.FiscalDocumentsAsync(ct);
 [HttpPost("api/fiscal-documents"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<IActionResult> Document(FiscalDocumentCommand x,CancellationToken ct)=>Created(()=>service.AddFiscalDocumentAsync(x,ct),"api/fiscal-documents");
 [HttpGet("api/iot/devices")]public Task<object> Devices(CancellationToken ct)=>service.DevicesAsync(ct);
 [HttpPost("api/iot/devices"),Authorize(Policy=Permissions.IntegrationsWrite)]public async Task<IActionResult> Device(DeviceCommand x,CancellationToken ct)=>Ok(await service.AddDeviceAsync(x,ct));
 [HttpPost("api/iot/readings"),AllowAnonymous]public async Task<IActionResult> Reading(ReadingCommand x,CancellationToken ct)=>Accepted(new{id=await Boundary("ingest reading",()=>service.AddReadingAsync(x,ct))});
 [HttpGet("api/payments/splits")]public Task<object> Splits(CancellationToken ct)=>service.SplitsAsync(ct);
 [HttpPost("api/payments/splits"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<IActionResult> Split(SplitCommand x,CancellationToken ct)=>Created(()=>service.CalculateSplitAsync(x,ct),"api/payments/splits");
 [HttpPost("api/payments/splits/{id:guid}/approve"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<IActionResult> Approve(Guid id,CancellationToken ct)=>Action(()=>service.ApproveSplitAsync(id,ct),"approve split");
 [HttpGet("api/messages/outbox")]public Task<object> Outbox(CancellationToken ct)=>service.OutboxAsync(ct);
 [HttpPost("api/messages/outbox"),Authorize(Policy=Permissions.IntegrationsWrite)]public Task<IActionResult> Message(MessageCommand x,CancellationToken ct)=>Created(()=>service.EnqueueMessageAsync(x,ct),"api/messages/outbox");
 [HttpGet("api/integrations/dashboard")]public Task<IntegrationDashboard> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
 private async Task<IActionResult> Created(Func<Task<Guid>> op,string route){var id=await Boundary("create",op);return base.Created(route+"/"+id,new{id});}
 private async Task<IActionResult> Action(Func<Task> op,string name){await Boundary(name,op);return NoContent();}
 private async Task<T> Boundary<T>(string operation,Func<Task<T>> op){try{return await op();}catch(Exception ex){logger.LogError(ex,"Integration boundary failed: {Operation}",operation);throw;}}
 private async Task Boundary(string operation,Func<Task> op){try{await op();}catch(Exception ex){logger.LogError(ex,"Integration boundary failed: {Operation}",operation);throw;}}
}
