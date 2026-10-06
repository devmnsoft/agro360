using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Agro360.Web.Pages.Saas;

[AllowAnonymous]
public sealed class AcceptModel : PageModel
{
    public void OnGet() { }
}
