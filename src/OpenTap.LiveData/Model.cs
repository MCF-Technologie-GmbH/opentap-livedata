using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OpenTap.LiveData
{
    /// <summary>The JSON type of a live value (Phase 1: single values).</summary>
    public static class LiveDataTypes
    {
        /// <summary>A number (integers and floating point; a TimeSpan as seconds).</summary>
        public const string Number = "number";
        /// <summary>Yes/no.</summary>
        public const string Boolean = "boolean";
        /// <summary>Text.</summary>
        public const string Text = "text";
        /// <summary>A point in time, ISO 8601 text.</summary>
        public const string Time = "time";
        /// <summary>One of named values; sent as the value's name.</summary>
        public const string Enum = "enum";
    }

    /// <summary>One choice of an enum value.</summary>
    public sealed class LiveDataEnumValue
    {
        /// <summary>The name sent as the value.</summary>
        [JsonPropertyName("value")] public string Value { get; set; } = "";
        /// <summary>The display name ([Display] on the enum member, else the name).</summary>
        [JsonPropertyName("display")] public string Display { get; set; } = "";
    }

    /// <summary>The description of one live value of a device.</summary>
    public sealed class LiveDataValueDescription
    {
        /// <summary>The key of the value (the property name); unique per device.</summary>
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        /// <summary>Display name ([Display]).</summary>
        [JsonPropertyName("display")] public string Display { get; set; } = "";
        /// <summary>Description ([Display]).</summary>
        [JsonPropertyName("description")] public string? Description { get; set; }
        /// <summary>Group path ([Display] Group).</summary>
        [JsonPropertyName("group")] public IReadOnlyList<string> Group { get; set; } = Array.Empty<string>();
        /// <summary>Order ([Display] Order).</summary>
        [JsonPropertyName("order")] public double Order { get; set; }
        /// <summary>Unit ([Unit]); null when none.</summary>
        [JsonPropertyName("unit")] public string? Unit { get; set; }
        /// <summary>The JSON type, one of <see cref="LiveDataTypes"/>.</summary>
        [JsonPropertyName("type")] public string Type { get; set; } = LiveDataTypes.Text;
        /// <summary>Whether the value can be null (no value).</summary>
        [JsonPropertyName("nullable")] public bool Nullable { get; set; }
        /// <summary>The choices of an enum value; null for other types.</summary>
        [JsonPropertyName("enumValues")] public IReadOnlyList<LiveDataEnumValue>? EnumValues { get; set; }
        /// <summary>"push" when the driver reports every change, "ask" when the value is read at an interval.</summary>
        [JsonPropertyName("mode")] public string Mode { get; set; } = LiveDataModes.Push;
        /// <summary>The read interval of an asked value in seconds; null for pushed values.</summary>
        [JsonPropertyName("intervalSeconds")] public double? IntervalSeconds { get; set; }
    }

    /// <summary>How a value gets its updates.</summary>
    public static class LiveDataModes
    {
        /// <summary>The driver reports every change (PropertyChanged).</summary>
        public const string Push = "push";
        /// <summary>The value is read at an interval while the device is open.</summary>
        public const string Ask = "ask";
    }

    /// <summary>The answer to a description request: every live value of one device.</summary>
    public sealed class LiveDataDescription
    {
        /// <summary>Version of the payload format.</summary>
        [JsonPropertyName("v")] public int Version { get; set; } = LiveDataJson.FormatVersion;
        /// <summary>The device's Device ID.</summary>
        [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
        /// <summary>The resource name in the publishing process's Bench.</summary>
        [JsonPropertyName("resource")] public string Resource { get; set; } = "";
        /// <summary>The driver type (full CLR type name).</summary>
        [JsonPropertyName("driverType")] public string DriverType { get; set; } = "";
        /// <summary>Who publishes (set by the host, for example a session name); null when the host sets none.</summary>
        [JsonPropertyName("holder")] public string? Holder { get; set; }
        /// <summary>The values.</summary>
        [JsonPropertyName("values")] public IReadOnlyList<LiveDataValueDescription> Values { get; set; } = Array.Empty<LiveDataValueDescription>();
    }

    /// <summary>One reported value with its time.</summary>
    public sealed class LiveDataSample
    {
        /// <summary>When the driver reported or the package read the value (UTC).</summary>
        [JsonPropertyName("t")] public DateTimeOffset Time { get; set; }
        /// <summary>The value as JSON (null = no value).</summary>
        [JsonPropertyName("v")] public JsonNode? Value { get; set; }
    }

    /// <summary>All reports of one value of one device within one batch interval (100 ms), oldest first.</summary>
    public sealed class LiveDataBatch
    {
        /// <summary>Version of the payload format.</summary>
        [JsonPropertyName("v")] public int Version { get; set; } = LiveDataJson.FormatVersion;
        /// <summary>The device's Device ID.</summary>
        [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
        /// <summary>The value's key (<see cref="LiveDataValueDescription.Name"/>).</summary>
        [JsonPropertyName("value")] public string Value { get; set; } = "";
        /// <summary>Who publishes (set by the host); null when the host sets none.</summary>
        [JsonPropertyName("holder")] public string? Holder { get; set; }
        /// <summary>The reports, oldest first. Every report is kept, also when the same value repeats.</summary>
        [JsonPropertyName("samples")] public IReadOnlyList<LiveDataSample> Samples { get; set; } = Array.Empty<LiveDataSample>();
    }

    /// <summary>The answer to a latest-values request: the newest report of every value that has one.</summary>
    public sealed class LiveDataLatest
    {
        /// <summary>Version of the payload format.</summary>
        [JsonPropertyName("v")] public int Version { get; set; } = LiveDataJson.FormatVersion;
        /// <summary>The device's Device ID.</summary>
        [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
        /// <summary>Who publishes (set by the host); null when the host sets none.</summary>
        [JsonPropertyName("holder")] public string? Holder { get; set; }
        /// <summary>The newest report per value key.</summary>
        [JsonPropertyName("values")] public IReadOnlyDictionary<string, LiveDataSample> Values { get; set; } = new Dictionary<string, LiveDataSample>();
    }
}
