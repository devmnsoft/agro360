using Agro360.Infrastructure.Services;
using Xunit;

namespace Agro360.UnitTests;

/// <summary>
/// Replay idempotente com autorização reduzida: o texto da resposta da LLM pode embutir custos que
/// eram visíveis a quem calculou; ocultar valores monetários é a única forma de não vazar dado
/// financeiro apenas sanitizando a tabela estruturada.
/// </summary>
public sealed class AiStockAssistantReplayTests
{
    [Theory]
    [InlineData("O custo médio é R$ 1.234,56 por saca.", "O custo médio é R$ ••• por saca.")]
    [InlineData("Custo R$99,90 e saldo normal", "Custo R$ ••• e saldo normal")]
    [InlineData("Total de R$ 5.000,00 registrados", "Total de R$ ••• registrados")]
    [InlineData("Nenhum valor monetário aqui", "Nenhum valor monetário aqui")]
    [InlineData("", "")]
    public void MascaraSomenteValoresMonetarios(string entrada, string esperado)
    {
        Assert.Equal(esperado, AiStockAssistant.MaskFinancialValues(entrada));
    }

    [Fact]
    public void MascaraTodosOsValoresDeUmTextoMultiplo()
    {
        var mascarado = AiStockAssistant.MaskFinancialValues("Soja R$ 210,00 e Milho R$ 89,50 abaixo do mínimo.");

        Assert.DoesNotContain("210", mascarado);
        Assert.DoesNotContain("89,50", mascarado);
        Assert.Contains("R$ •••", mascarado);
        // Dados operacionais (não financeiros) permanecem legíveis.
        Assert.Contains("abaixo do mínimo", mascarado);
    }
}
