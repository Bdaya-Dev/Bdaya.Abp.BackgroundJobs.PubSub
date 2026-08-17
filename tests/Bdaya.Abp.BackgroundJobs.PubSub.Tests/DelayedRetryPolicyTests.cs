using Shouldly;
using Volo.Abp;

namespace Bdaya.Abp.BackgroundJobs.PubSub.Tests;

/// <summary>
/// The delayed subscription's retry policy is built from configurable backoffs, and Pub/Sub
/// bounds both to 0s-600s. Left unchecked, an out-of-range value fails at CreateSubscription
/// time with a generic <c>INVALID_ARGUMENT</c> that names neither the offending setting nor the
/// job type — so the misconfiguration surfaces as an opaque startup failure. These pin the
/// clearer error. (Raised by CodeRabbit on the PR.)
/// </summary>
public class DelayedRetryPolicyTests
{
    private static JobQueueConfiguration Config(TimeSpan min, TimeSpan max) =>
        new(typeof(TestJobArgs), "t", "s", "t.delayed", "s.delayed")
        {
            DelayedRetryMinimumBackoff = min,
            DelayedRetryMaximumBackoff = max,
        };

    [Fact]
    public void Defaults_Are_Inside_The_Range_PubSub_Accepts()
    {
        // The shipped defaults must not need a consumer to fix them.
        var defaults = new AbpPubSubBackgroundJobOptions().GetOrCreateJobQueue<TestJobArgs>();

        var policy = PubSubBackgroundJobManager.BuildDelayedRetryPolicy(defaults);

        policy.MinimumBackoff.ToTimeSpan().ShouldBe(TimeSpan.FromSeconds(10));
        policy.MaximumBackoff.ToTimeSpan().ShouldBe(TimeSpan.FromSeconds(600));
    }

    [Fact]
    public void The_Upper_Bound_Itself_Is_Accepted()
    {
        // Boundary: 600s is the maximum Pub/Sub allows, so it must be legal, not off-by-one.
        var policy = PubSubBackgroundJobManager.BuildDelayedRetryPolicy(
            Config(TimeSpan.Zero, TimeSpan.FromSeconds(600)));

        policy.MaximumBackoff.ToTimeSpan().ShouldBe(TimeSpan.FromSeconds(600));
    }

    [Theory]
    [InlineData(-1, 600)]
    [InlineData(0, 601)]
    [InlineData(700, 700)]
    public void A_Backoff_Outside_PubSubs_Range_Is_Rejected_By_Name(int minSeconds, int maxSeconds)
    {
        var ex = Should.Throw<AbpException>(() => PubSubBackgroundJobManager.BuildDelayedRetryPolicy(
            Config(TimeSpan.FromSeconds(minSeconds), TimeSpan.FromSeconds(maxSeconds))));

        // The message has to be actionable, which is the whole point of checking here rather
        // than letting Pub/Sub reject it.
        ex.Message.ShouldContain("DelayedRetry");
        ex.Message.ShouldContain(nameof(TestJobArgs));
    }

    [Fact]
    public void A_Maximum_Below_The_Minimum_Is_Rejected()
    {
        var ex = Should.Throw<AbpException>(() => PubSubBackgroundJobManager.BuildDelayedRetryPolicy(
            Config(TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(10))));

        ex.Message.ShouldContain(nameof(JobQueueConfiguration.DelayedRetryMaximumBackoff));
        ex.Message.ShouldContain(nameof(JobQueueConfiguration.DelayedRetryMinimumBackoff));
    }
}
