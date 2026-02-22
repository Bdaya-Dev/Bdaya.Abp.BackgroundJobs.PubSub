using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using System.Net.Sockets;

namespace Bdaya.Abp.BackgroundJobs.PubSub.Tests;

/// <summary>
/// Fixture for managing Pub/Sub emulator container for integration tests.
/// </summary>
public class PubSubEmulatorFixture : IAsyncLifetime
{
    private IContainer? _container;

    public string ProjectId => "test-project";
    public string EmulatorHost => $"localhost:{HostPort}";
    public int HostPort { get; private set; }

    public async ValueTask InitializeAsync()
    {
        _container = new ContainerBuilder("gcr.io/google.com/cloudsdktool/cloud-sdk:emulators")
            .WithCommand("gcloud", "beta", "emulators", "pubsub", "start",
                $"--host-port=0.0.0.0:8085",
                $"--project={ProjectId}")
            .WithPortBinding(8085, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server started"))
            .Build();

        await _container.StartAsync();

        HostPort = _container.GetMappedPublicPort(8085);
        await WaitForEmulatorAsync(TimeSpan.FromSeconds(30));
    }

    private async Task WaitForEmulatorAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("localhost", HostPort);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(100);
            }
        }

        throw new TimeoutException($"Emulator at localhost:{HostPort} did not become available within {timeout.TotalSeconds} seconds");
    }

    public async ValueTask DisposeAsync()
    {
        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }
}
