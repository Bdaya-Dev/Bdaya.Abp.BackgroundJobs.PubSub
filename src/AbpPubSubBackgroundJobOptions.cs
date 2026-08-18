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
    ///
    /// <para>Applies to the IMMEDIATE subscription only. The delayed subscription deliberately
    /// gets no dead-letter policy — see <c>PubSubBackgroundJobManager.CreateSubscriptionIfNotExistsAsync</c>
    /// for why (its not-yet-due NACKs would otherwise exhaust the delivery-attempt budget and
    /// dead-letter jobs purely for not being due).</para>
    ///
    /// <para>⚠️ <b>Know what that costs, because it is asymmetric.</b> With no policy there is
    /// also no attempt cap on the delayed subscription, so a delayed job whose BODY keeps
    /// throwing a non-<c>AbpException</c> is NACKed and redelivered indefinitely, bounded only by
    /// <see cref="JobQueueConfiguration.MessageRetentionDuration"/> (default 7 days) — and is then
    /// dropped with <b>no dead-letter record at all</b>. The identical failure on the IMMEDIATE
    /// subscription is dead-lettered after <see cref="MaxDeliveryAttempts"/> and preserved for
    /// inspection. So an operator auditing the <c>.DeadLetter</c> topics will not see poisoned
    /// DELAYED jobs, and their absence is not evidence that none were lost.</para>
    ///
    /// <para>⚠️ Related, same reason: a <c>delay</c> longer than
    /// <see cref="JobQueueConfiguration.MessageRetentionDuration"/> means the message expires
    /// before it ever comes due, so the job <b>silently never runs</b>. Raise the retention on
    /// that queue if long delays are intended.</para>
    /// </summary>
    public string? DeadLetterTopicSuffix { get; set; } = "DeadLetter";

    /// <summary>
    /// Default shortest redelivery backoff for DELAYED subscriptions.
    /// Default: 10 seconds. See <see cref="JobQueueConfiguration.DelayedRetryMinimumBackoff"/>.
    /// </summary>
    public TimeSpan DelayedRetryMinimumBackoff { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Default longest redelivery backoff for DELAYED subscriptions.
    /// Default: 600 seconds (the maximum Pub/Sub accepts).
    /// See <see cref="JobQueueConfiguration.DelayedRetryMaximumBackoff"/>.
    /// </summary>
    public TimeSpan DelayedRetryMaximumBackoff { get; set; } = TimeSpan.FromSeconds(600);

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
            PrefetchCount)
        {
            DelayedRetryMinimumBackoff = DelayedRetryMinimumBackoff,
            DelayedRetryMaximumBackoff = DelayedRetryMaximumBackoff,
        };

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
