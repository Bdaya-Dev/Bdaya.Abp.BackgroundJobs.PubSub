namespace Bdaya.Abp.BackgroundJobs.PubSub.Tests;

/// <summary>
/// Test job arguments for simple job.
/// </summary>
public class TestJobArgs
{
    public string Message { get; set; } = string.Empty;
    public int Value { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Test job arguments with custom name.
/// </summary>
[BackgroundJobName("CustomNamedJob")]
public class CustomNamedJobArgs
{
    public string Data { get; set; } = string.Empty;
}

/// <summary>
/// Test job arguments for delayed execution.
/// </summary>
public class DelayedJobArgs
{
    public string Payload { get; set; } = string.Empty;
    public DateTime ScheduledFor { get; set; }
}

/// <summary>
/// Test job arguments for the delayed END-TO-END test (invora-backend#312).
///
/// <para>Deliberately a distinct type from <see cref="DelayedJobArgs"/> so it resolves to its
/// own delayed topic + subscription. <c>Should_Enqueue_Delayed_Job</c> parks a
/// <see cref="DelayedJobArgs"/> message with a five-minute delay on the shared emulator, and
/// with flow control at one outstanding message that parked message would be redelivered and
/// re-NACKed in front of the one this test is waiting for.</para>
/// </summary>
public class DelayedE2EJobArgs
{
    public string Payload { get; set; } = string.Empty;
}

/// <summary>
/// Job args whose queue is deliberately configured with an OUT-OF-RANGE delayed retry backoff
/// (see <c>PubSubTestModule</c>), so starting its delayed consumer fails. Used to pin that
/// <c>StartProcessingAsync</c> degrades instead of throwing — a throw would abort ABP startup for
/// the whole application, since consumers call it from <c>OnApplicationInitializationAsync</c>.
/// </summary>
public class DegradedDelayedJobArgs
{
    public string Payload { get; set; } = string.Empty;
}

/// <summary>
/// Test job arguments for priority testing.
/// </summary>
public class PriorityJobArgs
{
    public string JobId { get; set; } = string.Empty;
    public int Priority { get; set; }
}
