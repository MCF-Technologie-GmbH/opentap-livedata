using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenTap.LiveData
{
    /// <summary>
    /// The process-wide entry point a host uses: register the transport (and optionally a publish gate and contributors),
    /// then attach every resource while it is open and detach it when it closes. Without a registered transport
    /// <see cref="Attach"/> does nothing: the package collects and sends nothing.
    /// </summary>
    public static class LiveDataRuntime
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<IResource, Entry> _attached = new Dictionary<IResource, Entry>(ReferenceComparer.Instance);
        private static readonly List<ILiveDataContributor> _contributors = new List<ILiveDataContributor>();
        private static ILiveDataTransport? _transport;
        private static ILiveDataPublishGate? _gate;
        private static Func<string?> _holder = () => null;
        private static Timer? _timer;
        private static int _ticking;

        private sealed class Entry
        {
            public Entry(LiveDataCollector collector) { Collector = collector; }
            public LiveDataCollector Collector { get; }
            public int Count = 1;
        }

        /// <summary>Clock and ask scheduling; replaced by tests.</summary>
        internal static Func<DateTimeOffset> Clock = () => DateTimeOffset.UtcNow;
        internal static Action<Action> RunAsk = work => ThreadPool.QueueUserWorkItem(_ => work());
        internal static bool AutoTick = true;

        /// <summary>The registered transport, or null.</summary>
        public static ILiveDataTransport? Transport { get { lock (_lock) return _transport; } }

        /// <summary>The registered publish gate, or null (every open resource publishes).</summary>
        public static ILiveDataPublishGate? PublishGate { get { lock (_lock) return _gate; } }

        /// <summary>Registers the transport of this process (replaces a previous one).</summary>
        public static void RegisterTransport(ILiveDataTransport transport)
        {
            lock (_lock) _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        /// <summary>Removes the transport if it is the registered one; attached collectors stop publishing at their next step.</summary>
        public static void UnregisterTransport(ILiveDataTransport transport)
        {
            lock (_lock) if (ReferenceEquals(_transport, transport)) _transport = null;
        }

        /// <summary>Registers the publish gate of this process (replaces a previous one).</summary>
        public static void RegisterPublishGate(ILiveDataPublishGate gate)
        {
            lock (_lock)
            {
                if (_gate != null) _gate.Changed -= OnGateChanged;
                _gate = gate ?? throw new ArgumentNullException(nameof(gate));
                _gate.Changed += OnGateChanged;
            }
        }

        /// <summary>Removes the publish gate if it is the registered one.</summary>
        public static void UnregisterPublishGate(ILiveDataPublishGate gate)
        {
            lock (_lock)
            {
                if (!ReferenceEquals(_gate, gate)) return;
                _gate.Changed -= OnGateChanged;
                _gate = null;
            }
        }

        /// <summary>Adds a contributor of host values; used for resources attached afterwards.</summary>
        public static void RegisterContributor(ILiveDataContributor contributor)
        {
            if (contributor == null) throw new ArgumentNullException(nameof(contributor));
            lock (_lock) if (!_contributors.Contains(contributor)) _contributors.Add(contributor);
        }

        /// <summary>Removes a contributor.</summary>
        public static void UnregisterContributor(ILiveDataContributor contributor)
        {
            lock (_lock) _contributors.Remove(contributor);
        }

        /// <summary>Sets who publishes in this process (for example a session name); sent with every payload.</summary>
        public static void SetHolder(Func<string?> holder)
        {
            lock (_lock) _holder = holder ?? (() => null);
        }

        /// <summary>
        /// Starts collecting a resource (call it when the resource opened). Attaching the same resource again only counts
        /// up; it is collected until it is detached as often. Returns null when no transport is registered or the resource
        /// has no live values.
        /// </summary>
        public static LiveDataCollector? Attach(IResource resource)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            LiveDataCollector collector;
            lock (_lock)
            {
                if (_transport == null) return null;
                if (_attached.TryGetValue(resource, out var existing))
                {
                    existing.Count++;
                    return existing.Collector;
                }
                var members = MembersOf(resource);
                if (members.Count == 0) return null;
                collector = new LiveDataCollector(resource, members, () => Transport, () => PublishGate, () => CurrentHolder(), () => Clock(), work => RunAsk(work));
                _attached[resource] = new Entry(collector);
                if (AutoTick && _timer == null) _timer = new Timer(_ => TickAll(), null, LiveDataCollector.BatchInterval, LiveDataCollector.BatchInterval);
            }
            collector.Tick();
            return collector;
        }

        /// <summary>Stops collecting a resource once it is detached as often as it was attached (call it when it closed).</summary>
        public static void Detach(IResource resource)
        {
            if (resource == null) return;
            LiveDataCollector? stop = null;
            lock (_lock)
            {
                if (!_attached.TryGetValue(resource, out var entry)) return;
                if (--entry.Count > 0) return;
                _attached.Remove(resource);
                stop = entry.Collector;
                if (_attached.Count == 0 && _timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
            }
            stop.Dispose();
        }

        /// <summary>The collector of an attached resource, or null.</summary>
        public static LiveDataCollector? CollectorOf(IResource resource)
        {
            lock (_lock) return _attached.TryGetValue(resource, out var e) ? e.Collector : null;
        }

        /// <summary>The attached collectors.</summary>
        public static IReadOnlyList<LiveDataCollector> Collectors
        {
            get { lock (_lock) return _attached.Values.Select(e => e.Collector).ToList(); }
        }

        /// <summary>The live values of a resource: its [LiveData] properties and the members of registered contributors.</summary>
        public static IReadOnlyList<LiveDataMember> MembersOf(IResource resource)
        {
            var members = LiveDataMembers.Find(resource).ToList();
            var names = new HashSet<string>(members.Select(m => m.Description.Name), StringComparer.Ordinal);
            ILiveDataContributor[] contributors;
            lock (_lock) contributors = _contributors.ToArray();
            foreach (var c in contributors)
            {
                IEnumerable<LiveDataMember> extra;
                try { extra = c.Members(resource).ToList(); }
                catch (Exception ex)
                {
                    Log.CreateSource("LiveData").Debug("Contributor {0} failed for {1}: {2}", c.GetType().Name, resource.Name, ex.Message);
                    continue;
                }
                foreach (var m in extra)
                    if (names.Add(m.Description.Name)) members.Add(m);
            }
            return members;
        }

        /// <summary>One step of every attached collector (called every 100 ms; tests call it directly).</summary>
        public static void TickAll()
        {
            if (Interlocked.CompareExchange(ref _ticking, 1, 0) != 0) return;
            try
            {
                foreach (var c in Collectors)
                {
                    try { c.Tick(); }
                    catch (Exception ex) { Log.CreateSource("LiveData").Debug("Live data step failed for {0}: {1}", c.Resource.Name, ex.Message); }
                }
            }
            finally { Interlocked.Exchange(ref _ticking, 0); }
        }

        private static string? CurrentHolder()
        {
            Func<string?> h;
            lock (_lock) h = _holder;
            try { return h(); }
            catch { return null; }
        }

        private static void OnGateChanged(IResource? resource)
        {
            foreach (var c in Collectors)
                if (resource == null || ReferenceEquals(c.Resource, resource))
                    try { c.Tick(); }
                    catch { /* the next timer step tries again */ }
        }

        /// <summary>Detaches everything and clears the registrations (tests).</summary>
        internal static void Reset()
        {
            List<LiveDataCollector> all;
            lock (_lock)
            {
                all = _attached.Values.Select(e => e.Collector).ToList();
                _attached.Clear();
                _timer?.Dispose();
                _timer = null;
                if (_gate != null) _gate.Changed -= OnGateChanged;
                _gate = null;
                _transport = null;
                _contributors.Clear();
                _holder = () => null;
            }
            foreach (var c in all) c.Dispose();
        }

        private sealed class ReferenceComparer : IEqualityComparer<IResource>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(IResource? x, IResource? y) => ReferenceEquals(x, y);
            public int GetHashCode(IResource obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
