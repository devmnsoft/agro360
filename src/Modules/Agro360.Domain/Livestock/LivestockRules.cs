using Agro360.SharedKernel;

namespace Agro360.Domain.Livestock;

public static class LivestockRules
{
    public static decimal NonNegative(decimal value,string field){if(value<0)throw new DomainException($"{field} não pode ser negativo.","livestock.negative_value");return value;}
    public static decimal Positive(decimal value,string field){if(value<=0)throw new DomainException($"{field} deve ser positivo.","livestock.positive_required");return value;}
    public static decimal AverageDailyGain(decimal initialWeight,DateOnly initialDate,decimal finalWeight,DateOnly finalDate){NonNegative(initialWeight,"Peso inicial");NonNegative(finalWeight,"Peso final");var days=finalDate.DayNumber-initialDate.DayNumber;if(days<=0)throw new DomainException("A pesagem final deve ser posterior à inicial.","livestock.weight_period_invalid");return decimal.Round((finalWeight-initialWeight)/days,4);}
    public static DateOnly ExpectedBirth(DateOnly eventDate,string species)=>eventDate.AddDays(species.Trim().ToUpperInvariant() switch{"BOVINE" or "BOVINO"=>283,"BUFFALO" or "BUBALINO"=>310,"SHEEP" or "OVINO"=>150,"GOAT" or "CAPRINO"=>150,"SWINE" or "SUINO" or "SUÍNO"=>114,_=>280});
    public static void RequireFemale(string sex){if(!new[]{"F","FEMALE","FEMEA","FÊMEA"}.Contains(sex.Trim().ToUpperInvariant()))throw new DomainException("Somente fêmeas aptas podem iniciar gestação.","livestock.female_required");}
    public static decimal DietCost(IEnumerable<(decimal Quantity,decimal UnitCost)> items){var list=items.ToArray();if(list.Length==0)throw new DomainException("A dieta deve conter itens.","livestock.diet_empty");foreach(var i in list){Positive(i.Quantity,"Quantidade");NonNegative(i.UnitCost,"Custo");}return list.Sum(i=>i.Quantity*i.UnitCost);}
}
