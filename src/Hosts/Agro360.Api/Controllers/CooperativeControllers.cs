using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController,Authorize(Policy=Permissions.CooperativeRead)]
public abstract class CooperativeControllerBase(ICooperativeService service):ControllerBase
{
 protected ICooperativeService Service { get; }=service;
 protected async Task<IActionResult> Create(CooperativeCommand x,CancellationToken ct){var id=await Service.SaveAsync(null,x,ct);return Created($"{Request.Path}/{id}",new{id});}
 [HttpPut("{id:guid}"),Authorize(Policy=Permissions.CooperativeWrite)]public async Task<IActionResult> Update(Guid id,CooperativeCommand x,CancellationToken ct){await Service.SaveAsync(id,x,ct);return NoContent();}
 [HttpPost("{id:guid}/close"),Authorize(Policy=Permissions.CooperativeWrite)]public async Task<IActionResult> Close(Guid id,CancellationToken ct){await Service.ChangeStatusAsync(id,"CLOSED",ct);return NoContent();}
}
[Route("api/cooperative")]
public sealed class CooperativeController(ICooperativeService service,ILogger<CooperativeController> logger):CooperativeControllerBase(service)
{
 [HttpGet("members")]public Task<IReadOnlyList<CooperativeRecord>> Members(CancellationToken ct)=>Service.ListAsync("MEMBER",null,ct);
 [HttpPost("members"),Authorize(Policy=Permissions.CooperativeWrite)]public async Task<IActionResult> Member(MemberCommand x,CancellationToken ct)=>Created($"api/cooperative/members/{await Boundary(()=>Service.AddMemberAsync(x,ct))}",null);
 [HttpGet("member-classifications")]public Task<IReadOnlyList<CooperativeRecord>> Classifications(CancellationToken ct)=>Service.ListAsync("MEMBER_CLASSIFICATION",null,ct);
 [HttpGet("dashboard")]public Task<CooperativeDashboard> Dashboard(CancellationToken ct)=>Service.DashboardAsync(ct);
 private async Task<T> Boundary<T>(Func<Task<T>> action){try{return await action();}catch(Exception ex){ApiLogMessages.CooperativeBoundaryFailed(logger,ex);throw;}}
}
[Route("api/technical-assistance")]
public sealed class TechnicalAssistanceController(ICooperativeService s):CooperativeControllerBase(s){[HttpGet("visits")]public Task<IReadOnlyList<CooperativeRecord>> Visits(CancellationToken ct)=>Service.ListAsync("VISIT",null,ct);[HttpPost("visits")]public Task<IActionResult> Visit(CooperativeCommand x,CancellationToken ct)=>Create(x with{Kind="VISIT"},ct);[HttpGet("recommendations")]public Task<IReadOnlyList<CooperativeRecord>> Recommendations(CancellationToken ct)=>Service.ListAsync("RECOMMENDATION",null,ct);[HttpPost("recommendations")]public Task<IActionResult> Recommendation(CooperativeCommand x,CancellationToken ct)=>Create(x with{Kind="RECOMMENDATION"},ct);[HttpPost("action-plans")]public Task<IActionResult> Plan(CooperativeCommand x,CancellationToken ct)=>Create(x with{Kind="ACTION_PLAN"},ct);}
[Route("api/production-programs")]
public sealed class ProductionProgramsController(ICooperativeService s):CooperativeControllerBase(s){[HttpGet]public Task<IReadOnlyList<CooperativeRecord>> List(CancellationToken ct)=>Service.ListAsync("PROGRAM",null,ct);[HttpPost]public Task<IActionResult> Add(CooperativeCommand x,CancellationToken ct)=>Create(x with{Kind="PROGRAM"},ct);[HttpGet("{id:guid}/members")]public Task<IReadOnlyList<CooperativeRecord>> Members(Guid id,CancellationToken ct)=>Service.ListAsync("PROGRAM_MEMBER",null,ct);}
[Route("api/marketplace")]
public sealed class MarketplaceController(ICooperativeService s):CooperativeControllerBase(s){[HttpGet("offers")]public Task<IReadOnlyList<CooperativeRecord>> Offers(CancellationToken ct)=>Service.ListAsync("OFFER",null,ct);[HttpPost("offers")]public Task<IActionResult> Offer(CooperativeCommand x,CancellationToken ct)=>Create(x with{Kind="OFFER"},ct);[HttpGet("demands")]public Task<IReadOnlyList<CooperativeRecord>> Demands(CancellationToken ct)=>Service.ListAsync("DEMAND",null,ct);[HttpPost("demands")]public Task<IActionResult> Demand(CooperativeCommand x,CancellationToken ct)=>Create(x with{Kind="DEMAND"},ct);[HttpPost("negotiations")]public Task<IActionResult> Negotiation(CooperativeCommand x,CancellationToken ct)=>Create(x with{Kind="NEGOTIATION"},ct);}
[Route("api/collective-purchases")]
public sealed class CollectivePurchasesController(ICooperativeService s):CooperativeControllerBase(s){[HttpGet]public Task<IReadOnlyList<CooperativeRecord>> List(CancellationToken ct)=>Service.ListAsync("COLLECTIVE_PURCHASE",null,ct);[HttpPost]public async Task<IActionResult> Add(CollectivePurchaseCommand x,CancellationToken ct){var id=await Service.AddCollectivePurchaseAsync(x,ct);return Created($"api/collective-purchases/{id}",new{id});}[HttpPost("{id:guid}/approve"),Authorize(Policy=Permissions.CooperativeApprove)]public async Task<IActionResult> Approve(Guid id,CancellationToken ct){await Service.ChangeStatusAsync(id,"APPROVED",ct);return NoContent();}}
[Route("api/agro-contracts")]
public sealed class AgroContractsController(ICooperativeService s):CooperativeControllerBase(s){[HttpGet]public Task<IReadOnlyList<CooperativeRecord>> List(CancellationToken ct)=>Service.ListAsync("CONTRACT",null,ct);[HttpPost]public Task<IActionResult> Add(CooperativeCommand x,CancellationToken ct)=>Create(x with{Kind="CONTRACT"},ct);[HttpPost("{id:guid}/activate")]public async Task<IActionResult> Activate(Guid id,CancellationToken ct){await Service.ChangeStatusAsync(id,"ACTIVE",ct);return NoContent();}[HttpPost("{id:guid}/cancel")]public async Task<IActionResult> Cancel(Guid id,CancellationToken ct){await Service.ChangeStatusAsync(id,"CANCELLED",ct);return NoContent();}}
[Route("api/bonuses")]
public sealed class BonusesController(ICooperativeService s):CooperativeControllerBase(s){[HttpGet("rules")]public Task<IReadOnlyList<CooperativeRecord>> Rules(CancellationToken ct)=>Service.ListAsync("BONUS_RULE",null,ct);[HttpPost("rules")]public Task<IActionResult> Rule(CooperativeCommand x,CancellationToken ct)=>Create(x with{Kind="BONUS_RULE"},ct);[HttpGet("settlements")]public Task<IReadOnlyList<CooperativeRecord>> Settlements(CancellationToken ct)=>Service.ListAsync("SETTLEMENT",null,ct);}
[Route("api/rural-credit/pre-analyses")]
public sealed class RuralCreditController(ICooperativeService s):CooperativeControllerBase(s){[HttpGet]public Task<IReadOnlyList<CooperativeRecord>> List(CancellationToken ct)=>Service.ListAsync("CREDIT_PREANALYSIS",null,ct);[HttpPost]public Task<IActionResult> Add(CooperativeCommand x,CancellationToken ct)=>Create(x with{Kind="CREDIT_PREANALYSIS"},ct);}
[ApiController,Route("api/producer-portal/dashboard"),Authorize(Policy=Permissions.CooperativeRead)]public sealed class ProducerPortalController(ICooperativeService s):ControllerBase{[HttpGet]public Task<object> Dashboard(CancellationToken ct)=>s.ProducerDashboardAsync(ct);}
