using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OpenTap.LiveData
{
    /// <summary>
    /// One live value of a resource: its description and how the collector gets it. Built by reflection for
    /// <c>[LiveData]</c> properties, and by hosts (<see cref="ILiveDataContributor"/>) for values of their own.
    /// </summary>
    public sealed class LiveDataMember
    {
        /// <summary>Creates a member.</summary>
        /// <param name="description">The description sent to watchers.</param>
        /// <param name="read">Reads the current value (called on push events and, for asked values, at the interval).</param>
        /// <param name="propertyName">The name raised in PropertyChanged for a pushed value; null when the host reports it itself.</param>
        public LiveDataMember(LiveDataValueDescription description, Func<object?> read, string? propertyName)
        {
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Read = read ?? throw new ArgumentNullException(nameof(read));
            PropertyName = propertyName;
        }

        /// <summary>The description sent to watchers.</summary>
        public LiveDataValueDescription Description { get; }

        /// <summary>Reads the current value.</summary>
        public Func<object?> Read { get; }

        /// <summary>The PropertyChanged name of a pushed value, or null.</summary>
        public string? PropertyName { get; }

        /// <summary>Whether the value is asked at an interval.</summary>
        public bool IsAsked => Description.Mode == LiveDataModes.Ask;

        /// <summary>The ask interval (at least 100 ms).</summary>
        public TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(0.1, Description.IntervalSeconds ?? LiveDataAttribute.DefaultIntervalSeconds));
    }

    /// <summary>
    /// Lets a host add live values of its own to a resource's description (for example values a host already knows from
    /// another mechanism). Registered with <see cref="LiveDataRuntime.RegisterContributor"/>.
    /// </summary>
    public interface ILiveDataContributor
    {
        /// <summary>The additional members of the resource; empty when none. Keys must not collide with [LiveData] properties.</summary>
        IEnumerable<LiveDataMember> Members(IResource resource);
    }

    /// <summary>Finds the <c>[LiveData]</c> properties of a resource and describes them.</summary>
    public static class LiveDataMembers
    {
        /// <summary>The full name the attribute is recognised by.</summary>
        public const string AttributeFullName = "OpenTap.LiveData.LiveDataAttribute";

        private static readonly TraceSource Log = OpenTap.Log.CreateSource("LiveData");

        /// <summary>The <c>[LiveData]</c> members of a resource (Phase 1 types only), ordered by [Display] order and name.</summary>
        public static IReadOnlyList<LiveDataMember> Find(object resource)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            var list = new List<LiveDataMember>();
            foreach (var p in resource.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                var attr = p.GetCustomAttributes(true).FirstOrDefault(a => a.GetType().FullName == AttributeFullName);
                if (attr == null) continue;
                var description = Describe(p, attr);
                if (description == null)
                {
                    Log.Warning("[LiveData] {0}.{1}: type {2} is not supported yet; the value is not sent.", resource.GetType().Name, p.Name, p.PropertyType.Name);
                    continue;
                }
                var prop = p;
                list.Add(new LiveDataMember(description, () => prop.GetValue(resource), description.Mode == LiveDataModes.Push ? p.Name : null));
            }
            return list.OrderBy(m => m.Description.Order).ThenBy(m => m.Description.Name, StringComparer.Ordinal).ToList();
        }

        /// <summary>Describes one property; null when its type is not supported.</summary>
        internal static LiveDataValueDescription? Describe(PropertyInfo p, object liveDataAttribute)
        {
            var (type, nullable, enumType) = JsonType(p.PropertyType);
            if (type == null) return null;
            var display = p.GetCustomAttribute<DisplayAttribute>(true);
            var unit = p.GetCustomAttribute<UnitAttribute>(true);
            var asked = p.GetSetMethod(true) == null;
            double interval = LiveDataAttribute.DefaultIntervalSeconds;
            var intervalProp = liveDataAttribute.GetType().GetProperty(nameof(LiveDataAttribute.IntervalSeconds));
            if (intervalProp?.GetValue(liveDataAttribute) is double d && d > 0 && !double.IsNaN(d) && !double.IsInfinity(d)) interval = d;
            return new LiveDataValueDescription
            {
                Name = p.Name,
                Display = display?.Name ?? p.Name,
                Description = string.IsNullOrEmpty(display?.Description) ? null : display!.Description,
                Group = display?.Group ?? Array.Empty<string>(),
                Order = display?.Order ?? 0,
                Unit = string.IsNullOrEmpty(unit?.Unit) ? null : unit!.Unit,
                Type = type,
                Nullable = nullable,
                EnumValues = enumType == null ? null : EnumValues(enumType),
                Mode = asked ? LiveDataModes.Ask : LiveDataModes.Push,
                IntervalSeconds = asked ? interval : (double?)null,
            };
        }

        /// <summary>The JSON type of a CLR type (null when not supported in Phase 1), whether it is nullable, and the enum type.</summary>
        public static (string? Type, bool Nullable, Type? EnumType) JsonType(Type clr)
        {
            var underlying = System.Nullable.GetUnderlyingType(clr);
            var nullable = underlying != null || !clr.IsValueType;
            var t = underlying ?? clr;
            if (t.IsEnum) return (LiveDataTypes.Enum, nullable, t);
            if (t == typeof(bool)) return (LiveDataTypes.Boolean, nullable, null);
            if (t == typeof(string)) return (LiveDataTypes.Text, true, null);
            if (t == typeof(DateTime) || t == typeof(DateTimeOffset)) return (LiveDataTypes.Time, nullable, null);
            if (t == typeof(TimeSpan)) return (LiveDataTypes.Number, nullable, null);
            if (t == typeof(double) || t == typeof(float) || t == typeof(decimal) || t == typeof(int) || t == typeof(long) || t == typeof(short)
                || t == typeof(byte) || t == typeof(sbyte) || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort))
                return (LiveDataTypes.Number, nullable, null);
            return (null, nullable, null);
        }

        private static IReadOnlyList<LiveDataEnumValue> EnumValues(Type enumType) =>
            enumType.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => new LiveDataEnumValue { Value = f.Name, Display = f.GetCustomAttribute<DisplayAttribute>()?.Name ?? f.Name })
                .ToList();
    }
}
