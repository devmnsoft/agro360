using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Agro360.Web.Pages.Platform;

[Authorize(Roles = "SUPER_ADMIN,PLATFORM_SUPER_ADMIN")]
public sealed class PlatformModel : PageModel
{
    public void OnGet()
    {
    }
}
