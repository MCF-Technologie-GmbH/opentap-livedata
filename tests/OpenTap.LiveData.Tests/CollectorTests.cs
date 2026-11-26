using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenTap;

namespace OpenTap.LiveData.Tests
{
    [TestFixture, NonParallelizable]
    public class CollectorTests
    {
        private DateTimeOffset _now;
        private List<Action> _asks = null!;
        private MemoryTransport _transport = null!;

        [SetUp]
        public void SetUp()
        {
            LiveDataRuntime.Reset();
            _now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
            _asks = new List<Action>();
            LiveDataRuntime.Clock = () => _now;
            LiveDataRuntime.RunAsk = work => _asks.Add(work);
            LiveDataRuntime.AutoTick = false;
            _transport = new MemoryTransport();
        }

        [TearDown]
        public void TearDown()
        {
            LiveDataRuntime.Reset();
            LiveDataRuntime.Clock = () => DateTimeOffset.UtcNow;
            LiveDataRuntime.RunAsk = work => System.Threading.ThreadPool.QueueUserWorkItem(_ => work());
            LiveDataRuntime.AutoTick = true;
        }

        private void RunAsks()
        {
            var pending = _asks.ToList();
            _asks.Clear();
            foreach (var a in pending) a();
        }

        private FakeMeter OpenMeter(string address = "10.0.0.5")
        {
            var meter = new FakeMeter { Name = "DMM", Address = address };
            meter.Open();
            return meter;
        }

        [Test]
        public void NothingIsCollectedWithoutATransport()
        {
            var meter = OpenMeter();
            Assert.That(LiveDataRuntime.Attach(meter), Is.Null);
            meter.PushVoltage(1);
            LiveDataRuntime.TickAll();
            Assert.That(LiveDataRuntime.Collectors, Is.Empty);
        }

        [Test]
        public void PushesArriveInOrderAndEveryReportIsSentAlsoWhenTheValueRepeats()
        {
            LiveDataRuntime.RegisterTransport(_transport);
            var meter = OpenMeter();
            var collector = LiveDataRuntime.Attach(meter)!;
            Assert.That(_transport.Served.Keys, Is.EquivalentTo(new[] { collector.DeviceId }));

            meter.PushBarcode("A1"); _now += TimeSpan.FromMilliseconds(10);
            meter.PushBarcode("A1"); _now += TimeSpan.FromMilliseconds(10);
            meter.PushBarcode("B2");
            meter.PushVoltage(1.5);
            LiveDataRuntime.TickAll();

            var barcodes = _transport.Batches.Where(b => b.Value == "Barcode").ToList();
            Assert.That(barcodes, Has.Count.EqualTo(1), "one batch per value per step");
            Assert.That(barcodes[0].Samples.Select(s => s.Value!.GetValue<string>()), Is.EqualTo(new[] { "A1", "A1", "B2" }));
            Assert.That(barcodes[0].Samples.Select(s => s.Time), Is.Ordered);
            Assert.That(barcodes[0].DeviceId, Is.EqualTo(collector.DeviceId));
            Assert.That(_transport.SamplesOf("Voltage").Single().Value!.GetValue<double>(), Is.EqualTo(1.5));
        }

        [Test]
        public void ABatchIsSentOnlyForValuesWithNewReports()
        {
            LiveDataRuntime.RegisterTransport(_transport);
            var meter = OpenMeter();
            LiveDataRuntime.Attach(meter);
            RunAsks();
            LiveDataRuntime.TickAll();
            _transport.Batches.Clear();

            _now += LiveDataCollector.BatchInterval;
            LiveDataRuntime.TickAll();
            Assert.That(_transport.Batches, Is.Empty, "nothing new, nothing sent");

            meter.PushVoltage(2);
            _now += LiveDataCollector.BatchInterval;
            LiveDataRuntime.TickAll();
            Assert.That(_transport.Batches.Select(b => b.Value), Is.EqualTo(new[] { "Voltage" }));
        }

        [Test]
        public void AskedValuesAreReadOnceASecondAndAtTheDriversOwnInterval()
        {
            LiveDataRuntime.RegisterTransport(_transport);
            var meter = OpenMeter();
            LiveDataRuntime.Attach(meter); // first step: both asked values are due
            RunAsks();
            Assert.That(meter.Reads, Is.EqualTo(1));

            for (int i = 0; i < 9; i++) { _now += TimeSpan.FromMilliseconds(100); LiveDataRuntime.TickAll(); RunAsks(); }
            Assert.That(meter.Reads, Is.EqualTo(1), "not again before 1 s");
            _now += TimeSpan.FromMilliseconds(100); LiveDataRuntime.TickAll(); RunAsks();
            Assert.That(meter.Reads, Is.EqualTo(2), "again after 1 s");

            meter.AskedValue = 3; meter.SlowValue = 8;
            for (int i = 0; i < 30; i++) { _now += TimeSpan.FromMilliseconds(100); LiveDataRuntime.TickAll(); RunAsks(); }
            var slow = _transport.SamplesOf("Slow").Select(s => s.Value!.GetValue<double>()).ToList();
            Assert.That(slow, Is.EqualTo(new[] { 7.0 }), "Slow (5 s) is not read again within 4 s");
            _now += TimeSpan.FromSeconds(1); LiveDataRuntime.TickAll(); RunAsks(); LiveDataRuntime.TickAll();
            Assert.That(_transport.SamplesOf("Slow").Last().Value!.GetValue<double>(), Is.EqualTo(8.0));
            Assert.That(_transport.SamplesOf("Temperature").Select(s => s.Value!.GetValue<double>()), Is.EqualTo(new[] { 1.0, 3.0 }),
                "an asked value is sent when it changed");
        }

        [Test]
        public void AskedValuesAreNeverReadFromTwoThreadsAtOnceOrWhileClosed()
        {
            LiveDataRuntime.RegisterTransport(_transport);
            var meter = OpenMeter();
            LiveDataRuntime.Attach(meter);
            Assert.That(_asks, Has.Count.EqualTo(1));
            for (int i = 0; i < 30; i++) { _now += TimeSpan.FromMilliseconds(100); LiveDataRuntime.TickAll(); }
            Assert.That(_asks, Has.Count.EqualTo(1), "no second read starts while one is running");
            RunAsks();

            meter.Close();
            meter.Reads = 0;
            for (int i = 0; i < 30; i++) { _now += TimeSpan.FromMilliseconds(100); LiveDataRuntime.TickAll(); RunAsks(); }
            Assert.That(meter.Reads, Is.EqualTo(0), "not read while the device is closed");
        }

        [Test]
        public void AClosedGateStopsPublishingAndAnsweringAndItMovesWithTheLock()
        {
            LiveDataRuntime.RegisterTransport(_transport);
            var gate = new FakeGate();
            LiveDataRuntime.RegisterPublishGate(gate);
            var meter = OpenMeter();
            var collector = LiveDataRuntime.Attach(meter)!;
            Assert.That(_transport.Served, Is.Empty);
            meter.PushVoltage(1);
            LiveDataRuntime.TickAll();
            Assert.That(_transport.Batches, Is.Empty, "no lock, nothing published");
            Assert.That(_asks, Is.Empty, "no lock, the device is not read");

            gate.Set(meter, true);
            Assert.That(_transport.Served.Keys, Is.EquivalentTo(new[] { collector.DeviceId }));
            meter.PushVoltage(2);
            LiveDataRuntime.TickAll();
            Assert.That(_transport.SamplesOf("Voltage").Select(s => s.Value!.GetValue<double>()), Is.EqualTo(new[] { 2.0 }));

            gate.Set(meter, false);
            meter.PushVoltage(3);
            LiveDataRuntime.TickAll();
            Assert.That(_transport.Served, Is.Empty);
            Assert.That(_transport.SamplesOf("Voltage").Select(s => s.Value!.GetValue<double>()), Is.EqualTo(new[] { 2.0 }));
        }

        [Test]
        public void TwoOpenCopiesOfOneDeviceGiveOneValuePerChangeWhenOnlyTheLockHolderPublishes()
        {
            LiveDataRuntime.RegisterTransport(_transport);
            var gate = new FakeGate();
            LiveDataRuntime.RegisterPublishGate(gate);
            var a = OpenMeter("10.0.0.9");
            var b = OpenMeter("10.0.0.9");
            LiveDataRuntime.Attach(a);
            LiveDataRuntime.Attach(b);
            gate.Set(a, true);
            a.PushVoltage(5);
            b.PushVoltage(5);
            LiveDataRuntime.TickAll();
            Assert.That(_transport.SamplesOf("Voltage").Count(), Is.EqualTo(1));
        }

        [Test]
        public void DescriptionAndLatestAreAnsweredForTheDevice()
        {
            LiveDataRuntime.RegisterTransport(_transport);
            LiveDataRuntime.SetHolder(() => "Station 1");
            var meter = OpenMeter();
            var collector = LiveDataRuntime.Attach(meter)!;
            meter.PushVoltage(4.25);
            RunAsks();
            var endpoint = _transport.Served[collector.DeviceId];

            var description = LiveDataJson.Deserialize<LiveDataDescription>(LiveDataJson.Serialize(endpoint.Describe()))!;
            Assert.That(description.DeviceId, Is.EqualTo(collector.DeviceId));
            Assert.That(description.Holder, Is.EqualTo("Station 1"));
            Assert.That(description.Resource, Is.EqualTo("DMM"));
            var latest = endpoint.Latest();
            Assert.That(latest.Values["Voltage"].Value!.GetValue<double>(), Is.EqualTo(4.25));
            Assert.That(latest.Values["Temperature"].Value!.GetValue<double>(), Is.EqualTo(1.0));
        }

        [Test]
        public void AChangedDeviceIdMovesThePublishingAndAnnouncesTheNewDescription()
        {
            LiveDataRuntime.RegisterTransport(_transport);
            var meter = OpenMeter();
            var collector = LiveDataRuntime.Attach(meter)!;
            var old = collector.DeviceId;
            DeviceIdentity.Set(meter, "DMM-7");
            Assert.That(collector.DeviceId, Is.EqualTo("DMM-7"));
            Assert.That(_transport.Served.Keys, Is.EquivalentTo(new[] { "DMM-7" }));
            Assert.That(_transport.Log, Does.Contain("unserve " + old));
            Assert.That(_transport.DescriptionChanged, Is.EqualTo(new[] { "DMM-7" }));
        }

        [Test]
        public void DetachStopsCollectingOnlyAfterTheLastAttach()
        {
            LiveDataRuntime.RegisterTransport(_transport);
            var meter = OpenMeter();
            var collector = LiveDataRuntime.Attach(meter)!;
            Assert.That(LiveDataRuntime.Attach(meter), Is.SameAs(collector));
            LiveDataRuntime.Detach(meter);
            Assert.That(_transport.Served, Is.Not.Empty);
            LiveDataRuntime.Detach(meter);
            Assert.That(_transport.Served, Is.Empty);
            meter.PushVoltage(9);
            LiveDataRuntime.TickAll();
            Assert.That(_transport.SamplesOf("Voltage"), Is.Empty);
        }

        [Test]
        public void ContributedValuesArePublishedLikeLiveDataValues()
        {
            LiveDataRuntime.RegisterTransport(_transport);
            LiveDataRuntime.RegisterContributor(new MetricContributor());
            var meter = OpenMeter();
            var collector = LiveDataRuntime.Attach(meter)!;
            Assert.That(collector.Members.Select(m => m.Description.Name), Does.Contain("Legacy"));
            collector.Report("Legacy", 12.0);
            collector.Report("Legacy", 12.0);
            LiveDataRuntime.TickAll();
            Assert.That(_transport.SamplesOf("Legacy").Count(), Is.EqualTo(2));
        }

        private sealed class MetricContributor : ILiveDataContributor
        {
            public IEnumerable<LiveDataMember> Members(IResource resource)
            {
                yield return new LiveDataMember(new LiveDataValueDescription { Name = "Legacy", Display = "Legacy", Type = LiveDataTypes.Number }, () => 0.0, null);
                yield return new LiveDataMember(new LiveDataValueDescription { Name = "Voltage", Display = "Collision", Type = LiveDataTypes.Number }, () => 0.0, null);
            }
        }
    }
}
