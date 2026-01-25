using System.Collections.Concurrent;
using Google.Cloud.PubSub.V1;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Bdaya.Abp.BackgroundJobs.PubSub;

/// <summary>
/// Interface for managing Pub/Sub publisher and subscriber client instances.
/// Provides connection pooling for efficient resource usage.
/// </summary>
public interface IPubSubConnectionPool : IDisposable
{
    /// <summary>
    /// Gets a publisher client for the specified connection.
    /// </summary>
    Task<PublisherServiceApiClient> GetPublisherAsync(string? connectionName = null);

    /// <summary>
    /// Gets a subscriber client for the specified connection.
    /// </summary>
    Task<SubscriberServiceApiClient> GetSubscriberAsync(string? connectionName = null);

    /// <summary>
    /// Gets the connection configuration for the specified connection name.
    /// </summary>
    PubSubConnectionConfiguration GetConnection(string? connectionName = null);
}

/// <summary>
/// Connection pool implementation for Google Cloud Pub/Sub clients.
/// Manages and reuses client instances for better performance.
/// </summary>
public class PubSubConnectionPool : IPubSubConnectionPool, ISingletonDependency
{
    private readonly AbpPubSubOptions _options;
    private readonly ConcurrentDictionary<string, PublisherServiceApiClient> _publishers = new();
    private readonly ConcurrentDictionary<string, SubscriberServiceApiClient> _subscribers = new();
    private bool _disposed;

    public PubSubConnectionPool(IOptions<AbpPubSubOptions> options)
    {
        _options = options.Value;
    }

    public PubSubConnectionConfiguration GetConnection(string? connectionName = null)
    {
        connectionName ??= "Default";

        if (!_options.Connections.TryGetValue(connectionName, out var connection))
        {
            throw new AbpException($"Pub/Sub connection '{connectionName}' not found. Configure it in AbpPubSubOptions.Connections.");
        }

        return connection;
    }

    public async Task<PublisherServiceApiClient> GetPublisherAsync(string? connectionName = null)
    {
        connectionName ??= "Default";

        if (_publishers.TryGetValue(connectionName, out var existingClient))
        {
            return existingClient;
        }

        var connection = GetConnection(connectionName);
        var client = await CreatePublisherClientAsync(connection);

        return _publishers.GetOrAdd(connectionName, client);
    }

    public async Task<SubscriberServiceApiClient> GetSubscriberAsync(string? connectionName = null)
    {
        connectionName ??= "Default";

        if (_subscribers.TryGetValue(connectionName, out var existingClient))
        {
            return existingClient;
        }

        var connection = GetConnection(connectionName);
        var client = await CreateSubscriberClientAsync(connection);

        return _subscribers.GetOrAdd(connectionName, client);
    }

    private async Task<PublisherServiceApiClient> CreatePublisherClientAsync(PubSubConnectionConfiguration connection)
    {
        var builder = new PublisherServiceApiClientBuilder();

        if (!string.IsNullOrEmpty(connection.EmulatorHost))
        {
            // Use emulator for local development
            builder.Endpoint = connection.EmulatorHost;
            builder.ChannelCredentials = ChannelCredentials.Insecure;
        }
        else if (!string.IsNullOrEmpty(connection.CredentialsPath))
        {
            // Use service account credentials file
            builder.CredentialsPath = connection.CredentialsPath;
        }
        // else: Use Application Default Credentials (ADC)

        return await builder.BuildAsync();
    }

    private async Task<SubscriberServiceApiClient> CreateSubscriberClientAsync(PubSubConnectionConfiguration connection)
    {
        var builder = new SubscriberServiceApiClientBuilder();

        if (!string.IsNullOrEmpty(connection.EmulatorHost))
        {
            // Use emulator for local development
            builder.Endpoint = connection.EmulatorHost;
            builder.ChannelCredentials = ChannelCredentials.Insecure;
        }
        else if (!string.IsNullOrEmpty(connection.CredentialsPath))
        {
            // Use service account credentials file
            builder.CredentialsPath = connection.CredentialsPath;
        }
        // else: Use Application Default Credentials (ADC)

        return await builder.BuildAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _publishers.Clear();
        _subscribers.Clear();
    }
}
