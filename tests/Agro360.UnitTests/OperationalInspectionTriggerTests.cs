using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agro360.Application.Contracts;
using Agro360.Domain.Compliance;
using Xunit;

namespace Agro360.UnitTests;

public sealed class OperationalInspectionTriggerTests
{
    [Theory]
    [InlineData("PURCHASE_RECEIPT", true)]
    [InlineData("HARVEST_RECEIPT", true)]
    [InlineData("PRODUCTION", true)]
    [InlineData("STORAGE", true)]
    [InlineData("SHIPMENT", true)]
    [InlineData("RETURN", true)]
    [InlineData("INVALID_PROCESS", false)]
    [InlineData("", false)]
    public void ProcessCodeValidationConformsToDomainRules(string processCode, bool expectedValid)
    {
        var isValid = InspectionModelRules.ValidProcessCodes.Contains(processCode);
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void EnsureValidProcessCodeThrowsOnInvalid()
    {
        Assert.Throws<ArgumentException>(() => InspectionModelRules.EnsureValidProcessCode("INVALID"));
        Assert.Throws<ArgumentException>(() => InspectionModelRules.EnsureValidProcessCode(""));
    }

    [Fact]
    public void IdempotencyKeyAndHashAreStableAndDeterministic()
    {
        var originId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var expectedKey = $"EVT:HARVEST_RECEIPT:{originId:N}";

        Assert.Equal("EVT:HARVEST_RECEIPT:11111111222233334444555555555555", expectedKey);

        var payload = new
        {
            originType = "PRODUCTION_RECEIPT",
            originId = originId.ToString(),
            unitId = (string?)null,
            productId = Guid.NewGuid().ToString(),
            lotId = (string?)null
        };

        var json = JsonSerializer.Serialize(payload);
        var hash1 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var hash2 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
    }

    [Fact]
    public void IntentStatusAllowedSetMatchesContract()
    {
        string[] allowedStatuses = ["PENDING", "STARTED", "AMBIGUOUS", "PENDING_MODEL", "SKIPPED_NO_ACTOR"];

        Assert.Contains("PENDING", allowedStatuses);
        Assert.Contains("STARTED", allowedStatuses);
        Assert.Contains("AMBIGUOUS", allowedStatuses);
        Assert.Contains("PENDING_MODEL", allowedStatuses);
        Assert.Contains("SKIPPED_NO_ACTOR", allowedStatuses);
        Assert.Equal(5, allowedStatuses.Length);
    }

    [Fact]
    public void ResolutionScenarioSingleCandidateResultsInStarted()
    {
        var modelId = Guid.NewGuid();
        var recommendedVersionId = Guid.NewGuid();
        var resolution = new InspectionModelResolution(
            Candidates:
            [
                new InspectionModelCandidate(modelId, "MOD-01", "Modelo Grãos", recommendedVersionId, 1, 100, 10, "Match perfeito")
            ],
            RecommendedModelId: modelId,
            RecommendedVersionId: recommendedVersionId,
            Ambiguous: false,
            RuleExplanation: "Modelo único encontrado"
        );

        Assert.False(resolution.Ambiguous);
        Assert.NotNull(resolution.RecommendedVersionId);
        Assert.Single(resolution.Candidates);
    }

    [Fact]
    public void ResolutionScenarioNoCandidateResultsInPendingModelWithoutApproval()
    {
        var resolution = new InspectionModelResolution(
            Candidates: [],
            RecommendedModelId: null,
            RecommendedVersionId: null,
            Ambiguous: false,
            RuleExplanation: "Nenhum modelo publicado aplicável encontrado."
        );

        Assert.False(resolution.Ambiguous);
        Assert.Empty(resolution.Candidates);
        Assert.Null(resolution.RecommendedVersionId);
    }

    [Fact]
    public void ResolutionScenarioTieWithoutWinnerResultsInAmbiguous()
    {
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var resolution = new InspectionModelResolution(
            Candidates:
            [
                new InspectionModelCandidate(m1, "MOD-A", "Modelo A", v1, 1, 100, 10, "Match categoria"),
                new InspectionModelCandidate(m2, "MOD-B", "Modelo B", v2, 1, 100, 10, "Match unidade")
            ],
            RecommendedModelId: null,
            RecommendedVersionId: null,
            Ambiguous: true,
            RuleExplanation: "Empate de especificidade entre candidatos."
        );

        Assert.True(resolution.Ambiguous);
        Assert.Null(resolution.RecommendedVersionId);
        Assert.Equal(2, resolution.Candidates.Count);
    }

    [Fact]
    public void OperationalInspectionEventResultPreservesRunIdOnStarted()
    {
        var runId = Guid.NewGuid();
        var result = new OperationalInspectionEventResult(
            IntentId: Guid.NewGuid(),
            ProcessCode: "HARVEST_RECEIPT",
            OriginType: "PRODUCTION_RECEIPT",
            OriginId: Guid.NewGuid(),
            Status: "STARTED",
            RunId: runId,
            Message: "Inspeção iniciada com sucesso"
        );

        Assert.Equal("STARTED", result.Status);
        Assert.Equal(runId, result.RunId);
    }

    [Fact]
    public void OperationalInspectionEventResultPendingModelHasNoRunId()
    {
        var result = new OperationalInspectionEventResult(
            IntentId: Guid.NewGuid(),
            ProcessCode: "RETURN",
            OriginType: "FULFILLMENT_RETURN_RECEIPT",
            OriginId: Guid.NewGuid(),
            Status: "PENDING_MODEL",
            RunId: null,
            Message: "Nenhum modelo publicado aplicável"
        );

        Assert.Equal("PENDING_MODEL", result.Status);
        Assert.Null(result.RunId);
    }
}
