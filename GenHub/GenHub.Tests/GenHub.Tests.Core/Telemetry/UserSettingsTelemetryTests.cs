using System.Text.Json;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using Xunit;

namespace GenHub.Tests.Core.Telemetry;

/// <summary>
/// Unit tests for telemetry configuration in <see cref="UserSettings"/>.
/// </summary>
public class UserSettingsTelemetryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>
    /// Verifies default values for telemetry settings.
    /// </summary>
    [Fact]
    public void DefaultSettings_HaveAnonymousMetricsEnabled()
    {
        var settings = new UserSettings();

        Assert.Equal(TelemetryLevel.AnonymousMetrics, settings.TelemetryPreference);
        Assert.False(settings.EnableTelemetryPromptShown);
        Assert.Null(settings.AnonymousInstallationId);
    }

    /// <summary>
    /// Verifies Clone method copies telemetry settings correctly.
    /// </summary>
    [Fact]
    public void Clone_CopiesTelemetrySettings()
    {
        var original = new UserSettings
        {
            TelemetryPreference = TelemetryLevel.CrashReportsOnly,
            EnableTelemetryPromptShown = true,
            AnonymousInstallationId = "test-guid-123",
        };

        var clone = original.Clone();

        Assert.Equal(TelemetryLevel.CrashReportsOnly, clone.TelemetryPreference);
        Assert.True(clone.EnableTelemetryPromptShown);
        Assert.Equal("test-guid-123", clone.AnonymousInstallationId);
    }

    /// <summary>
    /// Verifies JSON roundtrip serialization of telemetry settings.
    /// </summary>
    [Fact]
    public void JsonSerialization_RoundtripsTelemetrySettings()
    {
        var original = new UserSettings
        {
            TelemetryPreference = TelemetryLevel.Disabled,
            EnableTelemetryPromptShown = true,
            AnonymousInstallationId = "inst-456",
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(TelemetryLevel.Disabled, deserialized.TelemetryPreference);
        Assert.True(deserialized.EnableTelemetryPromptShown);
        Assert.Equal("inst-456", deserialized.AnonymousInstallationId);
    }
}
