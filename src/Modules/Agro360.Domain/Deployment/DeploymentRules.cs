namespace Agro360.Domain.Deployment;

public static class DeploymentRules
{
    public static readonly string[] Segments = ["GRAINS", "LIVESTOCK", "AMAZON", "COOPERATIVE", "AGROINDUSTRY", "MIXED", "LOGISTICS"];

    public static void ValidateOnboarding(string organization, string slug, string segment, string property,
        string administratorName, string administratorEmail, string cycle, IReadOnlyCollection<string> modules)
    {
        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(property) ||
            string.IsNullOrWhiteSpace(administratorName) || string.IsNullOrWhiteSpace(cycle) || modules.Count == 0)
            throw new ArgumentException("Organização, propriedade, administrador, ciclo e módulos são obrigatórios.");
        if (!Segments.Contains(segment, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Segmento agro inválido.");
        if (slug.Length is < 3 or > 80 || slug.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))
            throw new ArgumentException("Slug deve conter letras, números ou hífen.");
        if (!administratorEmail.Contains('@') || administratorEmail.Length > 254)
            throw new ArgumentException("E-mail do administrador inválido.");
    }

    public static int Progress(IEnumerable<bool> items)
    {
        var values = items.ToArray();
        return values.Length == 0 ? 0 : (int)Math.Round(values.Count(x => x) * 100m / values.Length);
    }

    public static IReadOnlyList<string[]> ParseCsv(string content, char delimiter)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("O CSV está vazio.");
        var rows = content.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(delimiter).Select(x => x.Trim().Trim('"')).ToArray()).ToArray();
        if (rows.Length < 2) throw new ArgumentException("O CSV deve possuir cabeçalho e ao menos uma linha.");
        if (rows[0].Any(string.IsNullOrWhiteSpace) || rows[0].Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows[0].Length)
            throw new ArgumentException("Cabeçalhos devem ser preenchidos e únicos.");
        return rows;
    }
}
