using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OpenTap;

namespace OpenTap.LiveData.Tests
{
    [TestFixture]
    public class DeviceIdTests
    {
        [TestCase("TCPIP0::192.168.1.20::INSTR", "TCPIP::192.168.1.20::inst0::INSTR")]
        [TestCase("tcpip::192.168.1.20::instr", "TCPIP0::192.168.1.20::INST0::INSTR")]
        [TestCase("TCPIP::Meter-7.local::hislip0::INSTR", "tcpip0::meter-7.local::HISLIP0::instr")]
        [TestCase("TCPIP0::10.0.0.2::5025::SOCKET", "TCPIP::10.0.0.2::5025::socket")]
        [TestCase("USB0::0x0957::0x1A07::MY53000001::INSTR", "USB::2391::6663::MY53000001::0::INSTR")]
        [TestCase("GPIB::22::INSTR", "GPIB0::22::INSTR")]
        public void TwoSpellingsOfOneVisaAddressGiveOneId(string a, string b)
        {
            var x = new FakeScpi { VisaAddress = a };
            var y = new FakeScpi { VisaAddress = b };
            Assert.That(DeviceIdentity.Get(x), Is.EqualTo(DeviceIdentity.Get(y)));
            Assert.That(DeviceIdentity.Get(x), Does.StartWith(typeof(FakeScpi).FullName + "@visa:"));
        }

        [Test]
        public void DifferentAddressesAndDifferentDriversGiveDifferentIds()
        {
            Assert.That(DeviceIdentity.Get(new FakeScpi { VisaAddress = "TCPIP::10.0.0.1::INSTR" }),
                Is.Not.EqualTo(DeviceIdentity.Get(new FakeScpi { VisaAddress = "TCPIP::10.0.0.2::INSTR" })));
            Assert.That(DeviceIdentity.Get(new FakeMeter { Address = "10.0.0.1" }),
                Is.Not.EqualTo(DeviceIdentity.Get(new FakeScpi { VisaAddress = "10.0.0.1" })));
        }

        [Test]
        public void AddressPropertiesAreTrimmedAndLowerCased()
        {
            Assert.That(DeviceIdentity.Get(new FakeMeter { Address = "  Meter-1.LOCAL " }), Is.EqualTo(typeof(FakeMeter).FullName + "@address:meter-1.local"));
        }

        [Test]
        public void ARenamedInstrumentKeepsItsId()
        {
            var meter = new FakeMeter { Name = "DMM", Address = "10.0.0.5" };
            var before = DeviceIdentity.Get(meter);
            meter.Name = "Bench meter";
            Assert.That(DeviceIdentity.Get(meter), Is.EqualTo(before));

            // without an address: the name-based default is stored at the first save and stays after a rename
            var unnamed = new FakeMeter { Name = "Lonely" };
            var loaded = RoundTrip(unnamed);
            loaded.Name = "Renamed";
            Assert.That(DeviceIdentity.Get(loaded), Is.EqualTo(typeof(FakeMeter).FullName + "@name:Lonely"));
        }

        [Test]
        public void TheIdIsSavedWithTheBenchAndAnEditedIdIsKept()
        {
            var meter = new FakeMeter { Name = "DMM", Address = "10.0.0.5" };
            DeviceIdentity.Set(meter, " DMM-7 ");
            var xml = Serialize(meter);
            Assert.That(xml, Does.Contain("<LiveData.DeviceId>DMM-7</LiveData.DeviceId>"));

            var loaded = (FakeMeter)Deserialize(xml);
            Assert.That(DeviceIdentity.IsStored(loaded), Is.True);
            loaded.Address = "10.0.0.6";
            Assert.That(DeviceIdentity.Get(loaded), Is.EqualTo("DMM-7"), "the stored ID stays when the address changes");

            DeviceIdentity.Set(loaded, "");
            Assert.That(DeviceIdentity.Get(loaded), Is.EqualTo(typeof(FakeMeter).FullName + "@address:10.0.0.6"), "clearing returns to the default");
        }

        [Test]
        public void TheDefaultIsWrittenAtTheFirstSaveAndThenStays()
        {
            var meter = new FakeMeter { Name = "DMM", Address = "10.0.0.5" };
            var loaded = RoundTrip(meter);
            Assert.That(DeviceIdentity.IsStored(loaded), Is.True);
            loaded.Address = "10.0.0.99";
            Assert.That(DeviceIdentity.Get(loaded), Is.EqualTo(typeof(FakeMeter).FullName + "@address:10.0.0.5"));
        }

        [Test]
        public void SetIfNotStoredTakesOverAnOlderIdentityOnlyOnce()
        {
            var meter = new FakeMeter { Address = "10.0.0.5" };
            Assert.That(DeviceIdentity.SetIfNotStored(meter, "PSU-1"), Is.True);
            Assert.That(DeviceIdentity.SetIfNotStored(meter, "PSU-2"), Is.False);
            Assert.That(DeviceIdentity.Get(meter), Is.EqualTo("PSU-1"));
        }

        [Test]
        public void PartsGetTheirParentsIdAndTheirSlot()
        {
            var supply = new FakeScpi { VisaAddress = "TCPIP::10.0.0.3::INSTR" };
            var output = new FakeOutput { Supply = supply, Channel = 2 };
            Assert.That(DeviceIdentity.Get(output), Is.EqualTo(DeviceIdentity.Get(supply) + "/2"));
            DeviceIdentity.Set(supply, "PSU-1");
            Assert.That(DeviceIdentity.Get(output), Is.EqualTo("PSU-1/2"));
        }

        [Test]
        public void TheDeviceIdIsAMemberOfEveryInstrumentWithTheKnownIdsAsSuggestions()
        {
            var type = TypeData.GetTypeData(new FakeMeter());
            var member = type.GetMember(DeviceIdTypeData.DeviceIdName);
            Assert.That(member, Is.Not.Null);
            Assert.That(member!.GetAttribute<DisplayAttribute>()!.Name, Is.EqualTo("Device ID"));
            Assert.That(member.GetAttribute<SuggestedValuesAttribute>()!.PropertyName, Is.EqualTo(DeviceIdTypeData.KnownIdsName));
            Assert.That(type.Name, Is.EqualTo(typeof(FakeMeter).FullName), "the persisted type stays the driver's");
            Assert.That(type.GetMembers().Count(m => m.Name == DeviceIdTypeData.DeviceIdName), Is.EqualTo(1));
        }

        [Test]
        public void AHostCanOfferMoreIdsInTheField()
        {
            System.Func<object, System.Collections.Generic.IEnumerable<string>> source = r => r is FakeMeter ? new[] { " PSU-9 ", "" } : throw new System.Exception("broken");
            DeviceIdentity.RegisterSuggestions(source);
            try
            {
                var member = TypeData.GetTypeData(new FakeMeter()).GetMember(DeviceIdTypeData.KnownIdsName)!;
                Assert.That((System.Collections.Generic.IEnumerable<string>)member.GetValue(new FakeMeter())!, Does.Contain("PSU-9"));
                Assert.That(() => DeviceIdentity.KnownIds(new FakeScpi()), Throws.Nothing, "a failing source is skipped");
            }
            finally { DeviceIdentity.UnregisterSuggestions(source); }
            Assert.That(DeviceIdentity.KnownIds(new FakeMeter()), Does.Not.Contain("PSU-9"));
        }

        private static string Serialize(object o)
        {
            using var ms = new MemoryStream();
            new TapSerializer().Serialize(ms, o);
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static object Deserialize(string xml) => new TapSerializer().DeserializeFromString(xml)!;

        private static FakeMeter RoundTrip(FakeMeter m) => (FakeMeter)Deserialize(Serialize(m));
    }
}
