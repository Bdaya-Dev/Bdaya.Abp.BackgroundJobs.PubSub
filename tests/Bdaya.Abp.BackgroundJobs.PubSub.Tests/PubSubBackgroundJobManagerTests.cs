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
public class PubSubBackgroundJobManagerTests : IClassFixture<PubSubEmulatorFixture>, IAsyncLifetime
{
    private readonly PubSubEmulatorFixture _fixture;
    private IAbpApplicationWithInternalServiceProvider? _application;
    private IServiceScope? _scope;

    public PubSubBackgroundJobManagerTests(PubSubEmulatorFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
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

    public async Task DisposeAsync()
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
