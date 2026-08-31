using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Agro360.Api.Controllers;
[ApiController,Route("api/export-trading"),Authorize]
public sealed class ExportTradingController(IExportTradingService service):ControllerBase
{
 [HttpGet("dashboard"),Authorize(Policy=Permissions.ExportRead)] public async Task<IActionResult> Dashboard(CancellationToken ct)=>Ok(await service.DashboardAsync(ct));
 [HttpGet("customers"),Authorize(Policy=Permissions.ExportRead)] public Task<IReadOnlyList<dynamic>> Customers([FromQuery]ExportQuery q,CancellationToken ct)=>service.CustomersAsync(q,ct);
 [HttpPost("customers"),Authorize(Policy=Permissions.ExportWrite)] public async Task<IActionResult> Customer(ExportCustomerCommand x,CancellationToken ct)=>Created("api/export-trading/customers",new{id=await service.SaveCustomerAsync(null,x,ct)});
 [HttpPut("customers/{id:guid}"),Authorize(Policy=Permissions.ExportWrite)] public async Task<IActionResult> Customer(Guid id,ExportCustomerCommand x,CancellationToken ct){await service.SaveCustomerAsync(id,x,ct);return NoContent();}
 [HttpGet("contracts"),Authorize(Policy=Permissions.ExportRead)] public Task<IReadOnlyList<dynamic>> Contracts([FromQuery]ExportQuery q,CancellationToken ct)=>service.ContractsAsync(q,ct);
 [HttpPost("contracts"),Authorize(Policy=Permissions.ExportWrite)] public async Task<IActionResult> Contract(ExportContractCommand x,CancellationToken ct)=>Created("api/export-trading/contracts",new{id=await service.CreateContractAsync(x,ct)});
 [HttpPost("contracts/{id:guid}/approve"),Authorize(Policy=Permissions.ExportApprove)] public async Task<IActionResult> Approve(Guid id,CancellationToken ct){await service.ApproveContractAsync(id,ct);return NoContent();}
 [HttpPost("contracts/{id:guid}/cancel"),Authorize(Policy=Permissions.ExportApprove)] public async Task<IActionResult> Cancel(Guid id,[FromBody]string reason,CancellationToken ct){await service.CancelContractAsync(id,reason,ct);return NoContent();}
 [HttpGet("shipments"),Authorize(Policy=Permissions.ExportRead)] public Task<IReadOnlyList<dynamic>> Shipments([FromQuery]ExportQuery q,CancellationToken ct)=>service.ShipmentsAsync(q,ct);
 [HttpPost("shipments"),Authorize(Policy=Permissions.ExportWrite)] public async Task<IActionResult> Shipment(ExportShipmentCommand x,CancellationToken ct)=>Created("api/export-trading/shipments",new{id=await service.CreateShipmentAsync(x,ct)});
 [HttpGet("reports/{report}.csv"),Authorize(Policy=Permissions.ExportReports)] public async Task<IActionResult> Report(string report,[FromQuery]ExportQuery q,CancellationToken ct)=>File(await service.ExportCsvAsync(report,q,ct),"text/csv; charset=utf-8",$"exportacao-{report}-{DateTime.UtcNow:yyyyMMdd}.csv");
}
