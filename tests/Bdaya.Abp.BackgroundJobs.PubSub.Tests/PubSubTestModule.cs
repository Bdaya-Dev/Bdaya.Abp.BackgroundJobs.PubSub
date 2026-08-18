using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Modularity;

namespace Bdaya.Abp.BackgroundJobs.PubSub.Tests;

[DependsOn(
    typeof(AbpBackgroundJobsModule),
    typeof(AbpBackgroundJobsPubSubModule),
    typeof(AbpTestBaseModule),
    typeof(AbpAutofacModule)
)]
public class PubSubTestModule : AbpModule
{
    public static string? EmulatorHost { get; set; }
    public static string? ProjectId { get; set; }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpPubSubOptions>(options =>
        {
            options.Default.ProjectId = ProjectId ?? "test-project";
            options.Default.EmulatorHost = EmulatorHost ?? "localhost:8085";
        });

        Configure<AbpPubSubBackgroundJobOptions>(options =>
        {
            options.DefaultTopicPrefix = "test-jobs";
            options.DefaultSubscriptionPrefix = "test-jobs";
            options.DefaultDelayedTopicPrefix = "test-jobs.Delayed";
            options.DefaultDelayedSubscriptionPrefix = "test-jobs.Delayed";
            options.AutoCreateTopics = true;
            options.AutoCreateSubscriptions = true;
            options.PrefetchCount = 1;

            // A not-yet-due delayed message is NACKed and redelivered after a backoff that grows
            // EXPONENTIALLY from the minimum toward the maximum, so the maximum is what bounds how
            // late a job can fire. Production defaults are 10s/600s; both are squeezed here so a
            // 3s-delay job is observed within the test's budget rather than up to 600s late.
            options.DelayedRetryMinimumBackoff = TimeSpan.FromSeconds(1);
            options.DelayedRetryMaximumBackoff = TimeSpan.FromSeconds(5);

            // Deliberately BROKEN, for the degraded-startup guard: 20 minutes is outside the
            // 0s-600s Pub/Sub accepts, so building this queue's delayed retry policy throws and
            // its delayed consumer cannot start. Every other job type is unaffected.
            options.GetOrCreateJobQueue<DegradedDelayedJobArgs>().DelayedRetryMaximumBackoff =
                TimeSpan.FromMinutes(20);
        });

        // Register test job handlers
        context.Services.AddTransient<TestJobHandler>();
    }
}
