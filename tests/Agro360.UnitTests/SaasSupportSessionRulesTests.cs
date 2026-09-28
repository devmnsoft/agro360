using Agro360.Application;
using Xunit;

namespace Agro360.UnitTests;

public sealed class SaasSupportSessionRulesTests
{
    [Theory]
    [InlineData("commercial.read", true)]
    [InlineData("reports.read", true)]
    [InlineData("saas.download", true)]
    [InlineData("support_session", true)]
    [InlineData("commercial.write", false)]
    [InlineData("commercial.approve_order", false)]
    [InlineData("platform.admin", false)]
    [InlineData("identity.manage", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsReadOnlyPermissionIdentifiesReadVersusMutationCorrectly(string? permission, bool expectedReadOnly)
    {
        Assert.Equal(expectedReadOnly, Permissions.IsReadOnlyPermission(permission!));
    }
}
