namespace ElRucio.Platform.Tests;

public sealed record GoldenScenario(
    string Name,
    string Input,
    IReadOnlyList<string> ExpectedFragments,
    Func<InboundTestFixture, InMemoryTransportHarness, string, Task>? Assertion);

public sealed class GoldenScenarioBuilder(string name)
{
    private readonly List<string> _expectedFragments = [];
    private string _input = string.Empty;
    private Func<InboundTestFixture, InMemoryTransportHarness, string, Task>? _assertion;

    public GoldenScenarioBuilder Input(string input)
    {
        _input = input;
        return this;
    }

    public GoldenScenarioBuilder ExpectContains(string fragment)
    {
        _expectedFragments.Add(fragment);
        return this;
    }

    public GoldenScenarioBuilder Then(Func<InboundTestFixture, InMemoryTransportHarness, string, Task> assertion)
    {
        _assertion = assertion;
        return this;
    }

    public GoldenScenario Build()
    {
        if (string.IsNullOrWhiteSpace(_input))
        {
            throw new InvalidOperationException("Scenario input is required.");
        }

        return new GoldenScenario(name, _input, _expectedFragments, _assertion);
    }
}

public static class GoldenScenarioRunner
{
    public static async Task RunAsync(GoldenScenario scenario, InboundTestFixture fixture, InMemoryTransportHarness harness)
    {
        var reply = await harness.SubmitAsync(scenario.Input);

        foreach (var fragment in scenario.ExpectedFragments)
        {
            Assert.Contains(fragment, reply, StringComparison.OrdinalIgnoreCase);
        }

        if (scenario.Assertion is not null)
        {
            await scenario.Assertion(fixture, harness, reply);
        }
    }
}
