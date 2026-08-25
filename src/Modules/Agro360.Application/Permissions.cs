namespace Agro360.Application;

public static class Permissions
{
    public const string PropertiesRead = "properties.read";
    public const string PropertiesWrite = "properties.write";
    public const string AgricultureRead = "agriculture.read";
    public const string AgricultureWrite = "agriculture.write";
    public const string InventoryRead = "inventory.read";
    public const string InventoryMove = "inventory.move";
    public const string InventoryAdjust = "inventory.adjust";
    public const string LivestockRead = "livestock.read";
    public const string LivestockWrite = "livestock.write";
    public const string LivestockSell = "livestock.sell";
    public const string CommercialWrite = "commercial.write";
    public const string FinanceRead = "finance.read";
    public const string DashboardRead = "dashboard.read";

    public static IReadOnlyCollection<string> Administrator =>
    [
        PropertiesRead,
        PropertiesWrite,
        AgricultureRead,
        AgricultureWrite,
        InventoryRead,
        InventoryMove,
        InventoryAdjust,
        LivestockRead,
        LivestockWrite,
        LivestockSell,
        CommercialWrite,
        FinanceRead,
        DashboardRead
    ];
}
