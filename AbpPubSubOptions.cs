namespace Bdaya.Abp.BackgroundJobs.PubSub;

/// <summary>
/// Container for multiple Pub/Sub connection configurations.
/// Allows defining named connections for different projects or environments.
/// </summary>
public class AbpPubSubOptions
{
    /// <summary>
    /// Dictionary of named connection configurations.
    /// The "Default" key is used when no specific connection is specified.
    /// </summary>
    public Dictionary<string, PubSubConnectionConfiguration> Connections { get; } = new()
    {
        { "Default", new PubSubConnectionConfiguration() }
    };

    /// <summary>
    /// Gets or sets the default connection configuration.
    /// This is a convenience property that accesses Connections["Default"].
    /// </summary>
    public PubSubConnectionConfiguration Default
    {
        get => Connections["Default"];
        set => Connections["Default"] = value;
    }
}
