using System.Text.RegularExpressions;
namespace Agro360.Domain.Ecosystem;
public static partial class EcosystemRules
{
 public static readonly string[] ApiScopes=["properties.read","lots.read","inventory.read","orders.read","finance.managerial.read","documents.read","evidence.write","events.write","webhooks","admin.restricted"];
 public static readonly string[] WebhookEvents=["lot.created","lot.blocked","lot.released","inventory.critical","order.approved","purchase.received","document.expired","billing.overdue","tenant.blocked","tenant.unblocked","user.created","user.blocked","alert.critical","export.shipped","nonconformity.created"];
 public static void Module(string code,string name){if(!Code().IsMatch(code)||string.IsNullOrWhiteSpace(name))throw new ArgumentException("Código e nome válidos são obrigatórios.");}
 public static string Document(string value){var d=new string(value.Where(char.IsDigit).ToArray());if(d.Length is not(11 or 14))throw new ArgumentException("CPF/CNPJ inválido.");return d;}
 public static void Scopes(IEnumerable<string> scopes){var s=scopes.Distinct(StringComparer.Ordinal).ToArray();if(s.Length==0||s.Any(x=>!ApiScopes.Contains(x,StringComparer.Ordinal)))throw new ArgumentException("Informe escopos explícitos válidos.");}
 public static Uri Webhook(string value){if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.IsLoopback||string.IsNullOrWhiteSpace(uri.Host))throw new ArgumentException("Webhook deve usar HTTPS público.");return uri;}
 [GeneratedRegex("^[a-z][a-z0-9.-]{2,79}$")]private static partial Regex Code();
}
