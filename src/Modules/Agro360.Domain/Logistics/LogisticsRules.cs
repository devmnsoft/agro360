using Agro360.SharedKernel;

namespace Agro360.Domain.Logistics;

public static class LogisticsRules
{
    public static readonly string[] TripStatuses =
        ["PLANNED", "AWAITING_PICKUP", "IN_TRANSIT", "DELIVERED", "CANCELLED", "WITH_OCCURRENCE"];

    public static readonly string[] RouteTypes = ["ROAD", "RIVER", "MIXED"];

    public static void ValidateTrip(string? origin, string? destination, string? routeType,
        decimal estimatedDistance, decimal freightValue, decimal tonnes, string? status)
    {
        Guard.Required(origin, nameof(origin), 240);
        Guard.Required(destination, nameof(destination), 240);

        if (string.Equals(origin!.Trim(), destination!.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Origem e destino da viagem devem ser diferentes.", "logistics.route_same_endpoint");
        if (!RouteTypes.Contains(routeType?.Trim().ToUpperInvariant()))
            throw new DomainException("Tipo de rota inválido. Use terrestre, fluvial ou mista.", "logistics.route_type_invalid");
        if (!TripStatuses.Contains(status?.Trim().ToUpperInvariant()))
            throw new DomainException("Status da viagem inválido.", "logistics.trip_status_invalid");
        if (estimatedDistance < 0 || freightValue < 0 || tonnes <= 0)
            throw new DomainException("Distância e frete não podem ser negativos e a carga deve ser positiva.", "logistics.trip_values_invalid");
    }

    public static void EnsureCanDeliver(string? currentStatus)
    {
        if (!string.Equals(currentStatus?.Trim(), "IN_TRANSIT", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("A entrega só pode ser confirmada após o início da expedição.", "logistics.delivery_requires_dispatch");
    }

    public static void ValidateCapacity(decimal? capacity, decimal? used, string? unit)
    {
        if (capacity is null)
            throw new DomainException("Capacidade do recurso ainda não foi informada.", "logistics.capacity_pending");
        if (string.IsNullOrWhiteSpace(unit))
            throw new DomainException("Unidade da capacidade é obrigatória; conversões não são presumidas.", "logistics.capacity_unit_required");
        if (capacity <= 0 || used is null || used < 0)
            throw new DomainException("Capacidade e ocupação são inválidas.", "logistics.capacity_invalid");
        if (used > capacity)
            throw new DomainException("A ocupação supera a capacidade configurada.", "logistics.capacity_exceeded");
    }

    public static void ValidateReconciliation(decimal dispatched, decimal accepted, decimal returned,
        decimal lost, decimal refusedPending)
    {
        if (dispatched < 0 || accepted < 0 || returned < 0 || lost < 0 || refusedPending < 0 ||
            accepted + returned + lost + refusedPending != dispatched)
            throw new DomainException("Toda quantidade expedida deve possuir um único destino conciliado.", "logistics.reconciliation_incomplete");
    }
}
