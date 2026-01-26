using Volo.Abp.BackgroundJobs;

namespace Bdaya.Abp.BackgroundJobs.PubSub;

/// <summary>
/// Interface for the Pub/Sub background job manager.
/// Extends IBackgroundJobManager with Pub/Sub-specific functionality.
/// </summary>
public interface IPubSubBackgroundJobManager : IBackgroundJobManager
{
    /// <summary>
    /// Initializes the Pub/Sub background job manager asynchronously.
    /// Creates topics and subscriptions as needed.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Starts processing jobs for a specific job type.
    /// </summary>
    Task StartProcessingAsync<TArgs>() where TArgs : class;

    /// <summary>
    /// Stops the background job processing.
    /// </summary>
    Task StopAsync();
}
