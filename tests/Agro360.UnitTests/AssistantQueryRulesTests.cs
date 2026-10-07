using Agro360.Application.Contracts;
using Xunit;

namespace Agro360.UnitTests;

public sealed class AssistantQueryRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" a ")]
    public void NormalizeQuestionRejectsMissingOrTooShortQuestions(string? question)
    {
        Assert.Throws<ArgumentException>(() => AssistantQueryRules.NormalizeQuestion(question));
    }

    [Fact]
    public void NormalizeQuestionTrimsWhitespaceAndAcceptsMaximumLength()
    {
        var question = $"  {new string('a', AssistantQueryRules.MaximumQuestionLength - 4)}  ";

        var normalized = AssistantQueryRules.NormalizeQuestion(question);

        Assert.Equal(AssistantQueryRules.MaximumQuestionLength - 4, normalized.Length);
    }

    [Fact]
    public void NormalizeQuestionRejectsOversizedPayloadIncludingWhitespace()
    {
        var question = $"{new string(' ', 2)}{new string('a', AssistantQueryRules.MaximumQuestionLength - 1)}";

        Assert.Throws<ArgumentException>(() => AssistantQueryRules.NormalizeQuestion(question));
    }
}
