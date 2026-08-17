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

            // A not-yet-due delayed message is NACKed and redelivered after this backoff, so it
            // doubles as the poll interval for "is it due yet". Production defaults to 10s/600s;
            // tests use seconds so a 3s-delay job is observed promptly instead of up to 10s late.
            options.DelayedRetryMinimumBackoff = TimeSpan.FromSeconds(1);
            options.DelayedRetryMaximumBackoff = TimeSpan.FromSeconds(5);
        });

        // Register test job handlers
        context.Services.AddTransient<TestJobHandler>();
    }
}
