using System.Text;
using System.Text.Json;
using Volo.Abp.DependencyInjection;

namespace Bdaya.Abp.BackgroundJobs.PubSub;

/// <summary>
/// Interface for serializing and deserializing job data for Pub/Sub messages.
/// </summary>
public interface IPubSubJobSerializer
{
    /// <summary>
    /// Serializes job arguments to a byte array.
    /// </summary>
    byte[] Serialize(object jobArgs);

    /// <summary>
    /// Deserializes a byte array to job arguments of the specified type.
    /// </summary>
    object? Deserialize(byte[] data, Type type);

    /// <summary>
    /// Deserializes a byte array to job arguments of type T.
    /// </summary>
    T? Deserialize<T>(byte[] data);
}

/// <summary>
/// Default JSON serializer for Pub/Sub job messages using System.Text.Json.
/// </summary>
public class PubSubJobSerializer : IPubSubJobSerializer, ITransientDependency
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public byte[] Serialize(object jobArgs)
    {
        var json = JsonSerializer.Serialize(jobArgs, JsonOptions);
        return Encoding.UTF8.GetBytes(json);
    }

    public object? Deserialize(byte[] data, Type type)
    {
        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize(json, type, JsonOptions);
    }

    public T? Deserialize<T>(byte[] data)
    {
        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
