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
}
