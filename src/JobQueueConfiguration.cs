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

    /// <summary>
    /// Shortest redelivery backoff on the DELAYED subscription — the FIRST retry interval only.
    /// Default: 10 seconds (Google Pub/Sub's own default minimum backoff). Accepted range 0s-600s.
    ///
    /// <para>A delayed message that is not yet due is NACKed rather than executed, so the
    /// redelivery interval doubles as the "is it due yet" poll. But Pub/Sub's retry policy is
    /// EXPONENTIAL — per Google's spec, "retry delay will be exponential based on provided
    /// minimum and maximum backoffs" — so the interval starts here and CLIMBS toward
    /// <see cref="DelayedRetryMaximumBackoff"/>. This value bounds only the first interval.</para>
    ///
    /// <para>⚠️ It does NOT bound how late a job can fire, and lowering it will not deliver
    /// tighter timing for any delay long enough to climb the ladder — for that, lower
    /// <see cref="DelayedRetryMaximumBackoff"/>. See its remarks for the worked consequence.</para>
    /// </summary>
    public TimeSpan DelayedRetryMinimumBackoff { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Longest redelivery backoff on the DELAYED subscription, and therefore the knob that
    /// actually bounds LATENESS. Default: 600 seconds, which is also the maximum Pub/Sub accepts.
    ///
    /// <para>Because the interval grows exponentially from
    /// <see cref="DelayedRetryMinimumBackoff"/> toward this value, a job is checked for
    /// due-ness less and less often the longer it waits — so a long delay can overshoot by up to
    /// roughly this much. On the shipped 10s/600s defaults a retry ladder of 30s/120s/480s lands
    /// its later rungs late by MINUTES, not by ~10s. Lower this when a job type needs its delay
    /// honoured tightly, accepting more redelivery traffic in exchange.</para>
    ///
    /// <para>⚠️ Applied at subscription CREATION only. Changing either backoff has no effect on
    /// a subscription that already exists — Pub/Sub keeps the policy it was created with, and
    /// <c>CreateSubscriptionIfNotExistsAsync</c> returns early when the subscription is found.
    /// Delete the subscription (or patch it out of band) to re-apply.</para>
    /// </summary>
    public TimeSpan DelayedRetryMaximumBackoff { get; set; } = TimeSpan.FromSeconds(600);

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
