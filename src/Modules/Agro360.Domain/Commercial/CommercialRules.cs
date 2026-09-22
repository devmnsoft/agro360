using Agro360.SharedKernel;

namespace Agro360.Domain.Commercial;

public static class CommercialRules
{
    private static readonly Dictionary<string, string[]> ContractTransitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DRAFT"] = ["UNDER_REVIEW", "CANCELLED"],
        ["UNDER_REVIEW"] = ["DRAFT", "APPROVED", "CANCELLED"],
        ["APPROVED"] = ["ACTIVE", "SUSPENDED", "CANCELLED"],
        ["ACTIVE"] = ["SUSPENDED", "PARTIALLY_FULFILLED", "FULFILLED", "CLOSED", "CANCELLED"],
        ["SUSPENDED"] = ["ACTIVE", "CANCELLED", "CLOSED"],
        ["PARTIALLY_FULFILLED"] = ["FULFILLED", "CANCELLED", "CLOSED"],
        ["FULFILLED"] = ["CLOSED"]
    };
    private static readonly Dictionary<string, string[]> OrderTransitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DRAFT"] = ["UNDER_REVIEW", "CANCELLED"],
        ["UNDER_REVIEW"] = ["DRAFT", "APPROVED", "CANCELLED"],
        ["APPROVED"] = ["RESERVED", "FULFILLMENT", "CANCELLED"],
        ["RESERVED"] = ["FULFILLMENT", "CANCELLED"],
        ["FULFILLMENT"] = ["INVOICED", "DELIVERED", "CANCELLED"],
        ["INVOICED"] = ["DELIVERED", "RETURNED"],
        ["DELIVERED"] = ["INVOICED", "RETURNED"]
    };

    public static string NormalizeOrderStatus(string? status)
    {
        var normalized = status?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized) || !OrderTransitions.Values.SelectMany(x => x).Append("DRAFT").Contains(normalized))
            throw new DomainException("Status do pedido inválido.", "sales.order_status_invalid");
        return normalized;
    }

    public static string NormalizeContractType(string? type)
    {
        var value = type?.Trim().ToUpperInvariant();
        if (value is not ("INTERNAL_SALE" or "COOPERATIVE" or "EXPORT" or "RECURRING_SUPPLY" or "TRADING"))
            throw new DomainException("Tipo de contrato inválido.", "sales.contract_type_invalid");
        return value;
    }

    public static void ValidateContract(string type, decimal quantity, decimal unitPrice, DateOnly validFrom, DateOnly validTo, string? terms, string? currency, string? incoterm)
    {
        var normalized = NormalizeContractType(type);
        if (quantity <= 0 || unitPrice < 0) throw new DomainException("Quantidade e preço do contrato são inválidos.", "sales.contract_values_invalid");
        if (validTo < validFrom) throw new DomainException("A vigência final deve ser igual ou posterior à inicial.", "sales.contract_period_invalid");
        if (string.IsNullOrWhiteSpace(terms)) throw new DomainException("Informe as condições comerciais.", "sales.contract_terms_required");
        if (normalized == "EXPORT" && (string.IsNullOrWhiteSpace(currency) || string.IsNullOrWhiteSpace(incoterm)))
            throw new DomainException("Contrato de exportação exige moeda e Incoterm.", "sales.contract_export_terms_required");
    }

    public static void ValidateContractTransition(string current, string next, string? reason)
    {
        if (!ContractTransitions.TryGetValue(current, out var allowed) || !allowed.Contains(next))
            throw new DomainException($"Transição de contrato de {current} para {next} não é permitida.", "sales.contract_transition_invalid");
        if (next == "CANCELLED" && string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Cancelamento do contrato exige motivo.", "sales.contract_cancel_reason_required");
    }

    public static void ValidateOrderTransition(string current, string next, string? reason)
    {
        if (!OrderTransitions.TryGetValue(current, out var allowed) || !allowed.Contains(next))
            throw new DomainException($"Transição de {current} para {next} não é permitida.", "sales.order_transition_invalid");
        if (next == "CANCELLED" && string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Cancelamento exige motivo.", "sales.order_cancel_reason_required");
    }

    public static void ValidateTaxDocument(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        var valid = digits.Length switch { 11 => IsValidCpf(digits), 14 => IsValidCnpj(digits), _ => false };
        if (!valid) throw new DomainException("CPF/CNPJ inválido.", "sales.invalid_tax_document");
    }

    public static void CustomerCanOrder(string status, bool mayOverrideDelinquency)
    {
        var normalized = status.Trim().ToUpperInvariant();
        if (normalized == "INACTIVE") throw new DomainException("Reative o cliente antes de criar um pedido.", "sales.customer_inactive");
        if (normalized == "BLOCKED")
            throw new DomainException("Cliente bloqueado não pode gerar pedido.", "sales.customer_blocked");
        if (normalized == "DELINQUENT" && !mayOverrideDelinquency)
            throw new DomainException("Cliente inadimplente exige autorização superior.", "sales.customer_delinquent_authorization_required");
    }

    public static void ValidateOpportunity(string stage, decimal value, string? lossReason)
    {
        if (value <= 0) throw new DomainException("O valor estimado deve ser positivo.");
        if (string.Equals(stage, "LOST", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(lossReason)) throw new DomainException("Informe o motivo da perda.");
    }

    public static CommercialOrderLineCalculation[] CalculateOrder(IEnumerable<(decimal Quantity, decimal UnitPrice, decimal Discount, decimal BasePrice, decimal MaximumDiscount)> items)
    {
        var rows = items.ToArray();
        if (rows.Length == 0) throw new DomainException("O pedido precisa ter ao menos um item.", "sales.order_without_items");
        var calculated = new List<CommercialOrderLineCalculation>(rows.Length);
        foreach (var item in rows)
        {
            if (item.Quantity <= 0 || item.UnitPrice <= 0 || item.BasePrice <= 0) throw new DomainException("Quantidade e preços devem ser positivos.");
            if (item.Discount is < 0 or > 100 || item.MaximumDiscount is < 0 or > 100) throw new DomainException("Desconto acima do limite comercial.", "sales.discount_exceeded");

            var netUnitPrice = item.UnitPrice * (1 - item.Discount / 100);
            var effectiveDiscount = Math.Max(0, (1 - netUnitPrice / item.BasePrice) * 100);
            if (effectiveDiscount > item.MaximumDiscount)
                throw new DomainException("Preço negociado e desconto excedem o limite comercial.", "sales.discount_exceeded");

            var lineTotal = decimal.Round(item.Quantity * netUnitPrice, 2, MidpointRounding.AwayFromZero);
            calculated.Add(new CommercialOrderLineCalculation(item.Quantity, item.UnitPrice, item.Discount, item.BasePrice, item.MaximumDiscount, lineTotal, effectiveDiscount));
        }
        return calculated.ToArray();
    }

    public static decimal OrderTotal(IEnumerable<CommercialOrderLineCalculation> lines, decimal freight)
    {
        if (freight < 0) throw new DomainException("Frete não pode ser negativo.", "sales.freight_invalid");
        return decimal.Round(lines.Sum(x => x.LineTotal) + freight, 2, MidpointRounding.AwayFromZero);
    }

    public static CommercialPriceCalculation CalculatePrice(
        decimal quantity,
        decimal baseUnitPrice,
        decimal percentageDiscount,
        decimal absoluteDiscount,
        decimal additions,
        decimal freight,
        decimal informativeTaxes,
        decimal standardDiscountLimit,
        string? specialDiscountReason)
    {
        if (quantity <= 0 || baseUnitPrice <= 0)
            throw new DomainException("Quantidade e preço base devem ser positivos.", "sales.price_values_invalid");
        if (percentageDiscount is < 0 or > 100 || standardDiscountLimit is < 0 or > 100
            || absoluteDiscount < 0 || additions < 0 || freight < 0 || informativeTaxes < 0)
            throw new DomainException("Os componentes do preço são inválidos.", "sales.price_components_invalid");

        var gross = decimal.Round(quantity * baseUnitPrice, 2, MidpointRounding.AwayFromZero);
        var percentageValue = decimal.Round(gross * percentageDiscount / 100, 2, MidpointRounding.AwayFromZero);
        var net = decimal.Round(gross - percentageValue - absoluteDiscount + additions + freight + informativeTaxes, 2, MidpointRounding.AwayFromZero);
        if (net < 0) throw new DomainException("O total líquido não pode ser negativo.", "sales.negative_net_total");

        var effectiveDiscount = gross == 0 ? 0 : decimal.Round((percentageValue + absoluteDiscount) / gross * 100, 4);
        var requiresApproval = effectiveDiscount > standardDiscountLimit;
        if (requiresApproval && string.IsNullOrWhiteSpace(specialDiscountReason))
            throw new DomainException("Desconto especial exige justificativa.", "sales.special_discount_reason_required");

        return new(gross, percentageValue, absoluteDiscount, additions, freight, informativeTaxes, net, effectiveDiscount, requiresApproval);
    }

    public static void ValidateContractBalance(decimal contractedQuantity, decimal fulfilledQuantity, decimal requestedQuantity)
    {
        if (contractedQuantity <= 0 || fulfilledQuantity < 0 || requestedQuantity <= 0 || fulfilledQuantity + requestedQuantity > contractedQuantity)
            throw new DomainException("Saldo contratual insuficiente para o pedido.", "sales.contract_balance_insufficient");
    }

    public static void EnsureContractAcceptsOrders(string status)
    {
        if (status.Trim().ToUpperInvariant() is not ("ACTIVE" or "PARTIALLY_FULFILLED"))
            throw new DomainException("O contrato não está disponível para novos pedidos.", "sales.contract_not_orderable");
    }

    public static bool IsCommissionEligible(string orderStatus)
        => orderStatus.Trim().ToUpperInvariant() is "APPROVED" or "INVOICED";

    public static decimal CommissionForOrder(string orderStatus, decimal netTotal, decimal percentage)
    {
        if (!IsCommissionEligible(orderStatus))
            throw new DomainException("O status do pedido não é elegível para comissão.", "sales.commission_status_ineligible");
        return Commission(netTotal, percentage, null);
    }

    public static CommercialComplianceRequirements ComplianceFor(string productName, bool regionalProduct = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        var normalized = productName.Trim().ToUpperInvariant();
        var reinforced = regionalProduct || normalized.Contains("AÇAÍ", StringComparison.Ordinal)
            || normalized.Contains("ACAI", StringComparison.Ordinal)
            || normalized.Contains("CACAU", StringComparison.Ordinal)
            || normalized.Contains("TUCUPI", StringComparison.Ordinal);
        return reinforced
            ? new(true, true, true, true, false, true)
            : new(false, false, false, false, false, false);
    }

    public static decimal Commission(decimal basis, decimal? percentage, decimal? fixedValue)
    {
        if (basis < 0 || (percentage is null) == (fixedValue is null) || percentage is < 0 or > 100 || fixedValue < 0)
            throw new DomainException("Regra de comissão inválida.");
        return decimal.Round(fixedValue ?? basis * percentage!.Value / 100, 2);
    }

    public static void ValidateSplit(IEnumerable<(Guid ParticipantId, decimal? Percentage, decimal? FixedValue)> participants)
    {
        var rows = participants.ToArray();
        if (rows.Length == 0 || rows.GroupBy(x => x.ParticipantId).Any(x => x.Count() > 1)) throw new DomainException("Participantes do split devem ser únicos.");
        if (rows.Any(x => (x.Percentage is null) == (x.FixedValue is null) || x.Percentage < 0 || x.FixedValue < 0)) throw new DomainException("Use percentual ou valor fixo por participante, nunca ambos.");
        if (rows.Sum(x => x.Percentage ?? 0) > 100) throw new DomainException("A soma percentual do split não pode ultrapassar 100%.", "sales.split_over_100");
    }

    private static bool IsValidCpf(string value)
    {
        if (value.Distinct().Count() == 1) return false;
        var first = Mod11(value, 9, 10);
        var second = Mod11(value, 10, 11);
        return value[9] - '0' == first && value[10] - '0' == second;
    }

    private static bool IsValidCnpj(string value)
    {
        if (value.Distinct().Count() == 1) return false;
        int Digit(int length, int[] weights)
        {
            var sum = 0;
            for (var i = 0; i < length; i++) sum += (value[i] - '0') * weights[i];
            var remainder = sum % 11;
            return remainder < 2 ? 0 : 11 - remainder;
        }
        return value[12] - '0' == Digit(12, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])
            && value[13] - '0' == Digit(13, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
    }

    private static int Mod11(string value, int length, int initialWeight)
    {
        var sum = 0;
        for (var i = 0; i < length; i++) sum += (value[i] - '0') * (initialWeight - i);
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}

public sealed record CommercialOrderLineCalculation(decimal Quantity, decimal UnitPrice, decimal Discount, decimal BasePrice, decimal MaximumDiscount, decimal LineTotal, decimal EffectiveDiscount);
public sealed record CommercialPriceCalculation(decimal GrossTotal, decimal PercentageDiscountValue, decimal AbsoluteDiscount, decimal Additions, decimal Freight, decimal InformativeTaxes, decimal NetTotal, decimal EffectiveDiscountPercentage, bool RequiresApproval);
public sealed record CommercialComplianceRequirements(bool RequiresEnvironmentalDocuments, bool RequiresTrackedOrigin, bool RequiresPhotoOrGpsEvidence, bool RequiresQualityReport, bool RequiresExportCompliance, bool ReinforcedTraceability);
