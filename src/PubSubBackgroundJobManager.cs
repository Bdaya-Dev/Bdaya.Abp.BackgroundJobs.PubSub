using System.Collections.Concurrent;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;

namespace Bdaya.Abp.BackgroundJobs.PubSub;

/// <summary>
/// Google Cloud Pub/Sub implementation of the ABP background job manager.
/// Provides FIFO job processing using Pub/Sub messaging.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IBackgroundJobManager), typeof(IPubSubBackgroundJobManager), typeof(PubSubBackgroundJobManager))]
public class PubSubBackgroundJobManager : IPubSubBackgroundJobManager, ISingletonDependency
{
    protected AbpPubSubBackgroundJobOptions Options { get; }
    protected IPubSubConnectionPool ConnectionPool { get; }
    protected IPubSubJobSerializer Serializer { get; }
    protected IServiceScopeFactory ServiceScopeFactory { get; }
    protected ILogger<PubSubBackgroundJobManager> Logger { get; }

    protected ConcurrentDictionary<System.Type, SubscriberClient> Subscribers { get; } = new();
    protected ConcurrentDictionary<System.Type, TopicName> Topics { get; } = new();
    
    private bool _initialized;
    private readonly object _initLock = new();

    public PubSubBackgroundJobManager(
        IOptions<AbpPubSubBackgroundJobOptions> options,
        IPubSubConnectionPool connectionPool,
        IPubSubJobSerializer serializer,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<PubSubBackgroundJobManager> logger)
    {
        Options = options.Value;
        ConnectionPool = connectionPool;
        Serializer = serializer;
        ServiceScopeFactory = serviceScopeFactory;
        Logger = logger;
    }

    public virtual Task InitializeAsync()
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        lock (_initLock)
        {
            if (_initialized)
            {
                return Task.CompletedTask;
            }

            _initialized = true;
        }

        Logger.LogInformation("Initializing Pub/Sub Background Job Manager...");
        return Task.CompletedTask;
    }

    public virtual async Task<string> EnqueueAsync<TArgs>(TArgs args, BackgroundJobPriority priority = BackgroundJobPriority.Normal,
        TimeSpan? delay = null)
    {
        var argsType = typeof(TArgs);
        var queueConfig = Options.GetOrCreateJobQueue<TArgs>();

        // Ensure topic exists
        var topicName = await EnsureTopicExistsAsync(argsType, queueConfig);

        // Use delayed topic if delay is specified
        if (delay.HasValue && delay.Value > TimeSpan.Zero)
        {
            return await EnqueueDelayedAsync(args, argsType, queueConfig, delay.Value);
        }

        // Publish to immediate topic
        return await PublishJobAsync(topicName, args, argsType, queueConfig, priority);
    }

    protected virtual async Task<string> EnqueueDelayedAsync<TArgs>(
        TArgs args,
        System.Type argsType,
        JobQueueConfiguration queueConfig,
        TimeSpan delay)
    {
        var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);
        
        // For delayed jobs, we use message attributes to store the scheduled time
        // The subscriber will check if the job is ready to execute
        var delayedTopicName = await EnsureDelayedTopicExistsAsync(argsType, queueConfig);
        var scheduledTime = DateTime.UtcNow.Add(delay);

        var builder = new PublisherClientBuilder
        {
            TopicName = delayedTopicName
        };

        ConfigureClientBuilder(builder, connection);

        var publisherClient = await builder.BuildAsync();

        try
        {
            var body = Serializer.Serialize(args!);
            var messageId = Guid.NewGuid().ToString("N");

            var message = new PubsubMessage
            {
                Data = ByteString.CopyFrom(body),
                Attributes =
                {
                    ["JobArgsType"] = argsType.AssemblyQualifiedName ?? argsType.FullName ?? argsType.Name,
                    ["MessageId"] = messageId,
                    ["ScheduledTime"] = scheduledTime.ToString("O"),
                    ["Priority"] = BackgroundJobPriority.Normal.ToString()
                }
            };

            var publishedId = await publisherClient.PublishAsync(message);

            Logger.LogDebug(
                "Enqueued delayed job to Pub/Sub. System.Type: {JobType}, MessageId: {MessageId}, ScheduledTime: {ScheduledTime}",
                argsType.Name,
                publishedId,
                scheduledTime);

            return messageId;
        }
        finally
        {
            await publisherClient.ShutdownAsync(TimeSpan.FromSeconds(10));
        }
    }

    protected virtual async Task<string> PublishJobAsync<TArgs>(
        TopicName topicName,
        TArgs args,
        System.Type argsType,
        JobQueueConfiguration queueConfig,
        BackgroundJobPriority priority)
    {
        var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);

        var builder = new PublisherClientBuilder
        {
            TopicName = topicName
        };

        ConfigureClientBuilder(builder, connection);

        var publisherClient = await builder.BuildAsync();

        try
        {
            var body = Serializer.Serialize(args!);
            var messageId = Guid.NewGuid().ToString("N");

            var message = new PubsubMessage
            {
                Data = ByteString.CopyFrom(body),
                Attributes =
                {
                    ["JobArgsType"] = argsType.AssemblyQualifiedName ?? argsType.FullName ?? argsType.Name,
                    ["MessageId"] = messageId,
                    ["Priority"] = priority.ToString(),
                    ["EnqueuedAt"] = DateTime.UtcNow.ToString("O")
                }
            };

            var publishedId = await publisherClient.PublishAsync(message);

            Logger.LogDebug(
                "Enqueued job to Pub/Sub. System.Type: {JobType}, MessageId: {MessageId}, Topic: {Topic}",
                argsType.Name,
                publishedId,
                topicName.ToString());

            return messageId;
        }
        finally
        {
            await publisherClient.ShutdownAsync(TimeSpan.FromSeconds(10));
        }
    }

    protected virtual async Task<TopicName> EnsureTopicExistsAsync(System.Type argsType, JobQueueConfiguration queueConfig)
    {
        if (Topics.TryGetValue(argsType, out var existingTopic))
        {
            return existingTopic;
        }

        var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);
        var topicName = TopicName.FromProjectTopic(connection.ProjectId, queueConfig.TopicName);

        if (Options.AutoCreateTopics)
        {
            await CreateTopicIfNotExistsAsync(topicName, queueConfig.ConnectionName);
        }

        Topics[argsType] = topicName;
        return topicName;
    }

    protected virtual async Task<TopicName> EnsureDelayedTopicExistsAsync(System.Type argsType, JobQueueConfiguration queueConfig)
    {
        if (string.IsNullOrEmpty(queueConfig.DelayedTopicName))
        {
            throw new AbpException($"Delayed topic not configured for job System.Type {argsType.Name}");
        }

        var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);
        var topicName = TopicName.FromProjectTopic(connection.ProjectId, queueConfig.DelayedTopicName);

        if (Options.AutoCreateTopics)
        {
            await CreateTopicIfNotExistsAsync(topicName, queueConfig.ConnectionName);
        }

        return topicName;
    }

    protected virtual async Task CreateTopicIfNotExistsAsync(TopicName topicName, string connectionName)
    {
        try
        {
            var publisherClient = await ConnectionPool.GetPublisherAsync(connectionName);
            await publisherClient.GetTopicAsync(topicName);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            var publisherClient = await ConnectionPool.GetPublisherAsync(connectionName);
            await publisherClient.CreateTopicAsync(topicName);
            Logger.LogInformation("Created Pub/Sub topic: {TopicName}", topicName.ToString());
        }
    }

    protected virtual async Task<SubscriptionName> EnsureSubscriptionExistsAsync(
        System.Type argsType,
        JobQueueConfiguration queueConfig,
        TopicName topicName)
    {
        var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);
        var subscriptionName = SubscriptionName.FromProjectSubscription(
            connection.ProjectId,
            queueConfig.SubscriptionName);

        if (Options.AutoCreateSubscriptions)
        {
            await CreateSubscriptionIfNotExistsAsync(subscriptionName, topicName, queueConfig);
        }

        return subscriptionName;
    }

    protected virtual async Task CreateSubscriptionIfNotExistsAsync(
        SubscriptionName subscriptionName,
        TopicName topicName,
        JobQueueConfiguration queueConfig)
    {
        try
        {
            var subscriberClient = await ConnectionPool.GetSubscriberAsync(queueConfig.ConnectionName);
            await subscriberClient.GetSubscriptionAsync(subscriptionName);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            var subscriberClient = await ConnectionPool.GetSubscriberAsync(queueConfig.ConnectionName);
            var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);

            var request = new Subscription
            {
                SubscriptionName = subscriptionName,
                TopicAsTopicName = topicName,
                AckDeadlineSeconds = queueConfig.AckDeadlineSeconds,
                MessageRetentionDuration = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(queueConfig.MessageRetentionDuration)
            };

            // Configure dead letter if enabled
            if (!string.IsNullOrEmpty(Options.DeadLetterTopicSuffix) && queueConfig.MaxDeliveryAttempts.HasValue)
            {
                var deadLetterTopicName = TopicName.FromProjectTopic(
                    connection.ProjectId,
                    $"{queueConfig.TopicName}.{Options.DeadLetterTopicSuffix}");

                await CreateTopicIfNotExistsAsync(deadLetterTopicName, queueConfig.ConnectionName);

                request.DeadLetterPolicy = new DeadLetterPolicy
                {
                    DeadLetterTopic = deadLetterTopicName.ToString(),
                    MaxDeliveryAttempts = queueConfig.MaxDeliveryAttempts.Value
                };
            }

            await subscriberClient.CreateSubscriptionAsync(request);
            Logger.LogInformation("Created Pub/Sub subscription: {SubscriptionName}", subscriptionName.ToString());
        }
    }

    /// <summary>
    /// Starts listening for jobs of the specified System.Type.
    /// </summary>
    public virtual async Task StartProcessingAsync<TArgs>()
        where TArgs : class
    {
        var argsType = typeof(TArgs);
        var queueConfig = Options.GetOrCreateJobQueue<TArgs>();

        var topicName = await EnsureTopicExistsAsync(argsType, queueConfig);
        var subscriptionName = await EnsureSubscriptionExistsAsync(argsType, queueConfig, topicName);

        var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);

        var builder = new SubscriberClientBuilder
        {
            SubscriptionName = subscriptionName,
            Settings = new SubscriberClient.Settings
            {
                FlowControlSettings = new Google.Api.Gax.FlowControlSettings(
                    maxOutstandingElementCount: queueConfig.PrefetchCount ?? Options.PrefetchCount,
                    maxOutstandingByteCount: null)
            }
        };

        ConfigureClientBuilder(builder, connection);

        var subscriberClient = await builder.BuildAsync();
        Subscribers[argsType] = subscriberClient;

        // Start processing messages in the background
        _ = subscriberClient.StartAsync(async (message, cancellationToken) =>
        {
            return await ProcessJobMessageAsync<TArgs>(message, queueConfig, cancellationToken);
        });

        Logger.LogInformation(
            "Started processing jobs for {JobType} from subscription {SubscriptionName}",
            argsType.Name,
            subscriptionName.ToString());
    }

    protected virtual async Task<SubscriberClient.Reply> ProcessJobMessageAsync<TArgs>(
        PubsubMessage message,
        JobQueueConfiguration queueConfig,
        CancellationToken cancellationToken)
        where TArgs : class
    {
        try
        {
            // Check if this is a delayed job that's not ready yet
            if (message.Attributes.TryGetValue("ScheduledTime", out var scheduledTimeStr))
            {
                if (DateTime.TryParse(scheduledTimeStr, out var scheduledTime))
                {
                    if (DateTime.UtcNow < scheduledTime)
                    {
                        // Job is not ready yet, nack to requeue
                        return SubscriberClient.Reply.Nack;
                    }
                }
            }

            var argsType = typeof(TArgs);
            var jobArgs = Serializer.Deserialize<TArgs>(message.Data.ToByteArray());

            if (jobArgs == null)
            {
                Logger.LogWarning("Failed to deserialize job args for System.Type: {JobType}", argsType.Name);
                return SubscriberClient.Reply.Ack; // Ack to prevent redelivery of invalid message
            }

            Logger.LogDebug(
                "Processing job. System.Type: {JobType}, MessageId: {MessageId}",
                argsType.Name,
                message.MessageId);

            // Execute the job
            await ExecuteJobAsync(argsType, jobArgs);

            return SubscriberClient.Reply.Ack;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing job message. MessageId: {MessageId}", message.MessageId);
            return SubscriberClient.Reply.Nack;
        }
    }

    protected virtual async Task ExecuteJobAsync(System.Type argsType, object args)
    {
        using var scope = ServiceScopeFactory.CreateScope();

        var jobExecuterType = typeof(IBackgroundJobExecuter);
        var jobExecuter = scope.ServiceProvider.GetRequiredService(jobExecuterType) as IBackgroundJobExecuter;

        if (jobExecuter == null)
        {
            throw new AbpException($"Could not resolve {jobExecuterType.FullName}");
        }

        var context = new JobExecutionContext(
            scope.ServiceProvider,
            argsType,
            args);

        await jobExecuter.ExecuteAsync(context);
    }

    protected virtual void ConfigureClientBuilder<T>(T builder, PubSubConnectionConfiguration connection)
        where T : class
    {
        if (!string.IsNullOrEmpty(connection.EmulatorHost))
        {
            if (builder is PublisherClientBuilder publisherBuilder)
            {
                publisherBuilder.Endpoint = connection.EmulatorHost;
                publisherBuilder.ChannelCredentials = ChannelCredentials.Insecure;
            }
            else if (builder is SubscriberClientBuilder subscriberBuilder)
            {
                subscriberBuilder.Endpoint = connection.EmulatorHost;
                subscriberBuilder.ChannelCredentials = ChannelCredentials.Insecure;
            }
            else if (builder is PublisherServiceApiClientBuilder apiBuilder)
            {
                apiBuilder.Endpoint = connection.EmulatorHost;
                apiBuilder.ChannelCredentials = ChannelCredentials.Insecure;
            }
            else if (builder is SubscriberServiceApiClientBuilder subApiBuilder)
            {
                subApiBuilder.Endpoint = connection.EmulatorHost;
                subApiBuilder.ChannelCredentials = ChannelCredentials.Insecure;
            }
        }
        else
        {
            var credential = GetCredential(connection);
            if (credential == null)
            {
                return;
            }

            if (builder is PublisherClientBuilder publisherBuilder)
            {
                publisherBuilder.GoogleCredential = credential;
            }
            else if (builder is SubscriberClientBuilder subscriberBuilder)
            {
                subscriberBuilder.GoogleCredential = credential;
            }
            else if (builder is PublisherServiceApiClientBuilder apiBuilder)
            {
                apiBuilder.GoogleCredential = credential;
            }
            else if (builder is SubscriberServiceApiClientBuilder subApiBuilder)
            {
                subApiBuilder.GoogleCredential = credential;
            }
        }
    }

    private static GoogleCredential? GetCredential(PubSubConnectionConfiguration connection)
    {
        if (connection.Credential != null)
        {
            return connection.Credential;
        }

        if (!string.IsNullOrEmpty(connection.CredentialsJson))
        {
            return CredentialFactory.FromJson<GoogleCredential>(connection.CredentialsJson);
        }

        if (!string.IsNullOrEmpty(connection.CredentialsPath))
        {
            return CredentialFactory.FromFile<GoogleCredential>(connection.CredentialsPath);
        }

        return null;
    }

    public virtual async Task StopAsync()
    {
        Logger.LogInformation("Stopping Pub/Sub Background Job Manager...");

        var stopTasks = Subscribers.Values.Select(async subscriber =>
        {
            try
            {
                await subscriber.StopAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error stopping subscriber");
            }
        });

        await Task.WhenAll(stopTasks);
        Subscribers.Clear();

        Logger.LogInformation("Pub/Sub Background Job Manager stopped.");
    }
}
