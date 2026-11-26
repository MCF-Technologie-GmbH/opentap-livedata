using System;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace OpenTap.LiveData.Tests
{
    [TestFixture]
    public class DescriptionTests
    {
        [Test]
        public void EveryPhaseOneTypeIsDescribed()
        {
            var members = LiveDataMembers.Find(new FakeMeter()).ToDictionary(m => m.Description.Name, m => m.Description);

            Assert.That(members.Keys, Does.Not.Contain("NotLive"));
            Assert.That(members.Keys, Does.Not.Contain("Unsupported"), "arrays come with Phase 2");

            var v = members["Voltage"];
            Assert.That((v.Type, v.Unit, v.Display, v.Description, v.Mode, v.Nullable), Is.EqualTo((LiveDataTypes.Number, "V", "Voltage", "Measured voltage", LiveDataModes.Push, false)));
            Assert.That(v.Group, Is.EqualTo(new[] { "Measure" }));
            Assert.That(v.IntervalSeconds, Is.Null);

            Assert.That(members["Barcode"].Type, Is.EqualTo(LiveDataTypes.Text));
            Assert.That(members["Barcode"].Nullable, Is.True);
            Assert.That(members["OutputOn"].Type, Is.EqualTo(LiveDataTypes.Boolean));
            Assert.That(members["LastCalibration"].Type, Is.EqualTo(LiveDataTypes.Time));
            Assert.That(members["Uptime"].Type, Is.EqualTo(LiveDataTypes.Number));
            Assert.That(members["Count"].Type, Is.EqualTo(LiveDataTypes.Number));
            Assert.That(members["Count"].Nullable, Is.True);

            var range = members["Range"];
            Assert.That(range.Type, Is.EqualTo(LiveDataTypes.Enum));
            Assert.That(range.EnumValues!.Select(e => (e.Value, e.Display)), Is.EqualTo(new[] { ("Auto", "Automatic"), ("Low", "Low"), ("High", "High") }));

            Assert.That(members["Temperature"].Mode, Is.EqualTo(LiveDataModes.Ask));
            Assert.That(members["Temperature"].IntervalSeconds, Is.EqualTo(1.0));
            Assert.That(members["Slow"].IntervalSeconds, Is.EqualTo(5.0));
        }

        [Test]
        public void TheAttributeIsRecognisedByItsFullNameAlsoInADriversOwnCopy()
        {
            var members = LiveDataMembers.Find(new OwnAttributeDriver.OwnAttributeMeter());
            Assert.That(members.Select(m => m.Description.Name), Is.EqualTo(new[] { "Voltage" }));
            Assert.That(members[0].Description.Mode, Is.EqualTo(LiveDataModes.Ask));
            Assert.That(members[0].Description.IntervalSeconds, Is.EqualTo(2.5));
            Assert.That(members[0].Description.Unit, Is.EqualTo("V"));
        }

        [Test]
        public void ValuesBecomePlainJson()
        {
            Assert.That(LiveDataJson.ToJson(null), Is.Null);
            Assert.That(LiveDataJson.ToJson(1.5)!.ToJsonString(), Is.EqualTo("1.5"));
            Assert.That(LiveDataJson.ToJson(true)!.ToJsonString(), Is.EqualTo("true"));
            Assert.That(LiveDataJson.ToJson(Range.Auto)!.GetValue<string>(), Is.EqualTo("Auto"));
            Assert.That(LiveDataJson.ToJson(TimeSpan.FromMinutes(1))!.GetValue<double>(), Is.EqualTo(60.0));
            Assert.That(LiveDataJson.ToJson(double.NaN)!.GetValue<string>(), Is.EqualTo("NaN"));
            Assert.That(LiveDataJson.ToJson(new DateTimeOffset(2026, 11, 26, 13, 0, 0, TimeSpan.FromHours(1)))!.GetValue<string>(), Is.EqualTo("2026-11-26T12:00:00.0000000Z"));

            var batch = new LiveDataBatch { DeviceId = "x", Value = "Voltage", Samples = new[] { new LiveDataSample { Time = DateTimeOffset.UnixEpoch, Value = JsonValue.Create(2) } } };
            var json = System.Text.Encoding.UTF8.GetString(LiveDataJson.Serialize(batch));
            Assert.That(json, Is.EqualTo("{\"v\":1,\"deviceId\":\"x\",\"value\":\"Voltage\",\"holder\":null,\"samples\":[{\"t\":\"1970-01-01T00:00:00+00:00\",\"v\":2}]}"));
        }
    }
}
