using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OpenTap.LiveData
{
    /// <summary>The JSON form of live values and payloads (System.Text.Json). The schema is in <c>schema/livedata-v1.schema.json</c>.</summary>
    public static class LiveDataJson
    {
        /// <summary>The payload format version (the <c>v</c> field of every payload).</summary>
        public const int FormatVersion = 1;

        /// <summary>The serializer options of every payload: camel-case names as declared, nulls kept, NaN/Infinity as text.</summary>
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            WriteIndented = false,
        };

        /// <summary>A payload as UTF-8 JSON.</summary>
        public static byte[] Serialize<T>(T payload) => JsonSerializer.SerializeToUtf8Bytes(payload, Options);

        /// <summary>A payload from UTF-8 JSON.</summary>
        public static T? Deserialize<T>(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<T>(json, Options);

        /// <summary>
        /// A CLR value as JSON for its live data type: numbers as numbers (a TimeSpan in seconds; NaN and infinities as the
        /// texts "NaN", "Infinity", "-Infinity"), yes/no as true/false, times as ISO 8601 text in UTC, enums by name (flags
        /// joined by ", "), anything else as text. Null stays null.
        /// </summary>
        public static JsonNode? ToJson(object? value)
        {
            switch (value)
            {
                case null: return null;
                case bool b: return JsonValue.Create(b);
                case string s: return JsonValue.Create(s);
                case double d: return Number(d);
                case float f: return Number(f);
                case decimal m: return JsonValue.Create(m);
                case int i: return JsonValue.Create(i);
                case long l: return JsonValue.Create(l);
                case short sh: return JsonValue.Create(sh);
                case byte by: return JsonValue.Create(by);
                case sbyte sb: return JsonValue.Create(sb);
                case uint ui: return JsonValue.Create(ui);
                case ulong ul: return JsonValue.Create(ul);
                case ushort us: return JsonValue.Create(us);
                case TimeSpan ts: return Number(ts.TotalSeconds);
                case DateTime dt: return JsonValue.Create(Time(dt.Kind == DateTimeKind.Unspecified ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Local)) : new DateTimeOffset(dt)));
                case DateTimeOffset dto: return JsonValue.Create(Time(dto));
                case Enum e: return JsonValue.Create(e.ToString());
                default: return JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        private static JsonNode Number(double d)
        {
            if (double.IsNaN(d)) return JsonValue.Create("NaN")!;
            if (double.IsPositiveInfinity(d)) return JsonValue.Create("Infinity")!;
            if (double.IsNegativeInfinity(d)) return JsonValue.Create("-Infinity")!;
            return JsonValue.Create(d)!;
        }

        private static string Time(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }
}
