namespace Bdaya.Abp.BackgroundJobs.PubSub;

/// <summary>
/// Configuration options for the Pub/Sub background job manager.
/// Similar to AbpRabbitMqBackgroundJobOptions in ABP.
/// </summary>
public class AbpPubSubBackgroundJobOptions
{
    /// <summary>
    /// The name of the connection to use from AbpPubSubOptions.Connections.
    /// If not set, uses "Default".
    /// </summary>
    public string? ConnectionName { get; set; }

    /// <summary>
    /// Default prefix for job topic names.
    /// Default: "AbpBackgroundJobs"
    /// </summary>
    public string DefaultTopicPrefix { get; set; } = "AbpBackgroundJobs";

    /// <summary>
    /// Default prefix for job subscription names.
    /// Default: "AbpBackgroundJobs"
    /// </summary>
    public string DefaultSubscriptionPrefix { get; set; } = "AbpBackgroundJobs";

    /// <summary>
    /// Default prefix for delayed job topic names.
    /// Default: "AbpBackgroundJobs.Delayed"
    /// </summary>
    public string DefaultDelayedTopicPrefix { get; set; } = "AbpBackgroundJobs.Delayed";

    /// <summary>
    /// Default prefix for delayed job subscription names.
    /// Default: "AbpBackgroundJobs.Delayed"
    /// </summary>
    public string DefaultDelayedSubscriptionPrefix { get; set; } = "AbpBackgroundJobs.Delayed";

    /// <summary>
    /// Maximum concurrent handlers for all job queues (flow control).
    /// Default: 1 (process one job at a time).
    /// </summary>
    public int PrefetchCount { get; set; } = 1;

    /// <summary>
    /// Default acknowledgment deadline in seconds.
    /// Default: 60 seconds.
    /// </summary>
    public int AckDeadlineSeconds { get; set; } = 60;

    /// <summary>
    /// Default message retention duration in days.
    /// Default: 7 days.
    /// </summary>
    public int MessageRetentionDays { get; set; } = 7;

    /// <summary>
    /// Maximum delivery attempts before considering a job as failed.
    /// Default: 5.
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>
    /// Whether to automatically create topics if they don't exist.
    /// Default: true.
    /// </summary>
    public bool AutoCreateTopics { get; set; } = true;

    /// <summary>
    /// Whether to automatically create subscriptions if they don't exist.
    /// Default: true.
    /// </summary>
    public bool AutoCreateSubscriptions { get; set; } = true;

    /// <summary>
    /// Dead letter topic suffix for failed jobs.
    /// If set, failed jobs will be moved to {TopicName}.{DeadLetterTopicSuffix}.
    /// </summary>
    public string? DeadLetterTopicSuffix { get; set; } = "DeadLetter";

    /// <summary>
    /// Dictionary of job-specific queue configurations.
    /// Key is the job args type.
    /// </summary>
    public Dictionary<Type, JobQueueConfiguration> JobQueues { get; } = new();

    /// <summary>
    /// Gets or creates a queue configuration for the specified job args type.
    /// </summary>
    public JobQueueConfiguration GetOrCreateJobQueue<TArgs>()
    {
        return GetOrCreateJobQueue(typeof(TArgs));
    }

    /// <summary>
    /// Gets or creates a queue configuration for the specified job args type.
    /// </summary>
    public JobQueueConfiguration GetOrCreateJobQueue(Type argsType)
    {
        if (JobQueues.TryGetValue(argsType, out var config))
        {
            return config;
        }

        var jobName = GetJobName(argsType);
        var topicName = $"{DefaultTopicPrefix}.{jobName}";
        var subscriptionName = $"{DefaultSubscriptionPrefix}.{jobName}";
        var delayedTopicName = $"{DefaultDelayedTopicPrefix}.{jobName}";
        var delayedSubscriptionName = $"{DefaultDelayedSubscriptionPrefix}.{jobName}";

        config = new JobQueueConfiguration(
            argsType,
            topicName,
            subscriptionName,
            delayedTopicName,
            delayedSubscriptionName,
            ConnectionName ?? "Default",
            AckDeadlineSeconds,
            TimeSpan.FromDays(MessageRetentionDays),
            MaxDeliveryAttempts,
            PrefetchCount);

        JobQueues[argsType] = config;
        return config;
    }

    private static string GetJobName(Type argsType)
    {
        // Check for BackgroundJobName attribute
        var attribute = argsType
            .GetCustomAttributes(typeof(BackgroundJobNameAttribute), true)
            .FirstOrDefault() as BackgroundJobNameAttribute;

        if (attribute != null)
        {
            return attribute.Name;
        }

        // Use type name, replacing dots with underscores for Pub/Sub compatibility
        return argsType.FullName?.Replace(".", "_").Replace("+", "_") ?? argsType.Name;
    }
}

/// <summary>
/// Attribute to specify a custom name for a background job.
/// Similar to RabbitMQ's BackgroundJobName attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class BackgroundJobNameAttribute : Attribute
{
    public string Name { get; }

    public BackgroundJobNameAttribute(string name)
    {
        Name = name;
    }
}
