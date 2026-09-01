using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Agro360.Api.Controllers;

[ApiController,Route("api/crm"),Authorize(Policy=Permissions.CrmRead)]
public sealed class CrmSaasController(ICrmSaasService service):ControllerBase
{
 [HttpGet("lookups/plans")] public Task<IReadOnlyList<SupportLookup>> Plans(CancellationToken ct)=>service.PlansAsync(ct);
 [HttpGet("leads")] public Task<IReadOnlyList<CrmLead>> Leads(string? search,CancellationToken ct)=>service.LeadsAsync(search,ct);
 [HttpGet("leads/duplicates")] public Task<IReadOnlyList<DuplicateLead>> Duplicates(string? document,string? email,CancellationToken ct)=>service.DuplicatesAsync(document,email,ct);
 [HttpPost("leads"),Authorize(Policy=Permissions.CrmWrite)] public async Task<IActionResult> Lead(LeadCommand x,CancellationToken ct){var id=await service.CreateLeadAsync(x,ct);return Created($"/api/crm/leads/{id}",new{id});}
 [HttpGet("opportunities")] public Task<IReadOnlyList<CrmOpportunity>> Opportunities(CancellationToken ct)=>service.OpportunitiesAsync(ct);
 [HttpPost("opportunities"),Authorize(Policy=Permissions.CrmWrite)] public async Task<IActionResult> Opportunity(OpportunityCommand x,CancellationToken ct){var id=await service.CreateOpportunityAsync(x,ct);return Created($"/api/crm/opportunities/{id}",new{id});}
 [HttpPost("opportunities/{id:guid}/transition"),Authorize(Policy=Permissions.CrmWrite)] public async Task<IActionResult> Opportunity(Guid id,OpportunityTransition x,CancellationToken ct)=>Ok(new{tenantId=await service.TransitionOpportunityAsync(id,x,ct)});
 [HttpGet("proposals")] public Task<IReadOnlyList<CommercialProposal>> Proposals(CancellationToken ct)=>service.ProposalsAsync(ct);
 [HttpPost("proposals"),Authorize(Policy=Permissions.CommercialSaasWrite)] public async Task<IActionResult> Proposal(ProposalCommand x,CancellationToken ct){var id=await service.CreateProposalAsync(x,ct);return Created($"/api/crm/proposals/{id}",new{id});}
 [HttpPost("proposals/{id:guid}/transition"),Authorize(Policy=Permissions.CommercialSaasWrite)] public async Task<IActionResult> Proposal(Guid id,ProposalTransition x,CancellationToken ct){await service.TransitionProposalAsync(id,x,ct);return NoContent();}
 [HttpPost("proposals/{id:guid}/contract"),Authorize(Policy=Permissions.CommercialSaasWrite)] public async Task<IActionResult> Contract(Guid id,DateOnly startsOn,DateOnly endsOn,CancellationToken ct){var contract=await service.CreateContractAsync(id,startsOn,endsOn,ct);return Created($"/api/crm/contracts/{contract}",new{id=contract});}
 [HttpGet("contracts")] public Task<IReadOnlyList<SaasContract>> Contracts(CancellationToken ct)=>service.ContractsAsync(ct);
 [HttpPost("contracts/{id:guid}/suspend"),Authorize(Policy=Permissions.CommercialSaasWrite)] public async Task<IActionResult> Suspend(Guid id,[FromBody]string reason,CancellationToken ct){await service.SuspendContractAsync(id,reason,ct);return NoContent();}
 [HttpGet("customer-success/health"),Authorize(Policy=Permissions.CustomerSuccessRead)] public Task<IReadOnlyList<CustomerHealth>> Health(CancellationToken ct)=>service.HealthAsync(ct);
}
