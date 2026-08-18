using SmartMoney.Application.Scoring;
using SmartMoney.Domain.Enums;
using SmartMoney.Job.AI;
using SmartMoney.Job.Export;
using System.Text.Json;
using Xunit;

namespace SmartMoney.Application.Tests.AI;

public sealed class MarketInterpretationFoundationTests
{
    private static readonly MarketInterpretationResult ValidResult = new(
        "The setup is mixed and the canonical direction remains Neutral.",
        "FII is the main participant driver while Futures is the dominant indicator.",
        "Mixed participant evidence limits confidence.");

    [Fact]
    public void InputFactory_MapsEveryApprovedFieldExactly()
    {
        var decomposition = Decomposition();

        var input = MarketInterpretationInputFactory.Create(
            "2026-08-18", 22.5, "Neutral", "Mild", "NORMAL", 1.25,
            decomposition, "Authoritative explanation.");

        Assert.Equal("2026-08-18", input.signal_date);
        Assert.Equal(22.5, input.final_score);
        Assert.Equal("Neutral", input.displayed_direction);
        Assert.Equal("Mild", input.strength);
        Assert.Equal("NORMAL", input.regime);
        Assert.Equal(1.25, input.shock_score);
        Assert.Equal("FII", input.main_participant_driver);
        Assert.Equal(decomposition.participant_contributions.Select(x => (x.name, x.contribution)),
            input.participant_contributions.Select(x => (x.name, x.contribution)));
        Assert.Equal("Futures", input.main_indicator_driver);
        Assert.Equal(decomposition.indicator_contributions.Select(x => (x.name, x.contribution)),
            input.indicator_contributions.Select(x => (x.name, x.contribution)));
        Assert.Equal(0.7, input.smart_bias);
        Assert.Equal(-0.2, input.retail_bias);
        Assert.Equal(0.1, input.dii_bias);
        Assert.Equal(0.9, input.smart_retail_divergence);
        Assert.Equal(0.6, input.smart_dii_divergence);
        Assert.Equal("SmartBullRetailBear", input.smart_retail_state);
        Assert.Equal(0.65, input.participant_concentration);
        Assert.Equal("Mixed", input.participant_alignment);
        Assert.Equal("Aligned", input.indicator_alignment);
        Assert.Equal("Agree", input.dii_smart_relationship);
        Assert.Equal("Authoritative explanation.", input.deterministic_explanation);
    }

    [Fact]
    public void Fingerprint_IdenticalInputAndContext_IsStable()
    {
        var first = Fingerprint(Input(), Context());
        var second = Fingerprint(Input(), Context());

        Assert.Equal(first, second);
        Assert.StartsWith("sha256:", first);
        Assert.Equal(71, first.Length);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("prompt_version")]
    [InlineData("prompt_content")]
    [InlineData("model")]
    [InlineData("generation_setting")]
    public void Fingerprint_ResponseAffectingChange_Invalidates(string change)
    {
        var input = Input();
        var context = Context();
        var changedInput = change == "input" ? input with { shock_score = input.shock_score + 0.01 } : input;
        var changedContext = change switch
        {
            "prompt_version" => context with { prompt_version = "market-daily-v2" },
            "prompt_content" => context with { prompt_content = context.prompt_content + " changed" },
            "model" => context with { model = "model-b" },
            "generation_setting" => context with
            {
                generation_settings = new Dictionary<string, string> { ["temperature"] = "0.2" }
            },
            _ => context
        };

        Assert.NotEqual(Fingerprint(input, context), Fingerprint(changedInput, changedContext));
    }

    [Fact]
    public async Task Fingerprint_TimeoutChange_DoesNotInvalidate()
    {
        var first = await GenerateWithOptions(new MarketInterpretationOptions
        {
            Enabled = true, Provider = "fake", Model = "model-a", TimeoutSeconds = 5
        });
        var second = await GenerateWithOptions(new MarketInterpretationOptions
        {
            Enabled = true, Provider = "fake", Model = "model-a", TimeoutSeconds = 50
        });

        Assert.Equal(first!.input_fingerprint, second!.input_fingerprint);
    }

    [Fact]
    public async Task Service_Disabled_ReturnsNoAttemptAndDoesNotCallProvider()
    {
        var provider = new FakeProvider(ValidResult);
        var service = Service(provider, new MarketInterpretationOptions { Enabled = false });

        var attempt = await service.InterpretAsync(Input(), null, CancellationToken.None);

        Assert.Null(attempt);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public void JsonContract_NullInterpretationIsOmitted()
    {
        var dto = new MarketTodayDto(
            "NIFTY", "2026-08-18", 22.5, "NORMAL", 1.25, []);

        var json = JsonSerializer.Serialize(dto);

        Assert.DoesNotContain("ai_interpretation", json);
    }

    [Fact]
    public void Validator_AcceptsValidStructuredResult()
    {
        var validation = new MarketInterpretationValidator().Validate(ValidResult, Input());

        Assert.True(validation.is_valid);
        Assert.Empty(validation.errors);
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("key_observation")]
    [InlineData("uncertainty")]
    public void Validator_RejectsMissingRequiredField(string field)
    {
        var result = field switch
        {
            "summary" => ValidResult with { summary = " " },
            "key_observation" => ValidResult with { key_observation = "" },
            _ => ValidResult with { uncertainty = "\t" }
        };

        Assert.Contains($"{field}_required", Errors(result));
    }

    [Fact]
    public void Validator_RejectsPerFieldAndCombinedLengthLimits()
    {
        Assert.Contains("summary_too_long", Errors(ValidResult with { summary = new string('a', 401) }));

        var combined = new MarketInterpretationResult(
            new string('a', 350), new string('b', 350), new string('c', 350));
        Assert.Contains("combined_text_too_long", Errors(combined));
    }

    [Theory]
    [InlineData("Buy the index.")]
    [InlineData("This is a sell signal.")]
    [InlineData("Investors should hold.")]
    [InlineData("The price target is higher.")]
    [InlineData("This is a guaranteed outcome.")]
    [InlineData("You should enter a trade.")]
    [InlineData("Enter the market now.")]
    public void Validator_RejectsTradingLanguage(string prose)
        => Assert.Contains("trading_recommendation", Errors(ValidResult with { summary = prose }));

    [Theory]
    [InlineData("The score is 25.")]
    [InlineData("Confidence is 50%.")]
    [InlineData("The value is minus 2.5 points.")]
    public void Validator_RejectsNumericClaims(string prose)
        => Assert.Contains("numeric_claim", Errors(ValidResult with { summary = prose }));

    [Theory]
    [InlineData("The market will rise.")]
    [InlineData("The market could fall.")]
    [InlineData("A rally is expected to continue.")]
    [InlineData("This forecasts a decline.")]
    public void Validator_RejectsUnsupportedPrediction(string prose)
        => Assert.Contains("unsupported_prediction", Errors(ValidResult with { summary = prose }));

    [Fact]
    public void Validator_RejectsDirectionContradiction()
        => Assert.Contains("direction_contradiction",
            Errors(ValidResult with { summary = "The setup is Bearish." }));

    [Fact]
    public void Validator_RejectsRegimeContradiction()
        => Assert.Contains("regime_contradiction",
            Errors(ValidResult with { summary = "The Shock regime dominates." }));

    [Fact]
    public void Validator_RejectsWrongDominantParticipant()
        => Assert.Contains("participant_driver_contradiction",
            Errors(ValidResult with { key_observation = "DII is the dominant participant." }));

    [Fact]
    public void Validator_RejectsWrongDominantIndicator()
        => Assert.Contains("indicator_driver_contradiction",
            Errors(ValidResult with { key_observation = "Calls are the largest indicator driver." }));

    [Theory]
    [InlineData("News supports this setup.")]
    [InlineData("Earnings explain the alignment.")]
    [InlineData("A policy announcement is relevant.")]
    [InlineData("An economic event caused this.")]
    public void Validator_RejectsExternalFacts(string prose)
        => Assert.Contains("external_fact_reference", Errors(ValidResult with { summary = prose }));

    [Fact]
    public async Task Service_ProviderUnavailable_IsNonBlocking()
    {
        var service = Service(
            new DisabledMarketInterpretationProvider(),
            new MarketInterpretationOptions { Enabled = true, Provider = "disabled", Model = "none" });

        var attempt = await service.InterpretAsync(Input(), null, CancellationToken.None);

        Assert.Equal(MarketInterpretationStatus.Unavailable, attempt!.status);
        Assert.Equal("provider_unavailable", attempt.failure_category);
        Assert.Null(attempt.interpretation);
    }

    [Fact]
    public async Task Service_MalformedProviderResponse_IsInvalid()
    {
        var service = Service(new FakeProvider(null), EnabledOptions());

        var attempt = await service.InterpretAsync(Input(), null, CancellationToken.None);

        Assert.Equal(MarketInterpretationStatus.Invalid, attempt!.status);
        Assert.Contains("response_missing", attempt.failure_category);
    }

    [Fact]
    public async Task Service_SameFingerprintAndValidResult_IsReusedWithoutProviderCall()
    {
        var provider = new FakeProvider(ValidResult);
        var service = Service(provider, EnabledOptions());
        var generated = await service.InterpretAsync(Input(), null, CancellationToken.None);

        var reused = await service.InterpretAsync(Input(), generated, CancellationToken.None);

        Assert.Equal(MarketInterpretationStatus.Reused, reused!.status);
        Assert.Equal(generated!.generated_at, reused.generated_at);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task Service_MatchingFingerprintWithInvalidCachedText_IsNotReused()
    {
        var provider = new FakeProvider(ValidResult);
        var service = Service(provider, EnabledOptions());
        var generated = await service.InterpretAsync(Input(), null, CancellationToken.None);
        var invalidCached = generated! with
        {
            interpretation = ValidResult with { summary = "Buy the index." }
        };

        var attempt = await service.InterpretAsync(Input(), invalidCached, CancellationToken.None);

        Assert.Equal(MarketInterpretationStatus.Generated, attempt!.status);
        Assert.Equal(2, provider.CallCount);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("prompt")]
    [InlineData("model")]
    public async Task Service_ChangedFingerprint_DoesNotReuse(string change)
    {
        var firstProvider = new FakeProvider(ValidResult);
        var firstService = Service(firstProvider, EnabledOptions());
        var generated = await firstService.InterpretAsync(Input(), null, CancellationToken.None);

        var secondProvider = new FakeProvider(ValidResult);
        var secondOptions = EnabledOptions();
        if (change == "model") secondOptions.Model = "model-b";
        var secondPrompt = change == "prompt"
            ? new MarketInterpretationPrompt("market-daily-v2", "prompt-v2")
            : Prompt();
        var secondService = Service(secondProvider, secondOptions, secondPrompt);
        var secondInput = change == "input" ? Input() with { smart_bias = 0.8 } : Input();

        var attempt = await secondService.InterpretAsync(secondInput, generated, CancellationToken.None);

        Assert.Equal(MarketInterpretationStatus.Generated, attempt!.status);
        Assert.Equal(1, secondProvider.CallCount);
    }

    [Fact]
    public async Task Interpretation_DoesNotChangeDeterministicExplanationOrScoringOutputs()
    {
        var scoring = new MarketScoringCalculator();
        var biases = new Dictionary<ParticipantType, double>
        {
            [ParticipantType.FII] = 1.2,
            [ParticipantType.Pro] = -0.5,
            [ParticipantType.DII] = 0.8,
            [ParticipantType.Retail] = 0.0
        };
        var raw = scoring.ComputeMarketRawScore(biases);
        var final = scoring.ComputeFinalScore(raw);
        var regime = scoring.ComputeRegime(1.7);
        var explanation = MarketNarrative.Explanation("SHOCK", 1.7, final, Decomposition());
        var service = Service(new FakeProvider(ValidResult), EnabledOptions());

        _ = await service.InterpretAsync(Input() with { deterministic_explanation = explanation }, null, CancellationToken.None);

        Assert.Equal(raw, scoring.ComputeMarketRawScore(biases));
        Assert.Equal(final, scoring.ComputeFinalScore(raw));
        Assert.Equal(regime, scoring.ComputeRegime(1.7));
        Assert.Equal(explanation, MarketNarrative.Explanation("SHOCK", 1.7, final, Decomposition()));
    }

    private static IReadOnlyList<string> Errors(MarketInterpretationResult result)
        => new MarketInterpretationValidator().Validate(result, Input()).errors;

    private static string Fingerprint(
        MarketInterpretationInput input,
        MarketInterpretationFingerprintContext context)
        => MarketInterpretationFingerprint.Compute(input, context);

    private static MarketInterpretationFingerprintContext Context() => new(
        "market-daily-v1",
        "prompt-v1",
        "fake",
        "model-a",
        new Dictionary<string, string> { ["temperature"] = "0" });

    private static MarketInterpretationInput Input() => MarketInterpretationInputFactory.Create(
        "2026-08-18", 22.5, "Neutral", "Mild", "NORMAL", 1.25,
        Decomposition(), "Authoritative explanation.");

    private static MarketNarrativeDecomposition Decomposition() => new(
        participant_contributions:
        [
            new ContributionDto("FII", 0.4),
            new ContributionDto("PRO", -0.1),
            new ContributionDto("DII", 0.05),
            new ContributionDto("RETAIL", -0.02)
        ],
        main_participant_driver: "FII",
        indicator_contributions:
        [
            new ContributionDto("Futures", 0.3),
            new ContributionDto("Puts", 0.1),
            new ContributionDto("Calls", -0.05)
        ],
        main_indicator_driver: "Futures",
        participant_counts: new SignCountsDto(2, 2, 0),
        indicator_counts: new SignCountsDto(2, 1, 0),
        participant_concentration: 0.65,
        participant_alignment: "Mixed",
        indicator_alignment: "Aligned",
        dii_smart_relationship: "Agree",
        smart_bias: 0.7,
        retail_bias: -0.2,
        dii_bias: 0.1,
        smart_retail_divergence: 0.9,
        smart_dii_divergence: 0.6,
        smart_retail_state: "SmartBullRetailBear");

    private static MarketInterpretationOptions EnabledOptions() => new()
    {
        Enabled = true,
        Provider = "fake",
        Model = "model-a",
        TimeoutSeconds = 5,
        PromptVersion = "market-daily-v1",
        GenerationSettings = new Dictionary<string, string> { ["temperature"] = "0" }
    };

    private static MarketInterpretationPrompt Prompt() => new("market-daily-v1", "prompt-v1");

    private static MarketInterpretationService Service(
        IMarketInterpretationProvider provider,
        MarketInterpretationOptions options,
        MarketInterpretationPrompt? prompt = null) => new(
            provider,
            new MarketInterpretationValidator(),
            options,
            prompt ?? Prompt(),
            TimeProvider.System);

    private static Task<MarketInterpretationAttempt?> GenerateWithOptions(MarketInterpretationOptions options)
        => Service(new FakeProvider(ValidResult), options)
            .InterpretAsync(Input(), null, CancellationToken.None);

    private sealed class FakeProvider(MarketInterpretationResult? result) : IMarketInterpretationProvider
    {
        public int CallCount { get; private set; }

        public Task<MarketInterpretationResult?> GenerateAsync(
            MarketInterpretationInput input,
            MarketInterpretationPrompt prompt,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }
}
