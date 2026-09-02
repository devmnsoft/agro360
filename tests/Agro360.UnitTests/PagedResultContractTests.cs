using System.Globalization;
using System.Reflection;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Services;

namespace Agro360.UnitTests;

public sealed class PagedResultContractTests
{
    public static TheoryData<Type, Type, string> PagedMethods => new()
    {
        { typeof(IAgriculture360Service), typeof(Agriculture360Service), nameof(IAgriculture360Service.ListAsync) },
        { typeof(IAgricultureService), typeof(AgricultureService), nameof(IAgricultureService.ListSeasonsAsync) },
        { typeof(IInventoryService), typeof(InventoryService), nameof(IInventoryService.ListBalancesAsync) },
        { typeof(ILivestockService), typeof(LivestockService), nameof(ILivestockService.ListAnimalsAsync) },
        { typeof(ILookupService), typeof(LookupService), nameof(ILookupService.SearchAsync) },
        { typeof(IOperationsService), typeof(OperationsService), nameof(IOperationsService.ListSuppliersAsync) },
        { typeof(IPropertyService), typeof(PropertyService), nameof(IPropertyService.ListFarmsAsync) },
        { typeof(IPropertyService), typeof(PropertyService), nameof(IPropertyService.ListFieldsAsync) },
        { typeof(IWorkManagementService), typeof(WorkManagementService), nameof(IWorkManagementService.TasksAsync) },
    };

    [Theory]
    [MemberData(nameof(PagedMethods))]
    public void Implementation_uses_the_exact_application_contract(Type contract, Type implementation, string methodName)
    {
        MethodInfo contractMethod = contract.GetMethod(methodName)!;
        Type[] parameters = contractMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        MethodInfo implementationMethod = implementation.GetMethod(methodName, parameters)!;

        Assert.Equal(contractMethod.ReturnType, implementationMethod.ReturnType);
        Assert.Equal(typeof(PagedResult<>), contractMethod.ReturnType.GetGenericArguments()[0].GetGenericTypeDefinition());
    }

    [Fact]
    public void Fiscal_csv_uses_invariant_formatting()
    {
        MethodInfo csv = typeof(FiscalService).GetMethod("Csv", BindingFlags.NonPublic | BindingFlags.Static)!;
        CultureInfo previousCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            Assert.Equal("\"1234.56\"", csv.Invoke(null, [1234.56m]));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
