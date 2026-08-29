using Agro360.SharedKernel;

namespace Agro360.Domain.Finance;

public static class FinanceRules
{
    public static readonly string[] AccountTypes = ["REVENUE", "EXPENSE", "COST", "DIRECT_COST", "INDIRECT_COST", "INVESTMENT", "TAX", "TRANSFER", "ADJUSTMENT", "OTHER", "ASSET", "LIABILITY"];
    public static readonly string[] Natures = ["DEBIT", "CREDIT"];
    public static void Account(string code, string name, string type, string nature)
    {
        Guard.Required(code, nameof(code), 40); Guard.Required(name, nameof(name), 160);
        if (!AccountTypes.Contains(type.ToUpperInvariant())) throw new DomainException("Tipo de conta inválido.", "finance.account_type_invalid");
        if (!Natures.Contains(nature.ToUpperInvariant())) throw new DomainException("Natureza inválida.", "finance.nature_invalid");
    }
    public static decimal FinalAmount(decimal original, decimal discount, decimal interest, decimal fine)
    {
        if (original <= 0 || discount < 0 || interest < 0 || fine < 0) throw new DomainException("Valores financeiros devem ser positivos.", "finance.amount_invalid");
        var result = original - discount + interest + fine;
        if (result < 0) throw new DomainException("Desconto não pode superar o título.", "finance.discount_invalid");
        return result;
    }

    public static void Allocation(IEnumerable<decimal> percentages)
    {
        var values = percentages.ToArray();
        if (values.Length == 0 || values.Any(x => x <= 0) || Math.Abs(values.Sum() - 100m) > 0.0001m)
            throw new DomainException("O rateio deve totalizar 100%.", "finance.allocation_invalid");
    }

    public static decimal SettlementBalance(decimal balance, decimal amount, bool allowOverpayment = false)
    {
        if (amount <= 0) throw new DomainException("Valor da baixa deve ser positivo.", "finance.settlement_invalid");
        if (amount > balance && !allowOverpayment) throw new DomainException("Baixa maior que o saldo.", "finance.overpayment");
        return Math.Max(0, balance - amount);
    }

    public static decimal MarginPercent(decimal revenue, decimal directCost, decimal indirectCost) =>
        revenue == 0 ? 0 : Math.Round((revenue - directCost - indirectCost) / revenue * 100m, 4);

    public static void Budget(decimal amount, DateOnly from, DateOnly to)
    {
        if (amount <= 0) throw new DomainException("Valor previsto deve ser positivo.", "finance.budget_amount_invalid");
        if (to < from) throw new DomainException("Período do orçamento inválido.", "finance.budget_period_invalid");
    }
}
