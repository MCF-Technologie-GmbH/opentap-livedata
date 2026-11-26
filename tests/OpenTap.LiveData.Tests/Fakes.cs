using System;
using System.Collections.Generic;
using System.Linq;
using OpenTap;

namespace OpenTap.LiveData.Tests
{
    public enum Range
    {
        [Display("Automatic")] Auto,
        Low,
        High,
    }

    /// <summary>A meter with every Phase 1 type: pushed values (private set + PropertyChanged) and asked values (no setter).</summary>
    [Display("Fake Meter")]
    public class FakeMeter : Instrument
    {
        private double _voltage;
        private string? _barcode;
        public int Reads;
        public int Concurrent;
        public int MaxConcurrent;
        public double AskedValue = 1;
        public double SlowValue = 7;

        public string Address { get; set; } = "";

        [LiveData, Display("Voltage", "Measured voltage", Group: "Measure", Order: 1), Unit("V")]
        public double Voltage { get => _voltage; private set { _voltage = value; OnPropertyChanged(nameof(Voltage)); } }

        [LiveData, Display("Barcode", Order: 2)]
        public string? Barcode { get => _barcode; private set { _barcode = value; OnPropertyChanged(nameof(Barcode)); } }

        [LiveData, Display("Temperature", Order: 3), Unit("°C")]
        public double Temperature
        {
            get
            {
                var now = System.Threading.Interlocked.Increment(ref Concurrent);
                MaxConcurrent = Math.Max(MaxConcurrent, now);
                Reads++;
                System.Threading.Interlocked.Decrement(ref Concurrent);
                return AskedValue;
            }
        }

        [LiveData(IntervalSeconds = 5), Display("Slow", Order: 4)]
        public double Slow => SlowValue;

        [LiveData] public bool OutputOn { get; private set; }
        [LiveData] public DateTime LastCalibration { get; private set; }
        [LiveData] public Range Range { get; private set; }
        [LiveData] public int? Count { get; private set; }
        [LiveData] public TimeSpan Uptime { get; private set; }
        [LiveData] public double[] Unsupported { get; private set; } = Array.Empty<double>();

        public double NotLive { get; set; }

        public void PushVoltage(double v) => Voltage = v;
        public void PushBarcode(string v) => Barcode = v;
    }

    /// <summary>A SCPI instrument for VISA address tests.</summary>
    [Display("Fake Scpi")]
    public class FakeScpi : ScpiInstrument
    {
    }

    /// <summary>A part: an output of a supply, pointing to it, with a channel.</summary>
    [Display("Fake Output")]
    public class FakeOutput : Instrument
    {
        public FakeScpi? Supply { get; set; }
        public int Channel { get; set; }
    }

    public sealed class MemoryTransport : ILiveDataTransport
    {
        public readonly List<LiveDataBatch> Batches = new List<LiveDataBatch>();
        public readonly Dictionary<string, ILiveDataEndpoint> Served = new Dictionary<string, ILiveDataEndpoint>();
        public readonly List<string> DescriptionChanged = new List<string>();
        public readonly List<string> Log = new List<string>();

        public void Publish(LiveDataBatch batch)
        {
            // through JSON, as a real transport sends it
            var copy = LiveDataJson.Deserialize<LiveDataBatch>(LiveDataJson.Serialize(batch))!;
            Batches.Add(copy);
        }

        public void PublishDescriptionChanged(string deviceId) => DescriptionChanged.Add(deviceId);
        public void Serve(string deviceId, ILiveDataEndpoint endpoint) { Served[deviceId] = endpoint; Log.Add("serve " + deviceId); }
        public void Unserve(string deviceId) { Served.Remove(deviceId); Log.Add("unserve " + deviceId); }

        public IEnumerable<LiveDataSample> SamplesOf(string value) => Batches.Where(b => b.Value == value).SelectMany(b => b.Samples);
    }

    public sealed class FakeGate : ILiveDataPublishGate
    {
        public readonly HashSet<IResource> Open = new HashSet<IResource>();
        public bool MayPublish(IResource resource) => Open.Contains(resource);
        public event Action<IResource?>? Changed;
        public void Set(IResource r, bool open)
        {
            if (open) Open.Add(r); else Open.Remove(r);
            Changed?.Invoke(r);
        }
    }
}
