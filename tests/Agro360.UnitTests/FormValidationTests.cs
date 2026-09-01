using System.Globalization;
using Agro360.SharedKernel;

namespace Agro360.UnitTests;

public sealed class FormValidationTests
{
    [Theory]
    [InlineData("529.982.247-25", true)] [InlineData("111.111.111-11", false)] [InlineData("123", false)]
    public void Cpf_is_validated_on_server(string value, bool expected) => Assert.Equal(expected, FormValidation.IsValidCpf(value));

    [Theory]
    [InlineData("04.252.011/0001-10", true)] [InlineData("11.111.111/1111-11", false)] [InlineData("123", false)]
    public void Cnpj_is_validated_on_server(string value, bool expected) => Assert.Equal(expected, FormValidation.IsValidCnpj(value));

    [Theory]
    [InlineData(" Pessoa@Empresa.COM.BR ", "pessoa@empresa.com.br", true)] [InlineData("sem-dominio", "sem-dominio", false)]
    public void Email_is_normalized_and_validated(string value, string normalized, bool valid)
    { Assert.Equal(normalized, FormValidation.NormalizeEmail(value)); Assert.Equal(valid, FormValidation.IsValidEmail(value)); }

    [Fact] public void Decimal_respects_selected_culture()
    { Assert.True(FormValidation.TryParseDecimal("1.234,56", CultureInfo.GetCultureInfo("pt-BR"), out var amount)); Assert.Equal(1234.56m, amount); }

    [Fact] public void End_date_cannot_precede_start_date() => Assert.False(FormValidation.IsValidDateRange(new(2026, 9, 2), new(2026, 9, 1)));
}
