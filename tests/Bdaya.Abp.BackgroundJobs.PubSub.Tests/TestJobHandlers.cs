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
