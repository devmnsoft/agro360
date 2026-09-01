using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Agro360.Web.Pages.Crm;
public sealed class IndexModel:PageModel { public string ViewName {get;private set;}="dashboard"; public void OnGet(string? view)=>ViewName=string.IsNullOrWhiteSpace(view)?"dashboard":view; }
