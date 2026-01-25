namespace Bdaya.Abp.BackgroundJobs.PubSub;

/// <summary>
/// Configuration options for a Google Cloud Pub/Sub connection.
/// </summary>
public class PubSubConnectionConfiguration
{
    /// <summary>
    /// The Google Cloud Project ID.
    /// Required for Pub/Sub operations.
    /// </summary>
    public string ProjectId { get; set; } = default!;

    /// <summary>
    /// Path to the service account credentials JSON file.
    /// If not set, uses Application Default Credentials (ADC).
    /// </summary>
    public string? CredentialsPath { get; set; }

    /// <summary>
    /// The Pub/Sub emulator host for local development (e.g., "localhost:8085").
    /// When set, overrides cloud connections and uses the local emulator.
    /// </summary>
    public string? EmulatorHost { get; set; }
}
