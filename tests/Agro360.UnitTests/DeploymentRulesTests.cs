using Agro360.Domain.Deployment;
namespace Agro360.UnitTests;
public sealed class DeploymentRulesTests
{
 [Fact] public void Valid_onboarding_accepts_business_values()=>DeploymentRules.ValidateOnboarding("Fazenda Modelo","fazenda-modelo","GRAINS","São João","Ana","ana@agro.test","2026/27",["agriculture"]);
 [Fact] public void Onboarding_rejects_unknown_segment()=>Assert.Throws<ArgumentException>(()=>DeploymentRules.ValidateOnboarding("Fazenda","fazenda","OTHER","São João","Ana","ana@agro.test","2026",["agriculture"]));
 [Fact] public void Checklist_progress_uses_completed_required_items()=>Assert.Equal(75,DeploymentRules.Progress([true,true,true,false]));
 [Fact] public void Csv_requires_header_and_data()=>Assert.Throws<ArgumentException>(()=>DeploymentRules.ParseCsv("nome;email",';'));
 [Fact] public void Csv_preserves_line_values(){var rows=DeploymentRules.ParseCsv("nome;email\nAna;ana@agro.test",';');Assert.Equal("Ana",rows[1][0]);}
}
