namespace Bdaya.Abp.BackgroundJobs.PubSub;

/// <summary>
/// Configuration for a specific job queue in Pub/Sub.
/// Similar to RabbitMQ's JobQueueConfiguration in ABP.
/// </summary>
public class JobQueueConfiguration
{
    /// <summary>
    /// The type of the job arguments.
    /// </summary>
    public Type JobArgsType { get; }

    /// <summary>
    /// The Pub/Sub topic name for this job queue.
    /// If not set, uses DefaultTopicPrefix + JobName.
    /// </summary>
    public string TopicName { get; set; }

    /// <summary>
    /// The Pub/Sub subscription name for this job queue.
    /// If not set, uses DefaultSubscriptionPrefix + JobName.
    /// </summary>
    public string SubscriptionName { get; set; }

    /// <summary>
    /// The Pub/Sub topic name for delayed job execution.
    /// If not set, uses DefaultDelayedTopicPrefix + JobName.
    /// </summary>
    public string? DelayedTopicName { get; set; }

    /// <summary>
    /// The Pub/Sub subscription name for delayed jobs.
    /// </summary>
    public string? DelayedSubscriptionName { get; set; }

    /// <summary>
    /// The name of the connection to use from AbpPubSubOptions.Connections.
    /// If not set, uses "Default".
    /// </summary>
    public string ConnectionName { get; set; } = "Default";

    /// <summary>
    /// The acknowledgment deadline in seconds.
    /// Default: 60 seconds.
    /// </summary>
    public int AckDeadlineSeconds { get; set; } = 60;

    /// <summary>
    /// Message retention duration for the subscription.
    /// Default: 7 days.
    /// </summary>
    public TimeSpan MessageRetentionDuration { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Maximum delivery attempts before moving to dead letter topic.
    /// If null, uses the default from AbpPubSubBackgroundJobOptions.
    /// </summary>
    public int? MaxDeliveryAttempts { get; set; }

    /// <summary>
    /// Maximum concurrent handlers for this job queue (flow control).
    /// If null, uses the default from AbpPubSubBackgroundJobOptions.
    /// </summary>
    public int? PrefetchCount { get; set; }

    public JobQueueConfiguration(
        Type jobArgsType,
        string topicName,
        string subscriptionName,
        string? delayedTopicName = null,
        string? delayedSubscriptionName = null,
        string connectionName = "Default",
        int ackDeadlineSeconds = 60,
        TimeSpan? messageRetentionDuration = null,
        int? maxDeliveryAttempts = null,
        int? prefetchCount = null)
    {
        JobArgsType = jobArgsType;
        TopicName = topicName;
        SubscriptionName = subscriptionName;
        DelayedTopicName = delayedTopicName;
        DelayedSubscriptionName = delayedSubscriptionName;
        ConnectionName = connectionName;
        AckDeadlineSeconds = ackDeadlineSeconds;
        MessageRetentionDuration = messageRetentionDuration ?? TimeSpan.FromDays(7);
        MaxDeliveryAttempts = maxDeliveryAttempts;
        PrefetchCount = prefetchCount;
    }
}
