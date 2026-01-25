using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Modularity;

namespace Bdaya.Abp.BackgroundJobs.PubSub.Tests;

[DependsOn(
    typeof(AbpBackgroundJobsPubSubModule),
    typeof(AbpBackgroundJobsModule),
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
            options.AutoCreateTopics = true;
            options.AutoCreateSubscriptions = true;
            options.PrefetchCount = 1;
        });

        // Register test job handlers
        context.Services.AddTransient<TestJobHandler>();
    }
}
