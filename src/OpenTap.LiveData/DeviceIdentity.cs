using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OpenTap.LiveData
{
    /// <summary>
    /// The Device ID of a resource: one ID names one physical device everywhere (every profile, session and PC).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every instrument and DUT has the member <see cref="DeviceIdTypeData.DeviceIdName"/> (shown as "Device ID"),
    /// saved with the Bench. Until a value is stored, the default applies:
    /// <list type="bullet">
    /// <item>driver type + <c>@</c> + the device's normalized identity: <c>asset:&lt;id&gt;</c> when the driver reports an
    /// asset identifier, else <c>visa:&lt;address&gt;</c> (VISA address in canonical form, so different spellings of one
    /// address give one ID), else <c>address:&lt;value&gt;</c> from the first text property whose name ends in
    /// <c>Address</c> (trimmed, lower case);</item>
    /// <item>a part (a resource without an identity of its own that points to another resource and has a Channel,
    /// ChannelId or SerialNumber): its parent's Device ID + <c>/</c> + that value;</item>
    /// <item>otherwise driver type + <c>@name:</c> + the resource name.</item>
    /// </list>
    /// The default is written the first time the Bench is saved; from then on the stored ID stays, also when the
    /// instrument is renamed or its address changes.
    /// </para>
    /// </remarks>
    public static class DeviceIdentity
    {
        /// <summary>Separates a part's segment from its parent's Device ID.</summary>
        public const char PartSeparator = '/';

        /// <summary>The property names a part's segment is read from, in this order.</summary>
        public static readonly IReadOnlyList<string> PartProperties = new[] { "Channel", "ChannelId", "SerialNumber" };

        private sealed class Box { public string? Stored; }

        private static readonly ConditionalWeakTable<object, Box> _stored = new ConditionalWeakTable<object, Box>();

        /// <summary>Raised after a stored Device ID changed (argument: the resource).</summary>
        public static event Action<object>? Changed;

        /// <summary>The Device ID of a resource: the stored one, else the default.</summary>
        public static string Get(object resource)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            return Get(resource, 0);
        }

        private static string Get(object resource, int depth) => Stored(resource) ?? Default(resource, depth);

        /// <summary>The stored Device ID, or null while the default applies.</summary>
        public static string? Stored(object resource)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            lock (_stored) return _stored.TryGetValue(resource, out var box) ? box.Stored : null;
        }

        /// <summary>Whether a Device ID is stored for the resource (loaded from the Bench or typed).</summary>
        public static bool IsStored(object resource) => Stored(resource) != null;

        /// <summary>Stores a Device ID. Empty or white space removes the stored ID, so the default applies again.</summary>
        public static void Set(object resource, string? deviceId)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            var value = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId!.Trim();
            bool changed;
            lock (_stored)
            {
                var box = _stored.GetValue(resource, _ => new Box());
                changed = box.Stored != value;
                box.Stored = value;
            }
            if (changed) Changed?.Invoke(resource);
        }

        /// <summary>
        /// Stores a Device ID only when none is stored yet; returns whether it was stored. Used to take over an identity a
        /// resource had before (for example a key another package stored) without overwriting an ID that is already there.
        /// </summary>
        public static bool SetIfNotStored(object resource, string? deviceId)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            if (string.IsNullOrWhiteSpace(deviceId)) return false;
            lock (_stored)
            {
                var box = _stored.GetValue(resource, _ => new Box());
                if (box.Stored != null) return false;
                box.Stored = deviceId!.Trim();
            }
            Changed?.Invoke(resource);
            return true;
        }

        /// <summary>The default Device ID of a resource (what applies while none is stored).</summary>
        public static string Default(object resource) => Default(resource, 0);

        private static string Default(object resource, int depth)
        {
            var type = DriverType(resource);
            var identity = Identity(resource);
            if (identity != null) return type + "@" + identity;
            if (depth < 8 && resource is IResource r && PartOf(r) is { } part)
                return Get(part.Parent, depth + 1) + PartSeparator + part.Segment;
            var name = resource is IResource named ? named.Name : null;
            return type + "@name:" + (string.IsNullOrWhiteSpace(name) ? "" : name!.Trim());
        }

        /// <summary>The driver type part of a Device ID: the resource's full CLR type name.</summary>
        public static string DriverType(object resource) => resource.GetType().FullName ?? resource.GetType().Name;

        /// <summary>
        /// The normalized physical identity of a resource (<c>asset:</c>, <c>visa:</c> or <c>address:</c>), or null when it
        /// has none of its own.
        /// </summary>
        public static string? Identity(object resource)
        {
            if (AssetIdentifier(resource) is { } asset) return "asset:" + asset;
            if (resource is ScpiInstrument scpi && Safe(() => scpi.VisaAddress) is { } visa && !string.IsNullOrWhiteSpace(visa))
                return "visa:" + NormalizeVisaAddress(visa);
            var address = resource.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string) && p.CanRead && p.GetIndexParameters().Length == 0
                            && p.Name.EndsWith("Address", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => Safe(() => p.GetValue(resource) as string))
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (address == null) return null;
            return LooksLikeVisa(address) ? "visa:" + NormalizeVisaAddress(address) : "address:" + address.Trim().ToLowerInvariant();
        }

        /// <summary>The parent and segment of a part, or null when the resource is no part.</summary>
        internal static (IResource Parent, string Segment)? PartOf(IResource resource)
        {
            IResource? parent = null;
            foreach (var p in resource.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length != 0 || !typeof(IResource).IsAssignableFrom(p.PropertyType)) continue;
                if (Safe(() => p.GetValue(resource)) is IResource candidate && !ReferenceEquals(candidate, resource))
                {
                    if (parent != null && !ReferenceEquals(parent, candidate)) return null; // two parents: not a part
                    parent = candidate;
                }
            }
            if (parent == null) return null;
            foreach (var name in PartProperties)
            {
                var prop = resource.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
                var text = SegmentText(Safe(() => prop.GetValue(resource)));
                if (!string.IsNullOrWhiteSpace(text)) return (parent, text!.Trim().Replace(PartSeparator, '_'));
            }
            return null;
        }

        private static string? SegmentText(object? value)
        {
            switch (value)
            {
                case null: return null;
                case string s: return s;
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }

        /// <summary>OpenTAP's asset identifier (Metrics and Assets package), read by name so the package is not needed.</summary>
        private static string? AssetIdentifier(object resource)
        {
            var asset = resource.GetType().GetInterfaces().FirstOrDefault(i => i.FullName == "OpenTap.Metrics.AssetDiscovery.IAsset");
            var property = asset?.GetProperty("AssetIdentifier");
            if (property == null) return null;
            var value = Safe(() => property.GetValue(resource) as string);
            return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }

        private static bool LooksLikeVisa(string address)
        {
            var a = address.Trim().ToUpperInvariant();
            return a.Contains("::") && (a.StartsWith("TCPIP") || a.StartsWith("USB") || a.StartsWith("GPIB") || a.StartsWith("ASRL") || a.StartsWith("VXI") || a.StartsWith("PXI"));
        }

        /// <summary>
        /// A VISA resource address in canonical form: lower case, board number 0 written out, the default LAN device name
        /// <c>inst0</c> written out, USB vendor/product ids as hexadecimal and the default USB interface 0 written out. So
        /// <c>TCPIP::192.168.1.20::INSTR</c> and <c>TCPIP0::192.168.1.20::inst0::INSTR</c> give the same text. An address
        /// that is not VISA, or a VISA alias, is only trimmed and lower-cased.
        /// </summary>
        public static string NormalizeVisaAddress(string address)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            var parts = address.Trim().Split(new[] { "::" }, StringSplitOptions.None).Select(p => p.Trim().ToLowerInvariant()).ToList();
            if (parts.Count < 2) return address.Trim().ToLowerInvariant();
            var head = parts[0];
            string Board(string prefix) => head.Length == prefix.Length ? prefix + "0" : head;
            var cls = parts[parts.Count - 1];
            if (head.StartsWith("tcpip"))
            {
                var iface = Board("tcpip");
                if (cls == "socket" && parts.Count == 4) return string.Join("::", iface, parts[1], parts[2], "socket");
                if (cls == "instr" && parts.Count == 3) return string.Join("::", iface, parts[1], "inst0", "instr");
                if (cls != "instr" && cls != "socket" && parts.Count == 2) return string.Join("::", iface, parts[1], "inst0", "instr");
                if (cls != "instr" && cls != "socket" && parts.Count == 3) return string.Join("::", iface, parts[1], parts[2], "instr");
                parts[0] = iface;
                return string.Join("::", parts);
            }
            if (head.StartsWith("usb"))
            {
                parts[0] = Board("usb");
                if (parts.Count >= 4)
                {
                    parts[1] = Hex(parts[1]);
                    parts[2] = Hex(parts[2]);
                }
                if (cls != "instr" && cls != "rawusb") parts.Add("instr");
                // USB0::vid::pid::serial::INSTR -> interface 0 written out
                if (parts.Count == 5 && parts[4] == "instr") parts.Insert(4, "0");
                return string.Join("::", parts);
            }
            if (head.StartsWith("gpib"))
            {
                parts[0] = Board("gpib");
                if (cls != "instr" && cls != "intfc") parts.Add("instr");
                return string.Join("::", parts);
            }
            if (head.StartsWith("asrl"))
            {
                if (cls != "instr") parts.Add("instr");
                return string.Join("::", parts);
            }
            return string.Join("::", parts);
        }

        private static string Hex(string number)
        {
            ulong value;
            if (number.StartsWith("0x") && ulong.TryParse(number.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                return "0x" + value.ToString("x4", CultureInfo.InvariantCulture);
            if (ulong.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return "0x" + value.ToString("x4", CultureInfo.InvariantCulture);
            return number;
        }

        private static readonly object _suggestionLock = new();
        private static Func<object, IEnumerable<string>>[] _suggestionSources = Array.Empty<Func<object, IEnumerable<string>>>();

        /// <summary>
        /// Adds Device IDs the field offers for a resource beyond the loaded Bench, for example the IDs a host knows from other
        /// computers. A source that throws is skipped.
        /// </summary>
        public static void RegisterSuggestions(Func<object, IEnumerable<string>> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            lock (_suggestionLock)
                if (Array.IndexOf(_suggestionSources, source) < 0) _suggestionSources = _suggestionSources.Concat(new[] { source }).ToArray();
        }

        /// <summary>Removes a source added with <see cref="RegisterSuggestions"/>.</summary>
        public static void UnregisterSuggestions(Func<object, IEnumerable<string>> source)
        {
            lock (_suggestionLock) _suggestionSources = _suggestionSources.Where(s => s != source).ToArray();
        }

        /// <summary>The Device IDs of the resources in the loaded Bench (instruments and DUTs), without duplicates, sorted.</summary>
        public static IReadOnlyList<string> KnownIds()
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var r in InstrumentSettings.Current) ids.Add(Get(r));
                foreach (var r in DutSettings.Current) ids.Add(Get(r));
            }
            catch
            {
                // settings not loaded (a process without a Bench): nothing to offer
            }
            return ids.ToList();
        }

        /// <summary>The Device IDs the field of <paramref name="resource"/> offers: <see cref="KnownIds()"/> and the registered sources'.</summary>
        public static IReadOnlyList<string> KnownIds(object resource)
        {
            var ids = new SortedSet<string>(KnownIds(), StringComparer.Ordinal);
            foreach (var source in Volatile.Read(ref _suggestionSources))
            {
                try
                {
                    foreach (var id in source(resource) ?? Enumerable.Empty<string>())
                        if (!string.IsNullOrWhiteSpace(id)) ids.Add(id.Trim());
                }
                catch
                {
                    // a host's source failed: offer the rest
                }
            }
            return ids.ToList();
        }

        private static T? Safe<T>(Func<T?> read) where T : class
        {
            try { return read(); }
            catch { return null; }
        }
    }
}
