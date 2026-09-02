using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController]
[Route("api/v1/livestock")]
[Authorize]
public sealed class LivestockController(ILivestockService livestock) : ControllerBase
{
    [HttpPost("animals")]
    [Authorize(Policy = Permissions.LivestockWrite)]
    public async Task<IActionResult> RegisterAnimal(RegisterAnimalCommand command, CancellationToken cancellationToken)
    {
        var result = await livestock.RegisterAnimalAsync(command, cancellationToken).ConfigureAwait(false);
        return Created($"/api/v1/livestock/animals/{result.Id}", result);
    }

    [HttpGet("animals")]
    [Authorize(Policy = Permissions.LivestockRead)]
    public Task<PagedResult<AnimalDto>> ListAnimals(
        [FromQuery] Guid? farmId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        livestock.ListAnimalsAsync(farmId, page, pageSize, search, cancellationToken);

    [HttpPost("animals/{animalId:guid}/weights")]
    [Authorize(Policy = Permissions.LivestockWrite)]
    public Task<AnimalEventResult> Weigh(
        Guid animalId,
        WeighAnimalRequest request,
        CancellationToken cancellationToken) =>
        livestock.WeighAsync(
            new WeighAnimalCommand(animalId, request.WeightKg, request.MeasuredOn, request.Notes, request.IdempotencyKey),
            cancellationToken);

    [HttpPost("animals/{animalId:guid}/treatments")]
    [Authorize(Policy = Permissions.LivestockWrite)]
    public Task<AnimalEventResult> Treat(
        Guid animalId,
        TreatAnimalRequest request,
        CancellationToken cancellationToken) =>
        livestock.TreatAsync(
            new TreatAnimalCommand(
                animalId,
                request.WarehouseId,
                request.ProductId,
                request.Quantity,
                request.Unit,
                request.AppliedOn,
                request.WithdrawalDays,
                request.TreatmentType,
                request.LotNumber,
                request.Notes,
                request.IdempotencyKey),
            cancellationToken);
}

public sealed record WeighAnimalRequest(
    decimal WeightKg,
    DateOnly MeasuredOn,
    string? Notes,
    string? IdempotencyKey);

public sealed record TreatAnimalRequest(
    Guid WarehouseId,
    Guid ProductId,
    decimal Quantity,
    string Unit,
    DateOnly AppliedOn,
    int WithdrawalDays,
    string TreatmentType,
    string? LotNumber,
    string? Notes,
    string? IdempotencyKey);
