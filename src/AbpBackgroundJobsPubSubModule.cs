using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Modularity;

namespace Bdaya.Abp.BackgroundJobs.PubSub;

/// <summary>
/// ABP module for Google Cloud Pub/Sub background jobs integration.
/// Add this module as a dependency to use Pub/Sub for background job processing.
/// </summary>
[DependsOn(typeof(AbpBackgroundJobsAbstractionsModule))]
public class AbpBackgroundJobsPubSubModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        // Configure Pub/Sub connection options from configuration
        Configure<AbpPubSubOptions>(configuration.GetSection("PubSub"));

        // Configure background job options from configuration
        Configure<AbpPubSubBackgroundJobOptions>(configuration.GetSection("PubSub:BackgroundJobs"));
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        await context
            .ServiceProvider
            .GetRequiredService<IPubSubBackgroundJobManager>()
            .InitializeAsync();
    }

    public override async Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
    {
        var jobManager = context.ServiceProvider.GetRequiredService<IPubSubBackgroundJobManager>();

        if (jobManager is PubSubBackgroundJobManager pubSubJobManager)
        {
            await pubSubJobManager.StopAsync();
        }
    }
}
