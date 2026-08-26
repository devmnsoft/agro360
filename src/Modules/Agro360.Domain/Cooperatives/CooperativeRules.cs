namespace Agro360.Domain.Cooperatives;
public static class CooperativeRules
{
 public static void ValidateAllocation(decimal total,IEnumerable<decimal> allocations){if(total<=0||allocations.Any(x=>x<=0)||allocations.Sum()!=total)throw new ArgumentException("O rateio deve ser positivo e fechar o volume total.");}
 public static void ValidateTransition(string status){if(status is not ("ACTIVE" or "APPROVED" or "CANCELLED" or "CLOSED"))throw new ArgumentException("Transição inválida.");}
 public static decimal QualityBonus(decimal baseAmount,decimal percentage)=>decimal.Round(baseAmount*percentage/100,2);
}
