using System.Text.Json.Serialization;

namespace GenHub.ProxyLauncher;

/// <summary>
/// Source-generated JSON context for proxy configuration.
/// Keeps deserialization trim-compatible when the proxy is published trimmed.
/// </summary>
[JsonSerializable(typeof(Program.ProxyConfig))]
internal partial class ProxyJsonContext : JsonSerializerContext
{
}
