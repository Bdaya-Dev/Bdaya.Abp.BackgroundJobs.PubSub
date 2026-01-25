using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Bdaya.Abp.BackgroundJobs.PubSub.Tests;

/// <summary>
/// Fixture for managing Pub/Sub emulator container for integration tests.
/// </summary>
public class PubSubEmulatorFixture : IAsyncLifetime
{
    private IContainer? _container;

    public string EmulatorHost => $"localhost:{Port}";
    public string ProjectId => "test-project";
    public int Port { get; private set; }

    public async Task InitializeAsync()
    {
        Port = Random.Shared.Next(10000, 60000);

        _container = new ContainerBuilder()
            .WithImage("gcr.io/google.com/cloudsdktool/cloud-sdk:emulators")
            .WithCommand("gcloud", "beta", "emulators", "pubsub", "start",
                $"--host-port=0.0.0.0:8085",
                $"--project={ProjectId}")
            .WithPortBinding(Port, 8085)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(8085))
            .Build();

        await _container.StartAsync();

        // Give the emulator a moment to fully initialize
        await Task.Delay(2000);
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }
}
