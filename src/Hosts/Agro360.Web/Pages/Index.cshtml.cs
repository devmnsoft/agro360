using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Agro360.Web.Pages;

[AllowAnonymous]
public sealed class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
