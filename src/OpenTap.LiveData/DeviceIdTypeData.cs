using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;

namespace OpenTap.LiveData
{
    /// <summary>
    /// Adds the "Device ID" member to every instrument and DUT without changing the driver type: an OpenTAP stacked type
    /// data provider wraps the resource's type data and appends the member. The persisted type name stays the driver's,
    /// so Bench files, the Editor and every settings API see an ordinary resource with one more setting.
    /// </summary>
    public sealed class DeviceIdTypeDataProvider : IStackedTypeDataProvider, ITapPlugin
    {
        private static readonly ConcurrentDictionary<ITypeData, ITypeData> _wrappers = new ConcurrentDictionary<ITypeData, ITypeData>();

        /// <summary>Runs after OpenTAP's CLR provider and before providers of other packages that wrap resources (for example ResourceHub at 2.5).</summary>
        public double Priority => 2.2;

        /// <inheritdoc />
        public ITypeData? GetTypeData(string identifier, TypeDataProviderStack stack) => null; // type names stay the driver's

        /// <inheritdoc />
        public ITypeData? GetTypeData(object obj, TypeDataProviderStack stack)
        {
            if (!Applies(obj)) return null;
            var inner = stack.GetTypeData(obj);
            if (inner == null || Wraps(inner)) return null;
            return _wrappers.GetOrAdd(inner, i => new DeviceIdTypeData(i));
        }

        /// <summary>Instruments and DUTs and the other resources a Bench keeps; never result listeners or component settings.</summary>
        public static bool Applies(object? obj) => obj is IResource && obj is not IResultListener && obj is not ComponentSettings;

        private static bool Wraps(ITypeData type)
        {
            for (ITypeData? t = type; t != null; t = t.BaseType)
                if (t is DeviceIdTypeData) return true;
            return false;
        }
    }

    /// <summary>The type data of a resource with the Device ID member (see <see cref="DeviceIdTypeDataProvider"/>).</summary>
    public sealed class DeviceIdTypeData : ITypeData
    {
        /// <summary>The member name of the Device ID, as it is written into Bench files.</summary>
        public const string DeviceIdName = "LiveData.DeviceId";

        /// <summary>Hidden helper member: the Device IDs the field offers.</summary>
        public const string KnownIdsName = "LiveData.KnownDeviceIds";

        private readonly ITypeData _inner;
        private readonly IMemberData[] _members;
        private readonly IMemberData _deviceId;
        private readonly IMemberData _knownIds;

        internal DeviceIdTypeData(ITypeData inner)
        {
            _inner = inner;
            _deviceId = new DeviceIdMember(this);
            _knownIds = new KnownIdsMember(this);
            _members = inner.GetMembers().Concat(new[] { _deviceId, _knownIds }).ToArray();
        }

        /// <inheritdoc />
        public string Name => _inner.Name;
        /// <inheritdoc />
        public IEnumerable<object> Attributes => _inner.Attributes;
        /// <inheritdoc />
        public ITypeData BaseType => _inner;
        /// <inheritdoc />
        public bool CanCreateInstance => _inner.CanCreateInstance;
        /// <inheritdoc />
        public object CreateInstance(object[] arguments) => _inner.CreateInstance(arguments);
        /// <inheritdoc />
        public IEnumerable<IMemberData> GetMembers() => _members;

        /// <inheritdoc />
        public IMemberData? GetMember(string name)
        {
            if (name == DeviceIdName) return _deviceId;
            if (name == KnownIdsName) return _knownIds;
            return _inner.GetMember(name);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is DeviceIdTypeData o && _inner.Equals(o._inner);
        /// <inheritdoc />
        public override int GetHashCode() => _inner.GetHashCode() ^ 0x1D1D;
        /// <inheritdoc />
        public override string ToString() => _inner.Name + " (+Device ID)";

        private sealed class DeviceIdMember : IMemberData
        {
            private readonly object[] _attributes =
            {
                new DisplayAttribute("Device ID",
                    "Names the physical device. Every resource with the same Device ID - on any profile, in any session, on any computer - is the same device. " +
                    "The default is the driver type and the device's address; it is saved with the Bench and then stays, also when the resource is renamed. " +
                    "Give two different devices two different IDs, also when a Bench is used on two computers.",
                    "Device", Order: 990),
                new BrowsableAttribute(true),
                new SuggestedValuesAttribute(KnownIdsName),
            };

            public DeviceIdMember(ITypeData declaring) { DeclaringType = declaring; }
            public string Name => DeviceIdName;
            public ITypeData DeclaringType { get; }
            public ITypeData TypeDescriptor => TypeData.FromType(typeof(string));
            public bool Writable => true;
            public bool Readable => true;
            public IEnumerable<object> Attributes => _attributes;
            public object GetValue(object owner) => DeviceIdentity.Get(owner);
            public void SetValue(object owner, object? value) => DeviceIdentity.Set(owner, value?.ToString());
        }

        private sealed class KnownIdsMember : IMemberData
        {
            private readonly object[] _attributes = { new BrowsableAttribute(false), new XmlIgnoreAttribute() };
            public KnownIdsMember(ITypeData declaring) { DeclaringType = declaring; }
            public string Name => KnownIdsName;
            public ITypeData DeclaringType { get; }
            public ITypeData TypeDescriptor => TypeData.FromType(typeof(IEnumerable<string>));
            public bool Writable => false;
            public bool Readable => true;
            public IEnumerable<object> Attributes => _attributes;
            public object GetValue(object owner) => DeviceIdentity.KnownIds();
            public void SetValue(object owner, object? value) => throw new InvalidOperationException(KnownIdsName + " is read-only");
        }
    }
}
