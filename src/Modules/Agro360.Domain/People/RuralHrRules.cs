namespace Agro360.Domain.People;

public static class RuralHrRules
{
 public static decimal WorkedHours(DateTimeOffset start,DateTimeOffset end,int breakMinutes)
 { if(end<=start)throw new ArgumentException("A saída deve ser posterior à entrada.");if(breakMinutes<0)throw new ArgumentException("A pausa não pode ser negativa.");var hours=(decimal)(end-start).TotalHours-breakMinutes/60m;if(hours<=0||hours>24)throw new ArgumentException("Jornada inválida.");return decimal.Round(hours,2); }
 public static decimal LaborCost(decimal hours,decimal rate,string type,decimal quantity=1)=>type switch {"HOURLY"=>hours*rate,"DAILY"=>rate,"PIECEWORK"=>quantity*rate,"FIXED"=>rate,_=>throw new ArgumentException("Modalidade de custo inválida.")};
 public static void EnsureCapacity(int capacity,int passengers){if(capacity<=0||passengers<0||passengers>capacity)throw new ArgumentException("A lotação excede a capacidade do transporte.");}
 public static bool IsExpired(DateOnly expiresOn,DateOnly today)=>expiresOn<today;
}
