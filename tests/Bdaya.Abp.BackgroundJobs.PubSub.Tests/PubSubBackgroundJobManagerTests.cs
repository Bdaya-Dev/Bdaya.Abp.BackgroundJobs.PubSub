using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Modularity;
using Volo.Abp.Testing;

namespace Bdaya.Abp.BackgroundJobs.PubSub.Tests;

/// <summary>
/// Integration tests for PubSubBackgroundJobManager.
/// These tests require the Pub/Sub emulator to be running.
/// </summary>
[Collection("PubSubEmulator")]
public class PubSubBackgroundJobManagerTests(PubSubEmulatorFixture fixture) : IClassFixture<PubSubEmulatorFixture>, IAsyncLifetime
{
    private readonly PubSubEmulatorFixture _fixture = fixture;
    private IAbpApplicationWithInternalServiceProvider? _application;
    private IServiceScope? _scope;

    public async ValueTask InitializeAsync()
    {
        // Set emulator configuration before creating the application
        PubSubTestModule.EmulatorHost = _fixture.EmulatorHost;
        PubSubTestModule.ProjectId = _fixture.ProjectId;

        _application = await AbpApplicationFactory.CreateAsync<PubSubTestModule>(options =>
        {
            options.UseAutofac();
        });

        await _application.InitializeAsync();
        _scope = _application.ServiceProvider.CreateScope();

        // Reset test handlers
        TestJobHandler.Reset();
        CustomNamedJobHandler.Reset();
        DelayedJobHandler.Reset();
        PriorityJobHandler.Reset();
    }

    public async ValueTask DisposeAsync()
    {
        _scope?.Dispose();

        if (_application != null)
        {
            await _application.ShutdownAsync();
            _application.Dispose();
        }
    }

    [Fact]
    public void Should_Resolve_BackgroundJobManager()
    {
        // Arrange & Act
        var jobManager = _scope!.ServiceProvider.GetRequiredService<IBackgroundJobManager>();

        // Assert
        jobManager.ShouldNotBeNull();
        jobManager.ShouldBeOfType<PubSubBackgroundJobManager>();
    }

    [Fact]
    public void Should_Resolve_PubSubBackgroundJobManager()
    {
        // Arrange & Act
        var jobManager = _scope!.ServiceProvider.GetRequiredService<IPubSubBackgroundJobManager>();

        // Assert
        jobManager.ShouldNotBeNull();
    }

    [Fact]
    public async Task Should_Enqueue_Job()
    {
        // Arrange
        var jobManager = _scope!.ServiceProvider.GetRequiredService<IBackgroundJobManager>();
        var args = new TestJobArgs
        {
            Message = "Test Message",
            Value = 123
        };

        // Act
        var jobId = await jobManager.EnqueueAsync(args);

        // Assert
        jobId.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Should_Enqueue_Multiple_Jobs()
    {
        // Arrange
        var jobManager = _scope!.ServiceProvider.GetRequiredService<IBackgroundJobManager>();
        var jobIds = new List<string>();

        // Act
        for (int i = 0; i < 5; i++)
        {
            var args = new TestJobArgs
            {
                Message = $"Test Message {i}",
                Value = i
            };
            var jobId = await jobManager.EnqueueAsync(args);
            jobIds.Add(jobId);
        }

        // Assert
        jobIds.Count.ShouldBe(5);
        jobIds.ShouldAllBe(id => !string.IsNullOrEmpty(id));
        jobIds.Distinct().Count().ShouldBe(5); // All unique IDs
    }

    [Fact]
    public async Task Should_Enqueue_Delayed_Job()
    {
        // Arrange
        var jobManager = _scope!.ServiceProvider.GetRequiredService<IBackgroundJobManager>();
        var args = new DelayedJobArgs
        {
            Payload = "Delayed Payload",
            ScheduledFor = DateTime.UtcNow.AddMinutes(5)
        };

        // Act
        var jobId = await jobManager.EnqueueAsync(args, delay: TimeSpan.FromMinutes(5));

        // Assert
        jobId.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Should_Enqueue_Job_With_Priority()
    {
        // Arrange
        var jobManager = _scope!.ServiceProvider.GetRequiredService<IBackgroundJobManager>();
        var args = new PriorityJobArgs
        {
            JobId = Guid.NewGuid().ToString(),
            Priority = 1
        };

        // Act
        var jobId = await jobManager.EnqueueAsync(args, BackgroundJobPriority.High);

        // Assert
        jobId.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Should_Create_Topic_Automatically()
    {
        // Arrange
        var jobManager = _scope!.ServiceProvider.GetRequiredService<IBackgroundJobManager>();
        var connectionPool = _scope!.ServiceProvider.GetRequiredService<IPubSubConnectionPool>();
        var args = new TestJobArgs { Message = "Test" };

        // Act
        await jobManager.EnqueueAsync(args);

        // Assert - If we got here without exception, topic was created
        // Verify by getting publisher (which would fail if topic doesn't exist in strict mode)
        var publisher = await connectionPool.GetPublisherAsync();
        publisher.ShouldNotBeNull();
    }

    [Fact]
    public async Task Should_Use_Custom_Job_Name_For_Topic()
    {
        // Arrange
        var jobManager = _scope!.ServiceProvider.GetRequiredService<IBackgroundJobManager>();
        var args = new CustomNamedJobArgs { Data = "Custom Data" };

        // Act
        var jobId = await jobManager.EnqueueAsync(args);

        // Assert
        jobId.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void ConnectionPool_Should_Return_Same_Connection()
    {
        // Arrange
        var connectionPool = _scope!.ServiceProvider.GetRequiredService<IPubSubConnectionPool>();

        // Act
        var connection1 = connectionPool.GetConnection();
        var connection2 = connectionPool.GetConnection("Default");

        // Assert
        connection1.ShouldBeSameAs(connection2);
        connection1.ProjectId.ShouldBe(_fixture.ProjectId);
        connection1.EmulatorHost.ShouldBe(_fixture.EmulatorHost);
    }

    [Fact]
    public async Task Should_Execute_Job_Handler_EndToEnd()
    {
        // Arrange — this is the critical test that validates the full
        // enqueue → subscribe → deserialize → resolve handler → execute path.
        // It catches the bug where ExecuteJobAsync passed argsType instead of
        // the handler jobType to BackgroundJobExecuter.
        var jobManager = _scope!.ServiceProvider.GetRequiredService<IPubSubBackgroundJobManager>();
        TestJobHandler.Reset();

        var args = new TestJobArgs
        {
            Message = "E2E Test",
            Value = 999
        };

        // Act — start subscriber, then enqueue
        await jobManager.StartProcessingAsync<TestJobArgs>();
        await jobManager.EnqueueAsync(args);

        // Assert — wait for the handler to process the message
        var timeout = TimeSpan.FromSeconds(15);
        var deadline = DateTime.UtcNow + timeout;
        while (TestJobHandler.ExecutionCount == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        TestJobHandler.ExecutionCount.ShouldBeGreaterThan(0, "Job handler was never invoked — ExecuteJobAsync likely passed the wrong type to BackgroundJobExecuter.");
        var processed = TestJobHandler.ProcessedJobs.First();
        processed.Message.ShouldBe("E2E Test");
        processed.Value.ShouldBe(999);
    }

    /// <summary>
    /// invora-backend#312 — a DELAYED enqueue must actually execute.
    ///
    /// <para><c>Should_Enqueue_Delayed_Job</c> above asserts only that a job id came back, which
    /// is true even when the message is thrown away: <c>EnqueueDelayedAsync</c> publishes to a
    /// topic distinct from the immediate one, nothing ever subscribed to it, and Pub/Sub
    /// DISCARDS messages published to a topic with zero subscriptions. So the delayed path was a
    /// silent no-op on every deployment of this package while its test stayed green — the id it
    /// asserted on is minted client-side before the publish and says nothing about delivery.</para>
    ///
    /// <para>This test closes that gap end to end: start processing, enqueue with a delay, and
    /// require the job BODY to have run. The second assertion is the discrimination control —
    /// the recorded execution time must be at or after the scheduled time, which distinguishes
    /// "the delayed pipeline delivered it when due" from "it leaked onto the immediate topic and
    /// ran at once". Without it, a regression that routed delayed jobs straight to the immediate
    /// queue would still pass.</para>
    /// </summary>
    [Fact]
    public async Task Should_Execute_Delayed_Job_EndToEnd()
    {
        // Arrange
        var jobManager = _scope!.ServiceProvider.GetRequiredService<IPubSubBackgroundJobManager>();
        DelayedE2EJobHandler.Reset();

        var delay = TimeSpan.FromSeconds(3);
        var args = new DelayedE2EJobArgs { Payload = "Delayed E2E" };

        // Act — subscribe first, then enqueue with a delay.
        await jobManager.StartProcessingAsync<DelayedE2EJobArgs>();

        var enqueuedAt = DateTime.UtcNow;
        var jobId = await jobManager.EnqueueAsync(args, delay: delay);
        var notBefore = enqueuedAt + delay;

        jobId.ShouldNotBeNullOrEmpty();

        // Assert — wait for the handler to run. The budget covers the delay itself plus the
        // subscription's redelivery backoff (PubSubTestModule shortens it for tests).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (DelayedE2EJobHandler.ProcessedJobs.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        DelayedE2EJobHandler.ProcessedJobs.ShouldNotBeEmpty(
            "a delayed enqueue must actually execute -- if this is empty the delayed topic has "
                + "no subscription and Pub/Sub discarded the message (invora-backend#312).");

        var (processedArgs, processedAt) = DelayedE2EJobHandler.ProcessedJobs.First();
        processedArgs.Payload.ShouldBe("Delayed E2E");

        // CONTROL: proves the message travelled the DELAYED path, not the immediate one.
        processedAt.ShouldBeGreaterThanOrEqualTo(notBefore,
            "a delayed job must not run before it is due -- running early means it reached the "
                + "immediate queue and the delay was ignored.");
    }

    /// <summary>
    /// A failure starting the DELAYED consumer must degrade, not propagate.
    ///
    /// <para>Consumers call <c>StartProcessingAsync</c> from their module's
    /// <c>OnApplicationInitializationAsync</c>, so an exception escaping it aborts ABP startup for
    /// the WHOLE APPLICATION — not merely one job type. That exposure is real and lands on
    /// upgrade: every existing consumer creates its <c>.Delayed</c> subscriptions for the first
    /// time on taking this version, and <c>CreateSubscriptionIfNotExistsAsync</c> catches only
    /// <c>NotFound</c>, so e.g. a <c>PermissionDenied</c> on those brand-new topics would escape.
    /// Trading a whole-application startup failure for one job type losing its delayed capability
    /// is clearly the right way round, and this pins it.</para>
    ///
    /// <para><c>DegradedDelayedJobArgs</c> is configured with an out-of-range delayed backoff
    /// (<c>PubSubTestModule</c>), which is a deterministic way to make exactly that half fail.
    /// The second assertion is the one that matters: it is not enough that nothing threw — the
    /// IMMEDIATE path must still actually deliver, or "degraded" would just mean "broken".</para>
    /// </summary>
    [Fact]
    public async Task StartProcessingAsync_WhenTheDelayedConsumerFailsToStart_DegradesInsteadOfThrowing()
    {
        var jobManager = _scope!.ServiceProvider.GetRequiredService<IPubSubBackgroundJobManager>();
        DegradedDelayedJobHandler.Reset();

        await Should.NotThrowAsync(
            () => jobManager.StartProcessingAsync<DegradedDelayedJobArgs>());

        await jobManager.EnqueueAsync(new DegradedDelayedJobArgs { Payload = "immediate-survives" });

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DegradedDelayedJobHandler.ProcessedJobs.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        DegradedDelayedJobHandler.ProcessedJobs.ShouldNotBeEmpty(
            "the immediate consumer must still deliver after the delayed one failed to start -- "
                + "otherwise the catch is not degrading, it is hiding a total failure.");
        DegradedDelayedJobHandler.ProcessedJobs.First().Payload.ShouldBe("immediate-survives");
    }

    [Fact]
    public void ConnectionPool_Should_Throw_For_Unknown_Connection()
    {
        // Arrange
        var connectionPool = _scope!.ServiceProvider.GetRequiredService<IPubSubConnectionPool>();

        // Act & Assert
        Should.Throw<AbpException>(() => connectionPool.GetConnection("NonExistent"));
    }
}

[CollectionDefinition("PubSubEmulator")]
public class PubSubEmulatorCollection : ICollectionFixture<PubSubEmulatorFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
