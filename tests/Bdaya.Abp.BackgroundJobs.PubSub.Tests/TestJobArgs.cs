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
/// Test job arguments for priority testing.
/// </summary>
public class PriorityJobArgs
{
    public string JobId { get; set; } = string.Empty;
    public int Priority { get; set; }
}
