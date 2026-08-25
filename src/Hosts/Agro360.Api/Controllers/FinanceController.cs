using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/finance"), Authorize]
public sealed class FinanceController(IFinanceService service, ILogger<FinanceController> logger) : ControllerBase
{
    [HttpGet("chart-of-accounts"), Authorize(Policy=Permissions.FinanceRead)] public Task<IReadOnlyList<dynamic>> Accounts(CancellationToken ct)=>service.ListAccountsAsync(ct);
    [HttpPost("chart-of-accounts"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> AddAccount(AccountCommand x,CancellationToken ct)=>Created("api/finance/chart-of-accounts",new{id=await service.SaveAccountAsync(null,x,ct)});
    [HttpPut("chart-of-accounts/{id:guid}"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> EditAccount(Guid id,AccountCommand x,CancellationToken ct){await service.SaveAccountAsync(id,x,ct);return NoContent();}
    [HttpGet("cost-centers"), Authorize(Policy=Permissions.FinanceRead)] public Task<IReadOnlyList<dynamic>> CostCenters(CancellationToken ct)=>service.ListCostCentersAsync(ct);
    [HttpPost("cost-centers"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> AddCostCenter(CostCenterCommand x,CancellationToken ct)=>Created("api/finance/cost-centers",new{id=await service.SaveCostCenterAsync(null,x,ct)});
    [HttpPut("cost-centers/{id:guid}"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> EditCostCenter(Guid id,CostCenterCommand x,CancellationToken ct){await service.SaveCostCenterAsync(id,x,ct);return NoContent();}
    [HttpGet("payables"), Authorize(Policy=Permissions.FinanceRead)] public Task<IReadOnlyList<dynamic>> Payables(CancellationToken ct)=>service.ListTitlesAsync(true,ct);
    [HttpGet("payables/{id:guid}"), Authorize(Policy=Permissions.FinanceRead)] public async Task<IActionResult> Payable(Guid id,CancellationToken ct)=>Ok(await service.GetTitleAsync(true,id,ct));
    [HttpPost("payables"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> AddPayable(TitleCommand x,CancellationToken ct)=>Created("api/finance/payables",new{id=await service.SaveTitleAsync(true,null,x,ct)});
    [HttpPut("payables/{id:guid}"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> EditPayable(Guid id,TitleCommand x,CancellationToken ct){await service.SaveTitleAsync(true,id,x,ct);return NoContent();}
    [HttpPost("payables/{id:guid}/pay"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> Pay(Guid id,SettlementCommand x,CancellationToken ct){await service.SettleAsync(true,id,x,ct);return NoContent();}
    [HttpPost("payables/{id:guid}/cancel"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> CancelPayable(Guid id,CancelFinanceCommand x,CancellationToken ct){await service.CancelAsync(true,id,x,ct);return NoContent();}
    [HttpGet("receivables"), Authorize(Policy=Permissions.FinanceRead)] public Task<IReadOnlyList<dynamic>> Receivables(CancellationToken ct)=>service.ListTitlesAsync(false,ct);
    [HttpGet("receivables/{id:guid}"), Authorize(Policy=Permissions.FinanceRead)] public async Task<IActionResult> Receivable(Guid id,CancellationToken ct)=>Ok(await service.GetTitleAsync(false,id,ct));
    [HttpPost("receivables"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> AddReceivable(TitleCommand x,CancellationToken ct)=>Created("api/finance/receivables",new{id=await service.SaveTitleAsync(false,null,x,ct)});
    [HttpPut("receivables/{id:guid}"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> EditReceivable(Guid id,TitleCommand x,CancellationToken ct){await service.SaveTitleAsync(false,id,x,ct);return NoContent();}
    [HttpPost("receivables/{id:guid}/receive"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> Receive(Guid id,SettlementCommand x,CancellationToken ct){await service.SettleAsync(false,id,x,ct);return NoContent();}
    [HttpPost("receivables/{id:guid}/cancel"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> CancelReceivable(Guid id,CancelFinanceCommand x,CancellationToken ct){await service.CancelAsync(false,id,x,ct);return NoContent();}
    [HttpPost("manual-entries"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> Manual(ManualEntryCommand x,CancellationToken ct)=>Created("api/finance/manual-entries",new{id=await service.AddManualEntryAsync(x,ct)});
    [HttpGet("cash-flow"), Authorize(Policy=Permissions.FinanceRead)] public Task<IReadOnlyList<dynamic>> CashFlow([FromQuery]FinanceQuery q,CancellationToken ct)=>service.CashFlowAsync(q,ct);
    [HttpGet("results"), Authorize(Policy=Permissions.FinanceRead)] public Task<IReadOnlyList<dynamic>> Results([FromQuery]FinanceQuery q,CancellationToken ct)=>service.ResultsAsync(q,ct);
    [HttpGet("dashboard"), Authorize(Policy=Permissions.FinanceRead)] public async Task<IActionResult> Dashboard(CancellationToken ct){logger.LogInformation("Consultando dashboard financeiro");return Ok(await service.DashboardAsync(ct));}
}
