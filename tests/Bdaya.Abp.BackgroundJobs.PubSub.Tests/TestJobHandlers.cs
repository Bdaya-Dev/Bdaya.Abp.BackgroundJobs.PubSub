using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;

namespace Bdaya.Abp.BackgroundJobs.PubSub.Tests;

/// <summary>
/// Test job handler for TestJobArgs.
/// </summary>
public class TestJobHandler : AsyncBackgroundJob<TestJobArgs>, ITransientDependency
{
    public static ConcurrentBag<TestJobArgs> ProcessedJobs { get; } = new();
    public static int ExecutionCount => ProcessedJobs.Count;

    private readonly ILogger<TestJobHandler> _logger;

    public TestJobHandler(ILogger<TestJobHandler> logger)
    {
        _logger = logger;
    }

    public override Task ExecuteAsync(TestJobArgs args)
    {
        _logger.LogInformation("Processing test job: {Message}, Value: {Value}", args.Message, args.Value);
        ProcessedJobs.Add(args);
        return Task.CompletedTask;
    }

    public static void Reset()
    {
        ProcessedJobs.Clear();
    }
}

/// <summary>
/// Test job handler for CustomNamedJobArgs.
/// </summary>
public class CustomNamedJobHandler : AsyncBackgroundJob<CustomNamedJobArgs>, ITransientDependency
{
    public static ConcurrentBag<CustomNamedJobArgs> ProcessedJobs { get; } = new();

    public override Task ExecuteAsync(CustomNamedJobArgs args)
    {
        ProcessedJobs.Add(args);
        return Task.CompletedTask;
    }

    public static void Reset()
    {
        ProcessedJobs.Clear();
    }
}

/// <summary>
/// Test job handler for DelayedJobArgs.
/// </summary>
public class DelayedJobHandler : AsyncBackgroundJob<DelayedJobArgs>, ITransientDependency
{
    public static ConcurrentBag<(DelayedJobArgs Args, DateTime ProcessedAt)> ProcessedJobs { get; } = new();

    public override Task ExecuteAsync(DelayedJobArgs args)
    {
        ProcessedJobs.Add((args, DateTime.UtcNow));
        return Task.CompletedTask;
    }

    public static void Reset()
    {
        ProcessedJobs.Clear();
    }
}

/// <summary>
/// Handler for the delayed end-to-end test (invora-backend#312). Records the wall-clock time
/// each job body actually ran, so the test can assert the DELAY was honoured rather than merely
/// that something executed.
/// </summary>
public class DelayedE2EJobHandler : AsyncBackgroundJob<DelayedE2EJobArgs>, ITransientDependency
{
    public static ConcurrentBag<(DelayedE2EJobArgs Args, DateTime ProcessedAt)> ProcessedJobs { get; } = new();

    public override Task ExecuteAsync(DelayedE2EJobArgs args)
    {
        ProcessedJobs.Add((args, DateTime.UtcNow));
        return Task.CompletedTask;
    }

    public static void Reset()
    {
        ProcessedJobs.Clear();
    }
}

/// <summary>
/// Handler for the degraded-delayed-startup guard. Its only job is to prove the IMMEDIATE path
/// still delivers after the delayed path failed to start.
/// </summary>
public class DegradedDelayedJobHandler : AsyncBackgroundJob<DegradedDelayedJobArgs>, ITransientDependency
{
    public static ConcurrentBag<DegradedDelayedJobArgs> ProcessedJobs { get; } = new();

    public override Task ExecuteAsync(DegradedDelayedJobArgs args)
    {
        ProcessedJobs.Add(args);
        return Task.CompletedTask;
    }

    public static void Reset()
    {
        ProcessedJobs.Clear();
    }
}

/// <summary>
/// Test job handler for PriorityJobArgs.
/// </summary>
public class PriorityJobHandler : AsyncBackgroundJob<PriorityJobArgs>, ITransientDependency
{
    public static ConcurrentQueue<PriorityJobArgs> ProcessedJobs { get; } = new();

    public override Task ExecuteAsync(PriorityJobArgs args)
    {
        ProcessedJobs.Enqueue(args);
        return Task.CompletedTask;
    }

    public static void Reset()
    {
        ProcessedJobs.Clear();
    }
}
