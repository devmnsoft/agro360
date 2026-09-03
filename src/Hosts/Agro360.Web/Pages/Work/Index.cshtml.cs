using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Agro360.Web.Pages.Work;
public sealed class IndexModel(IConfiguration configuration):PageModel { public string ApiBaseUrl {get;}=configuration["ApiBaseUrl"]??"https://localhost:7081"; public void OnGet(){} }
