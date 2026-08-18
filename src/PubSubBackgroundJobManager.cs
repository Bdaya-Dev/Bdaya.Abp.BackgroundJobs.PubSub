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
[ExposeServices(
    typeof(IBackgroundJobManager),
    typeof(IPubSubBackgroundJobManager),
    typeof(PubSubBackgroundJobManager)
)]
public class PubSubBackgroundJobManager : IPubSubBackgroundJobManager, ISingletonDependency
{
    protected AbpPubSubBackgroundJobOptions Options { get; }
    protected AbpBackgroundJobOptions BackgroundJobOptions { get; }
    protected IPubSubConnectionPool ConnectionPool { get; }
    protected IPubSubJobSerializer Serializer { get; }
    protected IServiceScopeFactory ServiceScopeFactory { get; }
    protected ILogger<PubSubBackgroundJobManager> Logger { get; }

    protected ConcurrentDictionary<System.Type, SubscriberClient> Subscribers { get; } = new();

    /// <summary>
    /// Subscribers consuming each job type's DELAYED topic. Kept separate from
    /// <see cref="Subscribers"/> rather than sharing its key space, which would make one
    /// overwrite the other and leave a live client with nothing stopping it.
    /// </summary>
    protected ConcurrentDictionary<System.Type, SubscriberClient> DelayedSubscribers { get; } =
        new();

    protected ConcurrentDictionary<System.Type, TopicName> Topics { get; } = new();

    private bool _initialized;
    private readonly object _initLock = new();

    public PubSubBackgroundJobManager(
        IOptions<AbpPubSubBackgroundJobOptions> options,
        IOptions<AbpBackgroundJobOptions> backgroundJobOptions,
        IPubSubConnectionPool connectionPool,
        IPubSubJobSerializer serializer,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<PubSubBackgroundJobManager> logger
    )
    {
        Options = options.Value;
        BackgroundJobOptions = backgroundJobOptions.Value;
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

    public virtual async Task<string> EnqueueAsync<TArgs>(
        TArgs args,
        BackgroundJobPriority priority = BackgroundJobPriority.Normal,
        TimeSpan? delay = null
    )
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
        TimeSpan delay
    )
    {
        var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);

        // For delayed jobs, we use message attributes to store the scheduled time
        // The subscriber will check if the job is ready to execute
        var delayedTopicName = await EnsureDelayedTopicExistsAsync(argsType, queueConfig);
        var scheduledTime = DateTime.UtcNow.Add(delay);

        var builder = new PublisherClientBuilder { TopicName = delayedTopicName };

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
                    ["JobArgsType"] =
                        argsType.AssemblyQualifiedName ?? argsType.FullName ?? argsType.Name,
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
                scheduledTime
            );

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
        BackgroundJobPriority priority
    )
    {
        var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);

        var builder = new PublisherClientBuilder { TopicName = topicName };

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
                    ["JobArgsType"] =
                        argsType.AssemblyQualifiedName ?? argsType.FullName ?? argsType.Name,
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
                topicName.ToString()
            );

            return messageId;
        }
        finally
        {
            await publisherClient.ShutdownAsync(TimeSpan.FromSeconds(10));
        }
    }

    protected virtual async Task<TopicName> EnsureTopicExistsAsync(
        System.Type argsType,
        JobQueueConfiguration queueConfig
    )
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

    protected virtual async Task<TopicName> EnsureDelayedTopicExistsAsync(
        System.Type argsType,
        JobQueueConfiguration queueConfig
    )
    {
        if (string.IsNullOrEmpty(queueConfig.DelayedTopicName))
        {
            throw new AbpException(
                $"Delayed topic not configured for job System.Type {argsType.Name}"
            );
        }

        var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);
        var topicName = TopicName.FromProjectTopic(
            connection.ProjectId,
            queueConfig.DelayedTopicName
        );

        if (Options.AutoCreateTopics)
        {
            await CreateTopicIfNotExistsAsync(topicName, queueConfig.ConnectionName);
        }

        return topicName;
    }

    protected virtual async Task CreateTopicIfNotExistsAsync(
        TopicName topicName,
        string connectionName
    )
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
        TopicName topicName
    )
    {
        var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);
        var subscriptionName = SubscriptionName.FromProjectSubscription(
            connection.ProjectId,
            queueConfig.SubscriptionName
        );

        if (Options.AutoCreateSubscriptions)
        {
            await CreateSubscriptionIfNotExistsAsync(subscriptionName, topicName, queueConfig);
        }

        return subscriptionName;
    }

    protected virtual async Task CreateSubscriptionIfNotExistsAsync(
        SubscriptionName subscriptionName,
        TopicName topicName,
        JobQueueConfiguration queueConfig,
        bool isDelayed = false
    )
    {
        try
        {
            var subscriberClient = await ConnectionPool.GetSubscriberAsync(
                queueConfig.ConnectionName
            );
            await subscriberClient.GetSubscriptionAsync(subscriptionName);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            var subscriberClient = await ConnectionPool.GetSubscriberAsync(
                queueConfig.ConnectionName
            );
            var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);

            var request = new Subscription
            {
                SubscriptionName = subscriptionName,
                TopicAsTopicName = topicName,
                AckDeadlineSeconds = queueConfig.AckDeadlineSeconds,
                MessageRetentionDuration = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(
                    queueConfig.MessageRetentionDuration
                )
            };

            if (isDelayed)
            {
                // A not-yet-due delayed message is deliberately NACKed by
                // ProcessJobMessageAsync so Pub/Sub redelivers it later. Every one of
                // those NACKs burns a DELIVERY ATTEMPT, so attaching a DeadLetterPolicy
                // keyed on MaxDeliveryAttempts here would dead-letter jobs for the sole
                // reason that they are not due yet — silently destroying exactly the work
                // this subscription exists to deliver. Hence: no dead-letter policy on the
                // delayed subscription.
                //
                // ⚠️ THE COST OF THAT CHOICE, stated so it is not discovered the hard way:
                // no policy also means no attempt cap here. A delayed job whose BODY keeps
                // throwing a non-AbpException is redelivered until MessageRetentionDuration
                // expires (7 days by default) and is then dropped with NO dead-letter
                // record — where the immediate subscription would have preserved it after
                // MaxDeliveryAttempts. An empty `.DeadLetter` topic is therefore NOT
                // evidence that no delayed jobs were lost. Same reason, same silence: a
                // `delay` longer than MessageRetentionDuration expires before it comes due,
                // so the job never runs at all.
                //
                // A RetryPolicy instead turns those NACKs into an exponential-backoff
                // ladder rather than a redelivery hot loop (without it, a job delayed by
                // minutes would be redelivered and re-NACKed continuously for its whole
                // wait). Exponential means the interval CLIMBS from
                // DelayedRetryMinimumBackoff toward DelayedRetryMaximumBackoff, so it is the
                // MAXIMUM that bounds how late a job can fire — see those two properties.
                request.RetryPolicy = BuildDelayedRetryPolicy(queueConfig);
            }
            // Configure dead letter if enabled
            else if (
                !string.IsNullOrEmpty(Options.DeadLetterTopicSuffix)
                && queueConfig.MaxDeliveryAttempts.HasValue
            )
            {
                var deadLetterTopicName = TopicName.FromProjectTopic(
                    connection.ProjectId,
                    $"{queueConfig.TopicName}.{Options.DeadLetterTopicSuffix}"
                );

                await CreateTopicIfNotExistsAsync(deadLetterTopicName, queueConfig.ConnectionName);

                request.DeadLetterPolicy = new DeadLetterPolicy
                {
                    DeadLetterTopic = deadLetterTopicName.ToString(),
                    MaxDeliveryAttempts = queueConfig.MaxDeliveryAttempts.Value
                };
            }

            await subscriberClient.CreateSubscriptionAsync(request);
            Logger.LogInformation(
                "Created Pub/Sub subscription: {SubscriptionName}",
                subscriptionName.ToString()
            );
        }
    }

    /// <summary>
    /// Pub/Sub's own bound on a subscription retry policy's backoff values.
    /// </summary>
    private static readonly TimeSpan MaxRetryBackoff = TimeSpan.FromSeconds(600);

    /// <summary>
    /// Builds the delayed subscription's retry policy, validating the configured backoffs first.
    ///
    /// <para>Pub/Sub rejects out-of-range values at CreateSubscription time with a generic
    /// <c>INVALID_ARGUMENT</c> that names neither the offending knob nor the job type. Checking
    /// here turns a misconfiguration into a message that says which setting, on which queue, and
    /// what the bound is.</para>
    /// </summary>
    public static RetryPolicy BuildDelayedRetryPolicy(JobQueueConfiguration queueConfig)
    {
        var min = queueConfig.DelayedRetryMinimumBackoff;
        var max = queueConfig.DelayedRetryMaximumBackoff;

        static void Check(string name, TimeSpan value, JobQueueConfiguration cfg)
        {
            if (value < TimeSpan.Zero || value > MaxRetryBackoff)
            {
                throw new AbpException(
                    $"{name} for job queue '{cfg.JobArgsType.Name}' is {value}, outside the range "
                        + $"Pub/Sub accepts for a subscription retry policy (0s to {MaxRetryBackoff.TotalSeconds}s)."
                );
            }
        }

        Check(nameof(JobQueueConfiguration.DelayedRetryMinimumBackoff), min, queueConfig);
        Check(nameof(JobQueueConfiguration.DelayedRetryMaximumBackoff), max, queueConfig);

        if (max < min)
        {
            throw new AbpException(
                $"{nameof(JobQueueConfiguration.DelayedRetryMaximumBackoff)} ({max}) is less than "
                    + $"{nameof(JobQueueConfiguration.DelayedRetryMinimumBackoff)} ({min}) for job queue "
                    + $"'{queueConfig.JobArgsType.Name}'."
            );
        }

        return new RetryPolicy
        {
            MinimumBackoff = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(min),
            MaximumBackoff = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(max)
        };
    }

    /// <summary>
    /// Resolves (and, when <see cref="AbpPubSubBackgroundJobOptions.AutoCreateSubscriptions"/>
    /// is on, creates) the subscription that consumes the DELAYED topic for this job type.
    /// </summary>
    protected virtual async Task<SubscriptionName> EnsureDelayedSubscriptionExistsAsync(
        JobQueueConfiguration queueConfig,
        TopicName delayedTopicName
    )
    {
        var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);
        var subscriptionName = SubscriptionName.FromProjectSubscription(
            connection.ProjectId,
            queueConfig.DelayedSubscriptionName!
        );

        if (Options.AutoCreateSubscriptions)
        {
            await CreateSubscriptionIfNotExistsAsync(
                subscriptionName,
                delayedTopicName,
                queueConfig,
                isDelayed: true
            );
        }

        return subscriptionName;
    }

    /// <summary>
    /// Starts listening for jobs of the specified System.Type, on BOTH the immediate and the
    /// delayed topic.
    ///
    /// <para>The delayed half was missing entirely (invora-backend#312). <c>EnqueueAsync</c>
    /// with a non-zero <c>delay</c> publishes to <see cref="JobQueueConfiguration.DelayedTopicName"/>,
    /// a topic distinct from the immediate one, but nothing ever created or consumed a
    /// subscription against it — and Google Pub/Sub DISCARDS messages published to a topic with
    /// zero subscriptions. So every delayed re-enqueue on every deployment of this package was a
    /// silent no-op: the publish succeeded, an id came back, and the job simply never ran. It was
    /// measured in production on <c>inpro-invora</c> (the delayed topic existed — Pub/Sub
    /// auto-creates on first publish, so its existence proves the path was taken — while not one
    /// of the 17 subscriptions matched it), where it meant <c>WebhookDeliveryJob</c>'s entire
    /// retry ladder never ran.</para>
    /// </summary>
    public virtual async Task StartProcessingAsync<TArgs>()
        where TArgs : class
    {
        var argsType = typeof(TArgs);
        var queueConfig = Options.GetOrCreateJobQueue<TArgs>();
        var connection = ConnectionPool.GetConnection(queueConfig.ConnectionName);

        var topicName = await EnsureTopicExistsAsync(argsType, queueConfig);
        var subscriptionName = await EnsureSubscriptionExistsAsync(
            argsType,
            queueConfig,
            topicName
        );

        Subscribers[argsType] = await StartSubscriberAsync<TArgs>(
            subscriptionName,
            queueConfig,
            connection
        );

        Logger.LogInformation(
            "Started processing jobs for {JobType} from subscription {SubscriptionName}",
            argsType.Name,
            subscriptionName.ToString()
        );

        await StartProcessingDelayedAsync<TArgs>(argsType, queueConfig, connection);
    }

    /// <summary>
    /// Starts the subscriber for this job type's DELAYED topic. Messages there carry a
    /// <c>ScheduledTime</c> attribute and are NACKed by <see cref="ProcessJobMessageAsync{TArgs}"/>
    /// until they come due; the subscription's retry policy (see
    /// <see cref="CreateSubscriptionIfNotExistsAsync"/>) turns those NACKs into a backoff ladder.
    /// </summary>
    ///
    /// <remarks>
    /// <b>This method never throws — it degrades loudly instead, and that is deliberate.</b>
    ///
    /// <para>Before delayed consumption existed, <see cref="StartProcessingAsync{TArgs}"/> could
    /// only fail on the immediate path. Adding this step introduced a NEW way for it to throw,
    /// and consumers call it from their module's <c>OnApplicationInitializationAsync</c> — so an
    /// exception here would abort ABP startup for the WHOLE APPLICATION, not merely leave one job
    /// type degraded. The exposure is real and lands precisely on upgrade: because
    /// <c>GetOrCreateJobQueue</c> always populates the delayed names, every existing consumer will
    /// try to CREATE its <c>.Delayed</c> subscriptions for the first time the moment it takes this
    /// version, and <see cref="CreateSubscriptionIfNotExistsAsync"/> only catches
    /// <c>NotFound</c> — a <c>PermissionDenied</c> from a service account with no rights on those
    /// brand-new topics would propagate straight out.</para>
    ///
    /// <para>Trading a whole-application startup failure for one job type losing its delayed
    /// capability is clearly the right way round. And degrading here is NOT the silent-no-op
    /// failure this whole change exists to remove: that defect produced no signal at all, whereas
    /// this logs at ERROR, names the job type, and states in the message that delayed jobs will
    /// not run.</para>
    /// </remarks>
    protected virtual async Task StartProcessingDelayedAsync<TArgs>(
        System.Type argsType,
        JobQueueConfiguration queueConfig,
        PubSubConnectionConfiguration connection
    )
        where TArgs : class
    {
        if (
            string.IsNullOrEmpty(queueConfig.DelayedTopicName)
            || string.IsNullOrEmpty(queueConfig.DelayedSubscriptionName)
        )
        {
            // Nothing is silently dropped in this state: EnqueueDelayedAsync throws when
            // DelayedTopicName is unset, so a delayed enqueue fails loudly at publish time
            // rather than vanishing. Log it anyway so the capability gap is visible rather
            // than inferred from a job that never runs.
            Logger.LogWarning(
                "No delayed topic/subscription is configured for {JobType}; "
                    + "EnqueueAsync(..., delay: ...) is not supported for it.",
                argsType.Name
            );
            return;
        }

        try
        {
            var delayedTopicName = await EnsureDelayedTopicExistsAsync(argsType, queueConfig);
            var delayedSubscriptionName = await EnsureDelayedSubscriptionExistsAsync(
                queueConfig,
                delayedTopicName
            );

            DelayedSubscribers[argsType] = await StartSubscriberAsync<TArgs>(
                delayedSubscriptionName,
                queueConfig,
                connection
            );

            Logger.LogInformation(
                "Started processing DELAYED jobs for {JobType} from subscription {SubscriptionName}",
                argsType.Name,
                delayedSubscriptionName.ToString()
            );
        }
        catch (Exception ex)
        {
            // See the remarks above for why this is caught rather than propagated. LogError,
            // not LogWarning: a job type that accepts a delayed enqueue and never runs it is
            // an error state, and the whole point of this change is that such a state must
            // never again be reachable without a signal.
            Logger.LogError(
                ex,
                "Failed to start the DELAYED job consumer for {JobType} (topic {DelayedTopic}, "
                    + "subscription {DelayedSubscription}). Immediate jobs for this type are "
                    + "unaffected and still running, but EnqueueAsync(..., delay: ...) for it will "
                    + "NOT execute until this is resolved.",
                argsType.Name,
                queueConfig.DelayedTopicName,
                queueConfig.DelayedSubscriptionName
            );
        }
    }

    /// <summary>
    /// Builds a <see cref="SubscriberClient"/> for <paramref name="subscriptionName"/> and starts
    /// pumping its messages through <see cref="ProcessJobMessageAsync{TArgs}"/>. Shared by the
    /// immediate and delayed paths so the two cannot drift apart in flow-control, credential, or
    /// handler wiring.
    /// </summary>
    protected virtual async Task<SubscriberClient> StartSubscriberAsync<TArgs>(
        SubscriptionName subscriptionName,
        JobQueueConfiguration queueConfig,
        PubSubConnectionConfiguration connection
    )
        where TArgs : class
    {
        var builder = new SubscriberClientBuilder
        {
            SubscriptionName = subscriptionName,
            Settings = new SubscriberClient.Settings
            {
                FlowControlSettings = new Google.Api.Gax.FlowControlSettings(
                    maxOutstandingElementCount: queueConfig.PrefetchCount ?? Options.PrefetchCount,
                    maxOutstandingByteCount: null
                )
            }
        };

        ConfigureClientBuilder(builder, connection);

        var subscriberClient = await builder.BuildAsync();

        // Start processing messages in the background
        _ = subscriberClient.StartAsync(
            async (message, cancellationToken) =>
            {
                return await ProcessJobMessageAsync<TArgs>(message, queueConfig, cancellationToken);
            }
        );

        return subscriberClient;
    }

    protected virtual async Task<SubscriberClient.Reply> ProcessJobMessageAsync<TArgs>(
        PubsubMessage message,
        JobQueueConfiguration queueConfig,
        CancellationToken cancellationToken
    )
        where TArgs : class
    {
        try
        {
            // Check if this is a delayed job that's not ready yet
            if (message.Attributes.TryGetValue("ScheduledTime", out var scheduledTimeStr))
            {
                if (TryParseScheduledTime(scheduledTimeStr, out var scheduledTime))
                {
                    if (DateTimeOffset.UtcNow < scheduledTime)
                    {
                        // Job is not ready yet, nack to requeue
                        return SubscriberClient.Reply.Nack;
                    }
                }
                else
                {
                    // Fail OPEN — run it now rather than strand it forever (see
                    // IsDelayedJobDue) — but say so. Without this the fall-through is
                    // completely silent: a corrupted attribute quietly converts a delayed
                    // job into an immediate one, and on a self-re-enqueueing consumer such
                    // as WebhookDeliveryJob that is the collapsed retry ladder the
                    // interpret-don't-reject choice exists to avoid, with nothing left
                    // behind to diagnose it from.
                    Logger.LogWarning(
                        "Delayed job message {MessageId} for {JobType} carries an unparseable "
                            + "ScheduledTime attribute ('{ScheduledTime}'). Running it NOW rather "
                            + "than stranding it, but its delay is LOST — a job that re-enqueues "
                            + "itself with a backoff will retry without one until this is fixed.",
                        message.MessageId,
                        typeof(TArgs).Name,
                        scheduledTimeStr
                    );
                }
            }

            var argsType = typeof(TArgs);
            var jobArgs = Serializer.Deserialize<TArgs>(message.Data.ToByteArray());

            if (jobArgs == null)
            {
                Logger.LogError(
                    "Failed to deserialize job args for System.Type: {JobType}",
                    argsType.Name
                );
                return SubscriberClient.Reply.Ack; // Ack to prevent redelivery of invalid message
            }

            Logger.LogDebug(
                "Processing job. System.Type: {JobType}, MessageId: {MessageId}",
                argsType.Name,
                message.MessageId
            );

            // Execute the job
            await ExecuteJobAsync(argsType, jobArgs);

            return SubscriberClient.Reply.Ack;
        }
        catch (AbpException ex)
        {
            // Non-transient errors (e.g. job type not registered in DI, missing handler)
            // should be ACKed to prevent infinite retry loops — retrying won't fix them.
            Logger.LogError(
                ex,
                "Non-transient error processing job message, acknowledging to prevent retry loop. MessageId: {MessageId}",
                message.MessageId
            );
            return SubscriberClient.Reply.Ack;
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Error processing job message. MessageId: {MessageId}",
                message.MessageId
            );
            return SubscriberClient.Reply.Nack;
        }
    }

    /// <summary>
    /// Whether a delayed job carrying <paramref name="scheduledTimeAttribute"/> (the
    /// <c>ScheduledTime</c> message attribute written by <see cref="EnqueueDelayedAsync{TArgs}"/>)
    /// has come due as of <paramref name="now"/>.
    /// </summary>
    ///
    /// <remarks>
    /// Compares INSTANTS via <see cref="DateTimeOffset"/>. The previous implementation used
    /// <c>DateTime.TryParse</c> and compared the result against <c>DateTime.UtcNow</c>, which is
    /// wrong on any host whose local time is not UTC: <c>DateTime.TryParse</c> converts an
    /// offset-bearing timestamp to LOCAL time and returns <c>DateTimeKind.Local</c>, so the
    /// comparison silently added the host's UTC offset to every delay. Measured on a UTC+3 host:
    /// the attribute <c>2026-08-17T23:18:54Z</c> parsed to <c>2026-08-18T02:18:54+03:00</c> and
    /// the job was NACKed for a further three hours.
    ///
    /// <para>It survived because containers conventionally run UTC, where the offset is zero and
    /// the bug is invisible — the failure only appears off the machines CI runs on.</para>
    ///
    /// <para>An unparseable attribute is treated as DUE rather than never-due, so a malformed
    /// value cannot strand a job forever. That fall-through loses the delay, so it is NOT silent:
    /// <see cref="ProcessJobMessageAsync{TArgs}"/> logs a warning naming the message, the job type
    /// and the offending value before running it. This predicate itself is a pure function and
    /// cannot log — use <see cref="TryParseScheduledTime"/> if you need to tell "unparseable" from
    /// "not due yet", which is exactly the distinction that warning depends on.</para>
    /// </remarks>
    public static bool IsDelayedJobDue(string? scheduledTimeAttribute, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(scheduledTimeAttribute))
        {
            return true;
        }

        return TryParseScheduledTime(scheduledTimeAttribute, out var scheduledTime)
            ? now >= scheduledTime
            : true;
    }

    /// <summary>
    /// Parses a <c>ScheduledTime</c> message attribute into an absolute instant. Returns
    /// <c>false</c> when the value is absent or unreadable — which callers with a logger MUST
    /// distinguish from "not due yet", because the two lead to opposite actions and only one of
    /// them is worth telling an operator about.
    /// </summary>
    ///
    /// <remarks>
    /// Uses <c>AssumeUniversal | AdjustToUniversal</c>, NOT <c>RoundtripKind</c>. An OFFSET-LESS
    /// timestamp (no trailing <c>Z</c> and no <c>+hh:mm</c>) is the one shape that still
    /// reintroduced the exact host-dependence this parsing exists to remove: measured on a UTC+2
    /// host, <c>"2026-01-01T00:00:00.0000000"</c> parsed under <c>RoundtripKind</c> to
    /// <c>2026-01-01T00:00:00+02:00</c> — 22:00 UTC the previous day, two hours off, and a
    /// different instant on every machine. <c>AssumeUniversal</c> reads it as UTC, which is both
    /// deterministic and what the publisher means (<see cref="EnqueueDelayedAsync{TArgs}"/> writes
    /// <c>DateTime.UtcNow.ToString("O")</c>).
    ///
    /// <para>Interpreting an offset-less value rather than rejecting it is deliberate: rejecting
    /// would take the fail-open path and discard the delay entirely, turning a retry ladder into a
    /// hot loop. Assuming UTC preserves the delay and is wrong only for a producer that wrote LOCAL
    /// time with no offset — which nothing in this package does.</para>
    /// </remarks>
    public static bool TryParseScheduledTime(
        string? scheduledTimeAttribute,
        out DateTimeOffset scheduledTime
    )
    {
        scheduledTime = default;
        return !string.IsNullOrWhiteSpace(scheduledTimeAttribute)
            && DateTimeOffset.TryParse(
                scheduledTimeAttribute,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal
                    | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out scheduledTime
            );
    }

    protected virtual async Task ExecuteJobAsync(System.Type argsType, object args)
    {
        using var scope = ServiceScopeFactory.CreateScope();

        var jobExecuter = scope.ServiceProvider.GetRequiredService<IBackgroundJobExecuter>();

        // Look up the job handler type from ABP's options registry.
        // ABP's BackgroundJobExecuter resolves context.JobType from DI,
        // which must be the handler type (e.g. BackgroundEmailSendingJob),
        // not the args type (e.g. BackgroundEmailSendingJobArgs).
        var jobConfig = BackgroundJobOptions.GetJob(argsType);

        var context = new JobExecutionContext(scope.ServiceProvider, jobConfig.JobType, args);

        await jobExecuter.ExecuteAsync(context);
    }

    protected virtual void ConfigureClientBuilder<T>(
        T builder,
        PubSubConnectionConfiguration connection
    )
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

        // Both collections, or a delayed subscriber outlives the manager that started it.
        var stopTasks = Subscribers
            .Values.Concat(DelayedSubscribers.Values)
            .Select(async subscriber =>
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
        DelayedSubscribers.Clear();

        Logger.LogInformation("Pub/Sub Background Job Manager stopped.");
    }
}
