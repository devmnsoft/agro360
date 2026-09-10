using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Authorize]
public sealed class Livestock360Controller(ILivestock360Service service, ILivestockService animals, ILivestockHerdService herd) : ControllerBase
{
    [HttpGet("api/livestock/animals"), Authorize(Policy = Permissions.LivestockRead)] public Task<PagedResult<AnimalDto>> Animals(Guid? farmId, int page = 1, int pageSize = 25, string? search = null, CancellationToken ct = default) => animals.ListAnimalsAsync(farmId, page, pageSize, search, ct);
    [HttpPost("api/livestock/animals"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Animal(RegisterAnimalCommand x, CancellationToken ct) { var a = await animals.RegisterAnimalAsync(x, ct); return Created($"/api/livestock/animals/{a.Id}", a); }
    [HttpGet("api/livestock/animals/{id:guid}"), Authorize(Policy = Permissions.LivestockRead)] public async Task<IActionResult> Animal(Guid id, CancellationToken ct) => await service.GetAnimalAsync(id, ct) is { } a ? Ok(a) : NotFound();
    [HttpPut("api/livestock/animals/{id:guid}"), Authorize(Policy = Permissions.LivestockWrite)] public Task<AnimalDto> Animal(Guid id, RegisterAnimalCommand x, CancellationToken ct) => service.UpdateAnimalAsync(id, x, ct);
    [HttpPost("api/livestock/animals/{id:guid}/death"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> RegisterDeath(Guid id, AnimalStatusCommand x, CancellationToken ct) { await service.ChangeAnimalStatusAsync(id, "DEAD", x, ct); return NoContent(); }
    [HttpPost("api/livestock/animals/{id:guid}/sale"), Authorize(Policy = Permissions.LivestockSell)] public async Task<IActionResult> RegisterSale(Guid id, AnimalStatusCommand x, CancellationToken ct) { await service.ChangeAnimalStatusAsync(id, "SOLD", x, ct); return NoContent(); }
    [HttpPost("api/livestock/animals/{id:guid}/transfer"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Transfer(Guid id, AnimalTransferCommand x, CancellationToken ct) { await service.TransferAnimalAsync(id, x, ct); return NoContent(); }
    [HttpGet("api/livestock/herds"), Authorize(Policy = Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Herds(CancellationToken ct) => service.ListHerdsAsync(ct);
    [HttpPost("api/livestock/herds"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Herd(HerdCommand x, CancellationToken ct) { var id = await service.SaveHerdAsync(null, x, ct); return Created($"/api/livestock/herds/{id}", new { id }); }
    [HttpPut("api/livestock/herds/{id:guid}"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Herd(Guid id, HerdCommand x, CancellationToken ct) { await service.SaveHerdAsync(id, x, ct); return NoContent(); }
    [HttpGet("api/pastures"), Authorize(Policy = Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Pastures(CancellationToken ct) => service.ListPasturesAsync(ct);
    [HttpPost("api/pastures"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Pasture(PastureCommand x, CancellationToken ct) { var id = await service.SavePastureAsync(null, x, ct); return Created($"/api/pastures/{id}", new { id }); }
    [HttpPut("api/pastures/{id:guid}"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Pasture(Guid id, PastureCommand x, CancellationToken ct) { await service.SavePastureAsync(id, x, ct); return NoContent(); }
    [HttpGet("api/pastures/paddocks"), Authorize(Policy = Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Paddocks(CancellationToken ct) => service.ListPaddocksAsync(ct);
    [HttpPost("api/pastures/paddocks"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Paddock(PaddockCommand x, CancellationToken ct) { var id = await service.SavePaddockAsync(null, x, ct); return Created($"/api/pastures/paddocks/{id}", new { id }); }
    [HttpPut("api/pastures/paddocks/{id:guid}"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Paddock(Guid id, PaddockCommand x, CancellationToken ct) { await service.SavePaddockAsync(id, x, ct); return NoContent(); }
    [HttpPost("api/pastures/paddocks/{id:guid}/occupy"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> OccupyPaddock(Guid id, PaddockMovementCommand x, CancellationToken ct) { await service.MovePaddockAsync(id, true, x, ct); return NoContent(); }
    [HttpPost("api/pastures/paddocks/{id:guid}/release"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> ReleasePaddock(Guid id, PaddockMovementCommand x, CancellationToken ct) { await service.MovePaddockAsync(id, false, x, ct); return NoContent(); }
    [HttpGet("api/livestock/handling-events"), Authorize(Policy = Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Handling(CancellationToken ct) => service.ListEventsAsync("handling", ct);
    [HttpPost("api/livestock/handling-events"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Handling(HandlingEventCommand x, CancellationToken ct) { var id = await service.AddHandlingAsync(x, ct); return Created($"/api/livestock/handling-events/{id}", new { id }); }
    [HttpGet("api/livestock/health-events"), Authorize(Policy = Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Health(CancellationToken ct) => service.ListEventsAsync("health", ct);
    [HttpPost("api/livestock/health-events"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Health(HealthEventCommand x, CancellationToken ct) { var id = await service.AddHealthAsync(x, ct); return Created($"/api/livestock/health-events/{id}", new { id }); }
    [HttpGet("api/livestock/reproduction-events"), Authorize(Policy = Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Reproduction(CancellationToken ct) => service.ListEventsAsync("reproduction", ct);
    [HttpPost("api/livestock/reproduction-events"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Reproduction(ReproductionEventCommand x, CancellationToken ct) { var id = await service.AddReproductionAsync(x, ct); return Created($"/api/livestock/reproduction-events/{id}", new { id }); }
    [HttpGet("api/livestock/nutrition-plans"), Authorize(Policy = Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Plans(CancellationToken ct) => service.ListNutritionPlansAsync(ct);
    [HttpPost("api/livestock/nutrition-plans"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Plan(NutritionPlanCommand x, CancellationToken ct) { var id = await service.AddNutritionPlanAsync(x, ct); return Created($"/api/livestock/nutrition-plans/{id}", new { id }); }
    [HttpGet("api/livestock/feedings"), Authorize(Policy = Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Feedings(CancellationToken ct) => service.ListFeedingsAsync(ct);
    [HttpPost("api/livestock/feedings"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Feeding(FeedingCommand x, CancellationToken ct) { var id = await service.AddFeedingAsync(x, ct); return Created($"/api/livestock/feedings/{id}", new { id }); }
    [HttpGet("api/livestock/production/milk"), Authorize(Policy = Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Milk(CancellationToken ct) => service.ListMilkAsync(ct);
    [HttpPost("api/livestock/production/milk"), Authorize(Policy = Permissions.LivestockWrite)] public async Task<IActionResult> Milk(MilkProductionCommand x, CancellationToken ct) { var id = await service.AddMilkAsync(x, ct); return Created($"/api/livestock/production/milk/{id}", new { id }); }
    [HttpGet("api/livestock/production/weight-gain"), Authorize(Policy = Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Gain(CancellationToken ct) => service.WeightGainAsync(ct);
    [HttpGet("api/livestock/dashboard"), Authorize(Policy = Permissions.DashboardRead)] public Task<LivestockDashboardDto> Dashboard(CancellationToken ct) => service.DashboardAsync(ct);

    [HttpGet("api/livestock/lookups/{resource}"), Authorize(Policy = Permissions.LivestockRead)]
    public Task<PagedResult<LookupItem>> LivestockLookups(string resource, string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        herd.LookupsAsync(resource, search, page, pageSize, ct);

    [HttpGet("api/livestock/catalog/{kind}"), Authorize(Policy = Permissions.LivestockRead)]
    public Task<IReadOnlyList<dynamic>> Catalog(string kind, CancellationToken ct) => herd.ListCatalogAsync(kind, ct);
    [HttpPost("api/livestock/catalog/{kind}"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> Catalog(string kind, LivestockCatalogCommand x, CancellationToken ct) { var id = await herd.SaveCatalogAsync(kind, null, x, ct); return Created($"/api/livestock/catalog/{kind}/{id}", new { id }); }

    [HttpGet("api/livestock/facilities"), Authorize(Policy = Permissions.LivestockRead)]
    public Task<IReadOnlyList<dynamic>> Facilities(Guid? farmId, CancellationToken ct) => herd.ListFacilitiesAsync(farmId, ct);
    [HttpPost("api/livestock/facilities"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> Facility(FacilityCommand x, CancellationToken ct) { var id = await herd.SaveFacilityAsync(null, x, ct); return Created($"/api/livestock/facilities/{id}", new { id }); }
    [HttpPut("api/livestock/facilities/{id:guid}"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> Facility(Guid id, FacilityCommand x, CancellationToken ct) { await herd.SaveFacilityAsync(id, x, ct); return NoContent(); }

    [HttpGet("api/livestock/handling-lots"), Authorize(Policy = Permissions.LivestockRead)]
    public Task<IReadOnlyList<dynamic>> HandlingLots(Guid? farmId, CancellationToken ct) => herd.ListHandlingLotsAsync(farmId, ct);
    [HttpPost("api/livestock/handling-lots"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> HandlingLot(HandlingLotCommand x, CancellationToken ct) { var id = await herd.SaveHandlingLotAsync(null, x, ct); return Created($"/api/livestock/handling-lots/{id}", new { id }); }
    [HttpPost("api/livestock/herds/reconcile"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> Reconcile(ReconcileHerdCommand x, CancellationToken ct) { await herd.ReconcileHerdAsync(x, ct); return NoContent(); }

    [HttpGet("api/livestock/animals/{id:guid}/detail"), Authorize(Policy = Permissions.LivestockRead)]
    public async Task<IActionResult> AnimalDetail(Guid id, CancellationToken ct) => await herd.AnimalDetailAsync(id, ct) is { } detail ? Ok(detail) : NotFound();
    [HttpPost("api/livestock/animals/{id:guid}/tag"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> ChangeTag(Guid id, ChangeTagCommand x, CancellationToken ct) { await herd.ChangeTagAsync(id, x, ct); return NoContent(); }
    [HttpPost("api/livestock/animals/{id:guid}/inactivate"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> Inactivate(Guid id, InactivateAnimalCommand x, CancellationToken ct) { await herd.InactivateAsync(id, x, ct); return NoContent(); }
    [HttpPost("api/livestock/animals/{id:guid}/archive"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> Archive(Guid id, InactivateAnimalCommand x, CancellationToken ct) { await herd.SoftDeleteAsync(id, x.Reason, ct); return NoContent(); }

    [HttpGet("api/livestock/movements"), Authorize(Policy = Permissions.LivestockRead)]
    public Task<IReadOnlyList<dynamic>> Movements(Guid? farmId, string? kind, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        herd.ListMovementsAsync(farmId, kind, from, to, ct);
    [HttpPost("api/livestock/movements"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> Movement(HerdMovementCommand x, CancellationToken ct) { var id = await herd.RegisterMovementAsync(x, ct); return Created($"/api/livestock/movements/{id}", new { id }); }

    [HttpGet("api/livestock/handling-orders"), Authorize(Policy = Permissions.LivestockRead)]
    public Task<IReadOnlyList<dynamic>> HandlingOrders(string? status, Guid? farmId, CancellationToken ct) => herd.ListHandlingOrdersAsync(status, farmId, ct);
    [HttpGet("api/livestock/handling-orders/{id:guid}"), Authorize(Policy = Permissions.LivestockRead)]
    public async Task<IActionResult> HandlingOrder(Guid id, CancellationToken ct) => await herd.HandlingOrderAsync(id, ct) is { } order ? Ok(order) : NotFound();
    [HttpPost("api/livestock/handling-orders"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> HandlingOrder(HandlingOrderCommand x, CancellationToken ct) { var id = await herd.CreateHandlingOrderAsync(x, ct); return Created($"/api/livestock/handling-orders/{id}", new { id }); }
    [HttpPost("api/livestock/handling-orders/{id:guid}/transition"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> HandlingTransition(Guid id, HandlingOrderTransitionCommand x, CancellationToken ct) { await herd.TransitionHandlingOrderAsync(id, x, ct); return NoContent(); }
    [HttpGet("api/livestock/handling-orders/{id:guid}/preview"), Authorize(Policy = Permissions.LivestockRead)]
    public Task<IReadOnlyList<dynamic>> HandlingPreview(Guid id, CancellationToken ct) => herd.PreviewHandlingAsync(id, ct);
    [HttpPost("api/livestock/handling-orders/{id:guid}/execute"), Authorize(Policy = Permissions.LivestockWrite)]
    public Task<IReadOnlyList<dynamic>> HandlingExecute(Guid id, HandlingExecutionCommand x, CancellationToken ct) => herd.ExecuteHandlingAsync(id, x, ct);

    [HttpGet("api/livestock/weighings"), Authorize(Policy = Permissions.LivestockRead)]
    public Task<IReadOnlyList<dynamic>> Weighings(Guid? animalId, Guid? herdId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        herd.ListWeighingsAsync(animalId, herdId, from, to, ct);
    [HttpPost("api/livestock/weighings"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> Weighing(WeighingCommand x, CancellationToken ct) { var id = await herd.RecordWeighingAsync(x, ct); return Created($"/api/livestock/weighings/{id}", new { id }); }
    [HttpPost("api/livestock/weighings/{id:guid}/correct"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> WeighingCorrection(Guid id, WeighingCorrectionCommand x, CancellationToken ct) { await herd.CorrectWeighingAsync(id, x, ct); return NoContent(); }
    [HttpGet("api/livestock/performance"), Authorize(Policy = Permissions.LivestockRead)]
    public Task<IReadOnlyList<dynamic>> Performance(Guid? animalId, Guid? herdId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        herd.PerformanceAsync(animalId, herdId, from, to, ct);

    [HttpGet("api/livestock/restrictions"), Authorize(Policy = Permissions.LivestockRead)]
    public Task<IReadOnlyList<dynamic>> Restrictions(Guid? animalId, string? purpose, bool onlyActive = true, CancellationToken ct = default) =>
        herd.ListRestrictionsAsync(animalId, purpose, onlyActive, ct);
    [HttpPost("api/livestock/restrictions/{id:guid}/release"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> RestrictionRelease(Guid id, RestrictionReleaseCommand x, CancellationToken ct) { await herd.ReleaseRestrictionAsync(id, x, ct); return NoContent(); }

    [HttpPost("api/livestock/feedings/{id:guid}/return"), Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> FeedingReturn(Guid id, FeedingReturnCommand x, CancellationToken ct) { await herd.ReturnFeedingAsync(id, x, ct); return NoContent(); }

    [HttpGet("api/livestock/commercial/eligible"), Authorize(Policy = Permissions.LivestockRead)]
    public Task<IReadOnlyList<dynamic>> Eligible(Guid farmId, string? search = null, CancellationToken ct = default) =>
        herd.EligibleForSaleAsync(farmId, search, ct);
    [HttpPost("api/livestock/commercial/reservations"), Authorize(Policy = Permissions.LivestockSell)]
    public async Task<IActionResult> Reservation(SaleReservationCommand x, CancellationToken ct) { var id = await herd.ReserveAsync(x, ct); return Created($"/api/livestock/commercial/reservations/{id}", new { id }); }
    [HttpPost("api/livestock/commercial/reservations/{id:guid}/cancel"), Authorize(Policy = Permissions.LivestockSell)]
    public async Task<IActionResult> ReservationCancel(Guid id, InactivateAnimalCommand x, CancellationToken ct) { await herd.CancelReservationAsync(id, x.Reason, ct); return NoContent(); }
    [HttpPost("api/livestock/commercial/reservations/{id:guid}/exit"), Authorize(Policy = Permissions.LivestockSell)]
    public async Task<IActionResult> ReservationExit(Guid id, PhysicalExitCommand x, CancellationToken ct) { await herd.ConfirmPhysicalExitAsync(id, x, ct); return NoContent(); }
    [HttpPost("api/livestock/commercial/reservations/{id:guid}/financial"), Authorize(Policy = Permissions.LivestockSell)]
    public async Task<IActionResult> ReservationFinancial(Guid id, FinancialConfirmCommand x, CancellationToken ct) { await herd.ConfirmFinancialAsync(id, x, ct); return NoContent(); }

    [HttpGet("api/livestock/costs"), Authorize(Policy = Permissions.LivestockRead)]
    public Task<IReadOnlyList<dynamic>> Costs(Guid? farmId, Guid? herdId, Guid? animalId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        herd.CostSummaryAsync(farmId, herdId, animalId, from, to, ct);
    [HttpGet("api/livestock/reports/herd"), Authorize(Policy = Permissions.LivestockRead)]
    public Task<IReadOnlyList<dynamic>> HerdAsOf(DateOnly asOf, Guid? farmId, CancellationToken ct) => herd.HerdReportAsync(asOf, farmId, ct);
    [HttpGet("api/livestock/reports/export"), Authorize(Policy = Permissions.LivestockRead)]
    public async Task<IActionResult> ExportReport(string kind, DateOnly? from, DateOnly? to, Guid? farmId, string? status, CancellationToken ct)
    {
        var bytes = await herd.ExportCsvAsync(new LivestockExportFilter(kind, from, to, farmId, status), ct);
        return File(bytes, "text/csv; charset=utf-8", $"pecuaria-{kind}.csv");
    }
}
