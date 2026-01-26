using Google.Apis.Auth.OAuth2;

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
    /// Consider using <see cref="CredentialsJson"/> or <see cref="Credential"/> instead for better security.
    /// </summary>
    public string? CredentialsPath { get; set; }

    /// <summary>
    /// JSON string containing service account credentials.
    /// Useful when credentials are stored in a secret manager (Azure Key Vault, AWS Secrets Manager, etc.).
    /// Takes precedence over <see cref="CredentialsPath"/>.
    /// </summary>
    public string? CredentialsJson { get; set; }

    /// <summary>
    /// A pre-configured GoogleCredential instance.
    /// Takes precedence over <see cref="CredentialsJson"/> and <see cref="CredentialsPath"/>.
    /// Use this for maximum flexibility (Workload Identity Federation, custom credentials, etc.).
    /// </summary>
    public GoogleCredential? Credential { get; set; }

    /// <summary>
    /// The Pub/Sub emulator host for local development (e.g., "localhost:8085").
    /// When set, overrides cloud connections and uses the local emulator.
    /// </summary>
    public string? EmulatorHost { get; set; }
}
