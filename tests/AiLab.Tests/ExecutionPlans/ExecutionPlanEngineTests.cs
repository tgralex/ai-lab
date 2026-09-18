using AiLab.Core.Execution;
using AiLab.Core.ExecutionPlans;
using AiLab.Core.Providers;
using AiLab.Core.Requests;
using AiLab.Core.Statistics;
using AiLab.Tests.TestDoubles;
using Xunit;

namespace AiLab.Tests.ExecutionPlans;

public class ExecutionPlanEngineTests
{
    private static AiRequestDefinition BuildRequest(string name, string providerId = "openai") => new()
    {
        WorkspaceId = Guid.NewGuid(),
        Name = name,
        ProviderId = providerId,
        ModelId = "test-model",
        UserContext = new ContentBlock { Text = "hi" },
    };

    // Node Id is set equal to the request id, mirroring the pre-existing-data backfill strategy —
    // keeps every test's edges (already expressed in terms of AiRequestDefinition.Id) working
    // unchanged now that node identity is distinct from AiRequestId in general.
    private static ExecutionPlan BuildPlan(IReadOnlyList<AiRequestDefinition> requests, IReadOnlyList<(Guid From, Guid To)> edges)
    {
        var plan = new ExecutionPlan { WorkspaceId = Guid.NewGuid(), Name = "test-plan" };
        plan.Requests.AddRange(requests.Select(r => new ExecutionPlanRequest { Id = r.Id, AiRequestId = r.Id }));
        plan.Dependencies.AddRange(edges.Select(e => new ExecutionPlanDependency { FromNodeId = e.From, ToNodeId = e.To }));
        return plan;
    }

    [Fact]
    public async Task ExecuteAsync_IndependentRequests_RunConcurrently()
    {
        var a = BuildRequest("A");
        var b = BuildRequest("B");
        var c = BuildRequest("C");
        var plan = BuildPlan([a, b, c], []);
        var fakeProvider = new FakeAiProvider { SimulatedDelay = TimeSpan.FromMilliseconds(50) };
        var engine = new ExecutionPlanEngine(new AiRequestExecutor([fakeProvider], new CostCalculator(), new FakeAttachmentContentProvider()));
        var requestsById = new[] { a, b, c }.ToDictionary(r => r.Id);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var run = await engine.ExecuteAsync(plan, requestsById, new BindingResolutionContext(), new Dictionary<Guid, Core.Models.ProviderModel?>(), new PlanExecutionOptions(), null, CancellationToken.None);
        sw.Stop();

        Assert.Equal(3, fakeProvider.MaxConcurrentCalls);
        // Wall clock should be close to one delay (concurrent), not three (sequential).
        Assert.True(sw.ElapsedMilliseconds < 150, $"Expected concurrent execution (<150ms), took {sw.ElapsedMilliseconds}ms");
        Assert.Equal(Core.Execution.ExecutionStatus.Completed, run.Status);
    }

    [Fact]
    public async Task ExecuteAsync_DiamondDependency_DownstreamWaitsForBothUpstream()
    {
        var a = BuildRequest("A");
        var b = BuildRequest("B");
        var d = BuildRequest("D");
        var plan = BuildPlan([a, b, d], [(a.Id, d.Id), (b.Id, d.Id)]);
        var fakeProvider = new FakeAiProvider { SimulatedDelay = TimeSpan.FromMilliseconds(30) };
        var engine = new ExecutionPlanEngine(new AiRequestExecutor([fakeProvider], new CostCalculator(), new FakeAttachmentContentProvider()));
        var requestsById = new[] { a, b, d }.ToDictionary(r => r.Id);

        var run = await engine.ExecuteAsync(plan, requestsById, new BindingResolutionContext(), new Dictionary<Guid, Core.Models.ProviderModel?>(), new PlanExecutionOptions(), null, CancellationToken.None);

        Assert.Equal(2, run.Groups.Count);
        Assert.Equal(2, run.Groups[0].NodeIds.Count); // A, B
        Assert.Single(run.Groups[1].NodeIds); // D
        Assert.Equal(Core.Execution.ExecutionStatus.Completed, run.Status);
    }

    [Fact]
    public async Task ExecuteAsync_GlobalConcurrencyLimit_NeverExceeded()
    {
        var requests = Enumerable.Range(0, 6).Select(i => BuildRequest($"R{i}")).ToList();
        var plan = BuildPlan(requests, []);
        var fakeProvider = new FakeAiProvider { SimulatedDelay = TimeSpan.FromMilliseconds(40) };
        var engine = new ExecutionPlanEngine(new AiRequestExecutor([fakeProvider], new CostCalculator(), new FakeAttachmentContentProvider()));
        var requestsById = requests.ToDictionary(r => r.Id);

        await engine.ExecuteAsync(plan, requestsById, new BindingResolutionContext(), new Dictionary<Guid, Core.Models.ProviderModel?>(), new PlanExecutionOptions { GlobalMaxConcurrency = 2 }, null, CancellationToken.None);

        Assert.True(fakeProvider.MaxConcurrentCalls <= 2, $"Expected max 2 concurrent calls, saw {fakeProvider.MaxConcurrentCalls}");
    }

    [Fact]
    public async Task ExecuteAsync_ProviderSpecificConcurrencyLimit_AppliesPerProvider()
    {
        var openAiRequests = Enumerable.Range(0, 4).Select(i => BuildRequest($"OA{i}", "openai")).ToList();
        var anthropicRequests = Enumerable.Range(0, 4).Select(i => BuildRequest($"AN{i}", "anthropic")).ToList();
        var allRequests = openAiRequests.Concat(anthropicRequests).ToList();
        var plan = BuildPlan(allRequests, []);

        var openAiProvider = new FakeAiProvider("openai") { SimulatedDelay = TimeSpan.FromMilliseconds(40) };
        var anthropicProvider = new FakeAiProvider("anthropic") { SimulatedDelay = TimeSpan.FromMilliseconds(40) };
        var engine = new ExecutionPlanEngine(new AiRequestExecutor([openAiProvider, anthropicProvider], new CostCalculator(), new FakeAttachmentContentProvider()));
        var requestsById = allRequests.ToDictionary(r => r.Id);

        var options = new PlanExecutionOptions
        {
            GlobalMaxConcurrency = 8,
            ProviderMaxConcurrency = new Dictionary<string, int> { ["openai"] = 1, ["anthropic"] = 2 },
        };

        await engine.ExecuteAsync(plan, requestsById, new BindingResolutionContext(), new Dictionary<Guid, Core.Models.ProviderModel?>(), options, null, CancellationToken.None);

        Assert.Equal(1, openAiProvider.MaxConcurrentCalls);
        Assert.Equal(2, anthropicProvider.MaxConcurrentCalls);
    }

    [Fact]
    public async Task ExecuteAsync_FailPlanPolicy_CancelsDownstreamWork()
    {
        var a = BuildRequest("A");
        a.FailurePolicy = FailurePolicy.FailPlan;
        var b = BuildRequest("B"); // depends on A, should never run
        var plan = BuildPlan([a, b], [(a.Id, b.Id)]);

        var fakeProvider = new FakeAiProvider
        {
            ResultToReturn = new ProviderExecutionResult { Success = false, Failure = new Core.Execution.FailureInfo { Message = "boom" } },
        };
        var engine = new ExecutionPlanEngine(new AiRequestExecutor([fakeProvider], new CostCalculator(), new FakeAttachmentContentProvider()));
        var requestsById = new[] { a, b }.ToDictionary(r => r.Id);

        var run = await engine.ExecuteAsync(plan, requestsById, new BindingResolutionContext(), new Dictionary<Guid, Core.Models.ProviderModel?>(), new PlanExecutionOptions(), null, CancellationToken.None);

        Assert.Equal(1, fakeProvider.CallCount); // B never actually called the provider
        Assert.Equal(Core.Execution.ExecutionStatus.Failed, run.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ContinueWithErrorPolicy_DownstreamStillRunsWithEmptyBinding()
    {
        var a = BuildRequest("A");
        a.FailurePolicy = FailurePolicy.ContinueWithError;
        var b = BuildRequest("B");
        b.UserContext = new ContentBlock { Text = "Upstream said: {{A.output}}" };
        var plan = BuildPlan([a, b], [(a.Id, b.Id)]);

        var callCount = 0;
        var fakeProvider = new FakeAiProvider
        {
            Behavior = (ctx, progress, ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return Task.FromResult(new ProviderExecutionResult { Success = false, Failure = new Core.Execution.FailureInfo { Message = "boom" } });
                }

                return Task.FromResult(new ProviderExecutionResult { Success = true, OutputText = $"received: {ctx.UserContextText}" });
            },
        };
        var engine = new ExecutionPlanEngine(new AiRequestExecutor([fakeProvider], new CostCalculator(), new FakeAttachmentContentProvider()));
        var requestsById = new[] { a, b }.ToDictionary(r => r.Id);

        var run = await engine.ExecuteAsync(plan, requestsById, new BindingResolutionContext(), new Dictionary<Guid, Core.Models.ProviderModel?>(), new PlanExecutionOptions(), null, CancellationToken.None);

        Assert.Equal(2, fakeProvider.CallCount); // B still ran despite A's failure
        Assert.Equal(2, run.ExecutionRunIds.Count);
    }

    [Fact]
    public async Task ExecuteAsync_RetryPolicy_RetriesThenSucceeds()
    {
        var a = BuildRequest("A");
        a.FailurePolicy = FailurePolicy.Retry;
        a.RetryPolicy = new RetryPolicy { MaxRetries = 2, BaseBackoffMs = 1 };
        var plan = BuildPlan([a], []);

        var callCount = 0;
        var fakeProvider = new FakeAiProvider
        {
            Behavior = (ctx, progress, ct) =>
            {
                callCount++;
                return Task.FromResult(callCount < 2
                    ? new ProviderExecutionResult { Success = false, Failure = new Core.Execution.FailureInfo { Message = "transient" } }
                    : new ProviderExecutionResult { Success = true, OutputText = "ok" });
            },
        };
        var engine = new ExecutionPlanEngine(new AiRequestExecutor([fakeProvider], new CostCalculator(), new FakeAttachmentContentProvider()));
        var requestsById = new[] { a }.ToDictionary(r => r.Id);

        var run = await engine.ExecuteAsync(plan, requestsById, new BindingResolutionContext(), new Dictionary<Guid, Core.Models.ProviderModel?>(), new PlanExecutionOptions(), null, CancellationToken.None);

        Assert.Equal(2, callCount);
        Assert.Equal(Core.Execution.ExecutionStatus.Completed, run.Status);
    }

    [Fact]
    public async Task ExecuteAsync_PlanStats_WallClockLessThanCumulativeWhenParallel()
    {
        var requests = Enumerable.Range(0, 3).Select(i => BuildRequest($"R{i}")).ToList();
        var plan = BuildPlan(requests, []);
        var fakeProvider = new FakeAiProvider { SimulatedDelay = TimeSpan.FromMilliseconds(50) };
        var engine = new ExecutionPlanEngine(new AiRequestExecutor([fakeProvider], new CostCalculator(), new FakeAttachmentContentProvider()));
        var requestsById = requests.ToDictionary(r => r.Id);

        var run = await engine.ExecuteAsync(plan, requestsById, new BindingResolutionContext(), new Dictionary<Guid, Core.Models.ProviderModel?>(), new PlanExecutionOptions(), null, CancellationToken.None);

        Assert.True(run.WallClockDuration < run.CumulativeRequestDuration,
            $"Expected wall-clock ({run.WallClockDuration}) < cumulative ({run.CumulativeRequestDuration}) to prove real parallelism");
    }

    [Fact]
    public async Task ExecuteAsync_DownstreamBindingResolvesUpstreamOutput()
    {
        var a = BuildRequest("ParseResume");
        var b = BuildRequest("AssessJob");
        b.UserContext = new ContentBlock { Text = "Resume data: {{ParseResume.output}}" };
        var plan = BuildPlan([a, b], [(a.Id, b.Id)]);

        string? capturedUserContext = null;
        var fakeProvider = new FakeAiProvider
        {
            Behavior = (ctx, progress, ct) =>
            {
                if (ctx.UserContextText?.Contains("Resume data:") == true)
                {
                    capturedUserContext = ctx.UserContextText;
                }

                return Task.FromResult(new ProviderExecutionResult { Success = true, OutputText = "parsed-resume-json" });
            },
        };
        var engine = new ExecutionPlanEngine(new AiRequestExecutor([fakeProvider], new CostCalculator(), new FakeAttachmentContentProvider()));
        var requestsById = new[] { a, b }.ToDictionary(r => r.Id);

        await engine.ExecuteAsync(plan, requestsById, new BindingResolutionContext(), new Dictionary<Guid, Core.Models.ProviderModel?>(), new PlanExecutionOptions(), null, CancellationToken.None);

        // The {{...}} binding resolves inline as before. It must NOT also get restated in an
        // appended JSON block — a dependency already pulled in by an explicit binding is excluded
        // from that block (see ExecuteAsync_AlwaysAppendsPriorOutputsAsJson) specifically so a
        // "sum everything you see" style prompt can't double-count the same upstream value.
        Assert.Equal("Resume data: parsed-resume-json", capturedUserContext);
    }

    [Fact]
    public async Task ExecuteAsync_AlwaysAppendsPriorOutputsAsJson_EvenWhenTemplateBindingDoesNotMatch()
    {
        // "Combine" is reused as two nodes with different upstream dependencies. Its one shared
        // {{TaskOne.output}} binding is only ever correct for the first instance — the second
        // instance's real dependency is "TaskTwo", which the hand-written template never mentions.
        // The auto-appended JSON block should still carry the second instance's actual upstream
        // output regardless, so the reused request self-heals instead of silently seeing nothing.
        var taskOne = BuildRequest("TaskOne");
        taskOne.ModelId = "model-one";
        var taskTwo = BuildRequest("TaskTwo");
        taskTwo.ModelId = "model-two";
        var combine = BuildRequest("Combine");
        combine.UserContext = new ContentBlock { Text = "{{TaskOne.output}}" };

        var plan = new ExecutionPlan { WorkspaceId = Guid.NewGuid(), Name = "test-plan" };
        var n1 = Guid.NewGuid();
        var n2 = Guid.NewGuid();
        plan.Requests.Add(new ExecutionPlanRequest { Id = n1, AiRequestId = taskOne.Id });
        plan.Requests.Add(new ExecutionPlanRequest { Id = n2, AiRequestId = taskTwo.Id });
        plan.Requests.Add(new ExecutionPlanRequest { Id = Guid.NewGuid(), AiRequestId = combine.Id }); // label null -> "Combine"
        plan.Requests.Add(new ExecutionPlanRequest { Id = Guid.NewGuid(), AiRequestId = combine.Id, Label = "Combine (2)" });
        plan.Dependencies.Add(new ExecutionPlanDependency { FromNodeId = n1, ToNodeId = plan.Requests[2].Id });
        plan.Dependencies.Add(new ExecutionPlanDependency { FromNodeId = n2, ToNodeId = plan.Requests[3].Id });

        string? combine2UserContext = null;
        var fakeProvider = new FakeAiProvider
        {
            Behavior = (ctx, progress, ct) =>
            {
                var output = ctx.ModelId switch { "model-one" => "111", "model-two" => "222", _ => "combined" };
                if (ctx.UserContextText?.Contains("TaskTwo") == true)
                {
                    combine2UserContext = ctx.UserContextText;
                }

                return Task.FromResult(new ProviderExecutionResult { Success = true, OutputText = output });
            },
        };
        var engine = new ExecutionPlanEngine(new AiRequestExecutor([fakeProvider], new CostCalculator(), new FakeAttachmentContentProvider()));
        var requestsById = new[] { taskOne, taskTwo, combine }.ToDictionary(r => r.Id);

        await engine.ExecuteAsync(plan, requestsById, new BindingResolutionContext(), new Dictionary<Guid, Core.Models.ProviderModel?>(), new PlanExecutionOptions(), null, CancellationToken.None);

        Assert.NotNull(combine2UserContext);
        Assert.Contains("{{TaskOne.output}}", combine2UserContext); // stale binding stays unresolved verbatim, not silently blanked
        Assert.Contains("\"TaskTwo\"", combine2UserContext); // ...but the real upstream output is still there via the JSON block
        Assert.Contains("222", combine2UserContext);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_RecordsCanceledNotFailed()
    {
        var requests = Enumerable.Range(0, 3).Select(i => BuildRequest($"R{i}")).ToList();
        var plan = BuildPlan(requests, []);
        using var cts = new CancellationTokenSource();
        var fakeProvider = new FakeAiProvider
        {
            Behavior = async (ctx, progress, ct) =>
            {
                cts.Cancel();
                await Task.Delay(200, CancellationToken.None); // let the cancellation propagate to sibling waiters
                ct.ThrowIfCancellationRequested();
                return new ProviderExecutionResult { Success = true };
            },
        };
        var engine = new ExecutionPlanEngine(new AiRequestExecutor([fakeProvider], new CostCalculator(), new FakeAttachmentContentProvider()));
        var requestsById = requests.ToDictionary(r => r.Id);

        var run = await engine.ExecuteAsync(plan, requestsById, new BindingResolutionContext(), new Dictionary<Guid, Core.Models.ProviderModel?>(), new PlanExecutionOptions { GlobalMaxConcurrency = 1 }, null, cts.Token);

        Assert.Contains(run.ExecutionRunIds, _ => true); // at least the first node ran
        Assert.Equal(Core.Execution.ExecutionStatus.Canceled, run.Status);
    }
}
