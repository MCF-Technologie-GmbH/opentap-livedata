# OpenTap.LiveData

Live device data for OpenTAP drivers. MIT licence: use it in open and closed drivers alike.

A driver marks the properties that should be shown live with `[LiveData]`. The package

- describes them (name, description, group and order from OpenTAP's `[Display]`, unit from `[Unit]`, JSON type),
- collects every change the driver reports and reads values without a setter at an interval,
- hands one batch per value to a **transport** every 100 ms, as plain JSON (`System.Text.Json`),
- answers description and latest-value requests through the transport,
- gives every instrument and DUT a **Device ID** that is saved with the Bench.

The package depends only on OpenTAP. It has no network client: a host (for example Tap.X) registers an
`ILiveDataTransport`. Without a transport it collects and sends nothing.

## For driver developers

```csharp
using OpenTap;
using OpenTap.LiveData;

public class MyMeter : ScpiInstrument
{
    private double _voltage;

    // pushed: every report is sent, also when the same value comes twice
    [LiveData, Display("Voltage", Group: "Measure"), Unit("V")]
    public double Voltage { get => _voltage; private set { _voltage = value; OnPropertyChanged(nameof(Voltage)); } }

    // asked: read once a second while the instrument is open (never from two threads at once)
    [LiveData, Display("Temperature"), Unit("°C")]
    public double Temperature => double.Parse(ScpiQuery("SYST:TEMP?"));

    // asked at the driver's own interval
    [LiveData(IntervalSeconds = 10), Display("Errors")]
    public string LastError => ScpiQuery("SYST:ERR?");
}
```

Phase 1 types: numbers (a `TimeSpan` in seconds), `bool`, `string`, `DateTime`/`DateTimeOffset`, enums, and their
nullable forms. Other types are skipped with a log warning.

The package recognises the attribute by its full name `OpenTap.LiveData.LiveDataAttribute`, so a driver may declare
its own copy with that name instead of referencing the package.

`[DeviceFunction]` marks methods an operator may run from a screen; it is declared now and carried by later versions.

## Device ID

Every resource gets the setting **Device ID** (member `LiveData.DeviceId`). One ID names one physical device
everywhere: every profile, session and computer.

- Default: the driver type + `@` + the device's identity: `asset:<id>` when the driver reports an OpenTAP asset
  identifier, else `visa:<address>` with the VISA address in a canonical form (so `TCPIP::192.168.1.20::INSTR` and
  `TCPIP0::192.168.1.20::inst0::INSTR` give the same ID), else `address:<value>` from the first text property whose
  name ends in `Address`.
- A part (a resource without an address of its own that points to one other resource and has a `Channel`, `ChannelId`
  or `SerialNumber`) gets its parent's ID + `/` + that value.
- The default is written when the Bench is saved the first time and then stays, also when the instrument is renamed.
  It can be edited; the field offers the IDs of the loaded Bench.
- Two different devices need two IDs, also when one Bench is used on two computers.

## For hosts

```csharp
LiveDataRuntime.RegisterTransport(myTransport);        // required, otherwise nothing is collected
LiveDataRuntime.RegisterPublishGate(myGate);           // optional: only the lock holder publishes
LiveDataRuntime.RegisterContributor(myContributor);    // optional: values of the host's own
LiveDataRuntime.SetHolder(() => "Station 1");          // optional: sent with every payload

LiveDataRuntime.Attach(resource);   // when the resource opened
LiveDataRuntime.Detach(resource);   // when it closed
```

The payloads are described by [`schema/livedata-v1.schema.json`](schema/livedata-v1.schema.json).

## Build and test

```
dotnet build OpenTap.LiveData.sln
dotnet test tests/OpenTap.LiveData.Tests
dotnet build src/OpenTap.LiveData -c Release   # creates the OpenTAP package OpenTap.LiveData.<version>.TapPackage
```
