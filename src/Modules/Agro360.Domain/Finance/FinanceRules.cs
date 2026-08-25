using Agro360.SharedKernel;

namespace Agro360.Domain.Finance;

public static class FinanceRules
{
    public static readonly string[] AccountTypes = ["REVENUE", "EXPENSE", "COST", "INVESTMENT", "ASSET", "LIABILITY"];
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
}
