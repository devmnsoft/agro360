using Agro360.SharedKernel;

namespace Agro360.Domain.Storage;

public static class StorageRules
{
    public static decimal NetWeight(decimal gross, decimal tare)
    {
        if (gross < 0 || tare < 0 || tare > gross) throw new DomainException("Pesos inválidos: a tara não pode superar o peso bruto.", "storage.invalid_weight");
        return gross - tare;
    }

    public static decimal FinalWeight(decimal net, decimal discountPercent)
    {
        if (net < 0 || discountPercent is < 0 or > 100) throw new DomainException("Peso ou desconto técnico inválido.", "storage.invalid_discount");
        return decimal.Round(net * (1 - discountPercent / 100), 3);
    }

    public static void Capacity(decimal total, decimal occupied, bool allowOverflow = false)
    {
        if (total < 0 || occupied < 0) throw new DomainException("Capacidade não pode ser negativa.", "storage.negative_capacity");
        if (!allowOverflow && occupied > total) throw new DomainException("A ocupação supera a capacidade da estrutura.", "storage.capacity_exceeded");
    }

    public static void LotWithdrawal(decimal balance, decimal quantity, bool blocked)
    {
        if (blocked) throw new DomainException("Lote bloqueado não pode ser movimentado.", "storage.lot_blocked");
        if (quantity <= 0 || quantity > balance) throw new DomainException("Quantidade de saída supera o saldo do lote.", "storage.insufficient_balance");
    }

    public static decimal Freight(decimal value, decimal distance, decimal tonnes)
    {
        if (value < 0 || distance < 0 || tonnes < 0) throw new DomainException("Valores logísticos não podem ser negativos.", "logistics.negative_value");
        return tonnes == 0 ? 0 : decimal.Round(value / tonnes, 2);
    }

    public static void ValidateReturnDecision(
        string decision,
        decimal quantity,
        string reason,
        string idempotencyKey,
        string? unit = null,
        decimal? cost = null)
    {
        var d = decision?.Trim().ToUpperInvariant();
        if (d is not ("RELEASE" or "BLOCK" or "DISPOSE"))
            throw new DomainException("Decisão de retorno inválida. Use RELEASE, BLOCK ou DISPOSE.", "return.invalid_decision");
        if (quantity <= 0)
            throw new DomainException("A quantidade a destinar deve ser positiva.", "return.quantity_invalid");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("O motivo da destinação é obrigatório.", "return.reason_required");
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException("A chave de confirmação de idempotência é obrigatória.", "return.idempotency_required");
        if (cost.HasValue && cost.Value < 0)
            throw new DomainException("O custo da perda não pode ser negativo.", "return.cost_invalid");
        if (d == "DISPOSE" && string.IsNullOrWhiteSpace(unit))
            throw new DomainException("A unidade da perda é obrigatória.", "return.unit_required");
    }

    public static void ValidateReturnQualityForRelease(
        IReadOnlyList<(string? IntentStatus, string? RunStatus, string? RunResult)> qualityRecords)
    {
        if (qualityRecords == null || qualityRecords.Count == 0)
            throw new DomainException("Não é possível liberar o retorno: nenhum recebimento físico com inspeção associada.", "return.quality_missing");

        if (qualityRecords.Any(q => q.IntentStatus == "PENDING_MODEL"))
            throw new DomainException("Não é possível liberar o retorno: modelo de inspeção de qualidade pendente.", "return.quality_pending_model");

        if (qualityRecords.Any(q => q.IntentStatus == "AMBIGUOUS"))
            throw new DomainException("Não é possível liberar o retorno: modelos de qualidade ambíguos para o item.", "return.quality_ambiguous");

        if (qualityRecords.Any(q => string.Equals(q.RunResult, "NON_CONFORMING", StringComparison.OrdinalIgnoreCase)))
            throw new DomainException("Não é possível liberar o retorno: inspeção de qualidade reprovada (não conforme).", "return.quality_non_conforming");

        if (qualityRecords.Any(q => string.Equals(q.RunResult, "INCONCLUSIVE", StringComparison.OrdinalIgnoreCase)))
            throw new DomainException("Não é possível liberar o retorno: inspeção de qualidade inconclusiva.", "return.quality_inconclusive");

        if (qualityRecords.Any(q => q.RunStatus != "COMPLETED" || !string.Equals(q.RunResult, "CONFORMING", StringComparison.OrdinalIgnoreCase)))
            throw new DomainException("Não é possível liberar o retorno: inspeção de qualidade ainda não concluída com conformidade.", "return.quality_incomplete");
    }
}
