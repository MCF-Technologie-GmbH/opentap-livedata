using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace OpenTap.LiveData
{
    /// <summary>
    /// Collects the live values of one resource and hands them to the transport: every pushed report with its time,
    /// asked values at their interval while the resource is open, and every 100 ms one batch per value that has new
    /// reports. Created by <see cref="LiveDataRuntime.Attach"/>.
    /// </summary>
    public sealed class LiveDataCollector : ILiveDataEndpoint, IDisposable
    {
        /// <summary>The batch interval.</summary>
        public static readonly TimeSpan BatchInterval = TimeSpan.FromMilliseconds(100);

        /// <summary>At most this many reports of one value are kept between two batches; older ones are dropped.</summary>
        public const int MaxPendingPerValue = 10000;

        private static readonly TraceSource Log = OpenTap.Log.CreateSource("LiveData");

        private readonly object _lock = new object();
        private readonly IResource _resource;
        private readonly IReadOnlyList<LiveDataMember> _members;
        private readonly Dictionary<string, LiveDataMember> _byProperty;
        private readonly Dictionary<string, LiveDataMember> _byName;
        private readonly Dictionary<string, List<LiveDataSample>> _pending = new Dictionary<string, List<LiveDataSample>>(StringComparer.Ordinal);
        private readonly Dictionary<string, LiveDataSample> _latest = new Dictionary<string, LiveDataSample>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTimeOffset> _nextAsk = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        private readonly Func<ILiveDataTransport?> _transport;
        private readonly Func<ILiveDataPublishGate?> _gate;
        private readonly Func<string?> _holder;
        private readonly Func<DateTimeOffset> _clock;
        private readonly Action<Action> _runAsk;
        private int _asking;
        private bool _served;
        private bool _disposed;
        private string _deviceId;

        internal LiveDataCollector(IResource resource, IReadOnlyList<LiveDataMember> members, Func<ILiveDataTransport?> transport,
            Func<ILiveDataPublishGate?> gate, Func<string?> holder, Func<DateTimeOffset> clock, Action<Action> runAsk)
        {
            _resource = resource;
            _members = members;
            _transport = transport;
            _gate = gate;
            _holder = holder;
            _clock = clock;
            _runAsk = runAsk;
            _byName = members.ToDictionary(m => m.Description.Name, StringComparer.Ordinal);
            _byProperty = members.Where(m => m.PropertyName != null).ToDictionary(m => m.PropertyName!, StringComparer.Ordinal);
            _deviceId = DeviceIdentity.Get(resource);
            if (resource is INotifyPropertyChanged npc) npc.PropertyChanged += OnPropertyChanged;
            DeviceIdentity.Changed += OnDeviceIdChanged;
        }

        /// <summary>The resource.</summary>
        public IResource Resource => _resource;

        /// <summary>The Device ID the values are published under.</summary>
        public string DeviceId { get { lock (_lock) return _deviceId; } }

        /// <summary>The live values of the resource.</summary>
        public IReadOnlyList<LiveDataMember> Members => _members;

        /// <summary>Whether the collector currently publishes (the gate is open and a transport is registered).</summary>
        public bool Publishing { get { lock (_lock) return _served; } }

        /// <summary>
        /// Records a report of a value (for host-contributed values the host reports itself). Every call is one report,
        /// also when the value is the same as before. Unknown keys are ignored.
        /// </summary>
        public void Report(string valueName, object? value, DateTimeOffset? time = null)
        {
            if (!_byName.ContainsKey(valueName)) return;
            Add(valueName, LiveDataJson.ToJson(value), time ?? _clock(), onlyIfChanged: false);
        }

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == null || !_byProperty.TryGetValue(e.PropertyName, out var member)) return;
            object? value;
            try { value = member.Read(); }
            catch (Exception ex)
            {
                Log.Debug("Reading {0}.{1} after its change failed: {2}", _resource.Name, member.Description.Name, ex.Message);
                return;
            }
            Add(member.Description.Name, LiveDataJson.ToJson(value), _clock(), onlyIfChanged: false);
        }

        private void Add(string name, System.Text.Json.Nodes.JsonNode? json, DateTimeOffset time, bool onlyIfChanged)
        {
            lock (_lock)
            {
                if (_disposed) return;
                if (onlyIfChanged && _latest.TryGetValue(name, out var last) && JsonEquals(last.Value, json)) return;
                var sample = new LiveDataSample { Time = time, Value = json };
                _latest[name] = new LiveDataSample { Time = time, Value = json?.DeepClone() };
                if (!_served) return; // not publishing: only the latest value is kept
                if (!_pending.TryGetValue(name, out var list)) _pending[name] = list = new List<LiveDataSample>();
                if (list.Count >= MaxPendingPerValue) list.RemoveAt(0);
                list.Add(sample);
            }
        }

        private static bool JsonEquals(System.Text.Json.Nodes.JsonNode? a, System.Text.Json.Nodes.JsonNode? b)
        {
            if (a == null || b == null) return a == null && b == null;
            return a.ToJsonString() == b.ToJsonString();
        }

        /// <summary>
        /// One collector step (called every 100 ms by the runtime): follows the publish gate, starts due reads of asked
        /// values while the resource is open, and hands one batch per value with new reports to the transport.
        /// </summary>
        public void Tick()
        {
            var now = _clock();
            var transport = _transport();
            bool may;
            try { may = transport != null && (_gate()?.MayPublish(_resource) ?? true); }
            catch (Exception ex)
            {
                Log.Debug("Publish gate failed for {0}: {1}", _resource.Name, ex.Message);
                may = false;
            }

            string deviceId;
            bool serve = false, unserve = false;
            lock (_lock)
            {
                if (_disposed) return;
                deviceId = _deviceId;
                if (may && !_served) { _served = true; serve = true; }
                else if (!may && _served) { _served = false; unserve = true; _pending.Clear(); }
            }
            if (serve) transport!.Serve(deviceId, this);
            if (unserve) transport?.Unserve(deviceId);
            if (!may) return;

            AskDue(now);
            Flush(transport!, deviceId);
        }

        private void AskDue(DateTimeOffset now)
        {
            if (!_resource.IsConnected) return;
            List<LiveDataMember> due;
            lock (_lock)
            {
                due = _members.Where(m => m.IsAsked && (!_nextAsk.TryGetValue(m.Description.Name, out var next) || next <= now)).ToList();
                if (due.Count == 0) return;
            }
            if (Interlocked.CompareExchange(ref _asking, 1, 0) != 0) return; // never two reads at once
            lock (_lock) foreach (var m in due) _nextAsk[m.Description.Name] = now + m.Interval;
            _runAsk(() =>
            {
                try
                {
                    foreach (var m in due)
                    {
                        if (!_resource.IsConnected) break;
                        object? value;
                        try { value = m.Read(); }
                        catch (Exception ex)
                        {
                            Log.Debug("Reading {0}.{1} failed: {2}", _resource.Name, m.Description.Name, ex.Message);
                            continue;
                        }
                        Add(m.Description.Name, LiveDataJson.ToJson(value), _clock(), onlyIfChanged: true);
                    }
                }
                finally { Interlocked.Exchange(ref _asking, 0); }
            });
        }

        private void Flush(ILiveDataTransport transport, string deviceId)
        {
            List<KeyValuePair<string, List<LiveDataSample>>> batches;
            lock (_lock)
            {
                if (_pending.Count == 0) return;
                batches = _pending.Where(p => p.Value.Count > 0).ToList();
                _pending.Clear();
            }
            var holder = _holder();
            foreach (var b in batches)
            {
                try
                {
                    transport.Publish(new LiveDataBatch { DeviceId = deviceId, Value = b.Key, Holder = holder, Samples = b.Value });
                }
                catch (Exception ex)
                {
                    Log.Debug("Publishing {0}.{1} failed: {2}", _resource.Name, b.Key, ex.Message);
                }
            }
        }

        private void OnDeviceIdChanged(object resource)
        {
            if (!ReferenceEquals(resource, _resource)) return;
            var newId = DeviceIdentity.Get(_resource);
            string oldId;
            bool served;
            lock (_lock)
            {
                if (_disposed || newId == _deviceId) return;
                oldId = _deviceId;
                _deviceId = newId;
                served = _served;
                _pending.Clear();
            }
            var transport = _transport();
            if (served && transport != null)
            {
                transport.Unserve(oldId);
                transport.Serve(newId, this);
                transport.PublishDescriptionChanged(newId);
            }
        }

        /// <inheritdoc />
        public LiveDataDescription Describe()
        {
            return new LiveDataDescription
            {
                DeviceId = DeviceId,
                Resource = _resource.Name ?? "",
                DriverType = DeviceIdentity.DriverType(_resource),
                Holder = _holder(),
                Values = _members.Select(m => m.Description).ToList(),
            };
        }

        /// <inheritdoc />
        public LiveDataLatest Latest()
        {
            lock (_lock)
            {
                return new LiveDataLatest
                {
                    DeviceId = _deviceId,
                    Holder = _holder(),
                    Values = _latest.ToDictionary(p => p.Key, p => new LiveDataSample { Time = p.Value.Time, Value = p.Value.Value?.DeepClone() }, StringComparer.Ordinal),
                };
            }
        }

        /// <summary>Stops collecting and stops answering requests for the device.</summary>
        public void Dispose()
        {
            string deviceId;
            bool served;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                served = _served;
                _served = false;
                _pending.Clear();
                deviceId = _deviceId;
            }
            if (_resource is INotifyPropertyChanged npc) npc.PropertyChanged -= OnPropertyChanged;
            DeviceIdentity.Changed -= OnDeviceIdChanged;
            if (served) _transport()?.Unserve(deviceId);
        }
    }
}
