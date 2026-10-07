namespace Agro360.Application.Contracts;

public static class AssistantQueryRules
{
    public const int MaximumQuestionLength = 300;

    public static string NormalizeQuestion(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Informe uma pergunta.", nameof(question));

        if (question.Length > MaximumQuestionLength)
            throw new ArgumentException($"A pergunta deve ter no máximo {MaximumQuestionLength} caracteres.", nameof(question));

        var normalized = question.Trim();
        if (normalized.Length < 3)
            throw new ArgumentException("A pergunta deve ter pelo menos 3 caracteres.", nameof(question));

        return normalized;
    }
}
