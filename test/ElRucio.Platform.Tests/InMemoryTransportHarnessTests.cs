namespace ElRucio.Platform.Tests;

public class InMemoryTransportHarnessTests
{
    public static IEnumerable<object[]> GoldenScenarios()
    {
        yield return [new GoldenScenarioBuilder("start command")
            .Input("/start")
            .ExpectContains("El Rucio connected.")
            .Build()];

        yield return [new GoldenScenarioBuilder("plain prompt")
            .Input("tell me a short joke")
            .ExpectContains("agent-response")
            .Build()];

        yield return [new GoldenScenarioBuilder("natural schedule")
            .Input("schedule a task for 3 minutes from now to send me focus bullets")
            .ExpectContains("Scheduled one-time task")
            .Then(async (fixture, harness, _) =>
            {
                var tasks = await fixture.ScheduledTaskStore.ListAsync(harness.Conversation.ChatKey, CancellationToken.None);
                var created = Assert.Single(tasks);
                Assert.StartsWith("once:", created.Cron, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("send me focus bullets", created.Prompt, StringComparison.OrdinalIgnoreCase);
            })
            .Build()];

        yield return [new GoldenScenarioBuilder("risk gate")
            .Input("please delete all records right now")
            .ExpectContains("Risk gate triggered")
            .ExpectContains("/approve")
            .Build()];
    }

    [Theory]
    [MemberData(nameof(GoldenScenarios))]
    public async Task Golden_Scenarios_ProduceExpectedOutputs(GoldenScenario scenario)
    {
        var fixture = await InboundTestFixture.CreateAsync();
        var harness = new InMemoryTransportHarness(fixture.Processor, fixture.DefaultConversation);

        await GoldenScenarioRunner.RunAsync(scenario, fixture, harness);
    }
}
