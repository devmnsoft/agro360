using Agro360.SharedKernel;

namespace Agro360.Domain.Agriculture;

public static class GenealogyRules
{
    public static void ValidateLink(string kind, string originType, Guid originId, string destinationType, Guid destinationId, decimal? quantity, string? unit, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new DomainException("O tipo de vínculo de genealogia é obrigatório.", "genealogy.kind_required");

        if (string.IsNullOrWhiteSpace(originType))
            throw new DomainException("O tipo de origem é obrigatório.", "genealogy.origin_type_required");

        if (originId == Guid.Empty)
            throw new DomainException("O identificador de origem é obrigatório.", "genealogy.origin_id_required");

        if (string.IsNullOrWhiteSpace(destinationType))
            throw new DomainException("O tipo de destino é obrigatório.", "genealogy.destination_type_required");

        if (destinationId == Guid.Empty)
            throw new DomainException("O identificador de destino é obrigatório.", "genealogy.destination_id_required");

        if (originId == destinationId && string.Equals(originType, destinationType, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("A origem e o destino do vínculo não podem ser idênticos.", "genealogy.self_reference");

        if (quantity.HasValue && quantity.Value <= 0)
            throw new DomainException("A quantidade vinculada deve ser estritamente positiva.", "genealogy.quantity_invalid");

        if (quantity.HasValue && string.IsNullOrWhiteSpace(unit))
            throw new DomainException("A unidade de medida é obrigatória quando a quantidade é informada.", "genealogy.unit_required");

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException("A chave de idempotência é obrigatória.", "genealogy.idempotency_key_required");
    }

    public static bool CanConsolidateStock(IEnumerable<string?> units, out string? unifiedUnit, out string? reason)
    {
        var distinct = units
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u!.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();

        if (distinct.Length == 0)
        {
            unifiedUnit = null;
            reason = "Não há lotes com unidade de medida válida para cálculo de estoque.";
            return false;
        }

        if (distinct.Length > 1)
        {
            unifiedUnit = null;
            reason = "Unidades físicas heterogêneas nos lotes de estoque relacionados; consolidação recusada por integridade.";
            return false;
        }

        unifiedUnit = distinct[0];
        reason = null;
        return true;
    }

    public static decimal CalculateNetDeliveredQuantity(decimal acceptedQuantity, decimal returnedQuantity)
    {
        if (acceptedQuantity < 0)
            throw new DomainException("A quantidade aceita não pode ser negativa.", "genealogy.accepted_quantity_invalid");

        if (returnedQuantity < 0)
            throw new DomainException("A quantidade retornada não pode ser negativa.", "genealogy.returned_quantity_invalid");

        return Math.Max(0m, acceptedQuantity - returnedQuantity);
    }
}
