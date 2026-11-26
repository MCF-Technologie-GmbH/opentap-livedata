# OpenTap.LiveData

Attributes for live device data in OpenTAP drivers. MIT licence: use it in open and closed drivers alike.

The package holds only two attributes and references nothing but .NET:

- `[LiveData]` marks a property whose value should be shown live,
- `[DeviceFunction]` marks a method an operator may run from a screen.

A host that shows live data, such as [Tap.X](https://www.mcf-technologie.de), finds the attributes by their full name,
reads the values and shows them live on its screens. Where no such host is installed the attributes do nothing and the
driver works exactly as before.

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

    // asked: read once a second while the instrument is open (never from two threads at once,
    // never while a test uses the connection)
    [LiveData, Display("Temperature"), Unit("°C")]
    public double Temperature => double.Parse(ScpiQuery("SYST:TEMP?"));

    // asked at the driver's own interval
    [LiveData(IntervalSeconds = 10), Display("Errors")]
    public string LastError => ScpiQuery("SYST:ERR?");

    // run by an operator from a screen while no test uses the meter
    [DeviceFunction, Display("Zero")]
    public void Zero() => ScpiCommand("SENS:ZERO:AUTO ONCE");
}
```

A complete example is in [`examples/ExampleMeter`](examples/ExampleMeter/ExampleMeter.cs).

### Rules

- **Pushed values:** a property with a `private set` that raises `PropertyChanged`. Every report is sent, also the
  same value twice (a barcode read twice is two reads).
- **Asked values:** a property without a setter. It is read every `IntervalSeconds` (default 1 s, at least 0.1 s) while
  the device is open, never from two threads at once and never while a test uses the connection, and sent when its value
  changed. An asked value must only ask the device for what it already holds (its last reading, a status, an output
  readback): reading it must never start a measurement, switch a function or range, or change a setting.
- **Name, description, group, order** come from OpenTAP's `[Display]`, the **unit** from `[Unit]`.
- **Types** shown today: numbers (a `TimeSpan` in seconds), `bool`, `string`, `DateTime`/`DateTimeOffset`, enums and
  their nullable forms. Lists, records and curves follow; other types are skipped by the host.
- **Device functions** run only while no test uses the device.

### Without a reference to this package

Hosts recognise the attributes by their full names `OpenTap.LiveData.LiveDataAttribute` and
`OpenTap.LiveData.DeviceFunctionAttribute`. A driver may declare its own (internal) copy with that name instead of
referencing the package:

```csharp
namespace OpenTap.LiveData
{
    [System.AttributeUsage(System.AttributeTargets.Property)]
    internal sealed class LiveDataAttribute : System.Attribute
    {
        public double IntervalSeconds { get; set; } = 1.0;
    }
}
```

### Existing drivers

Properties marked with OpenTAP's `[Metric]` keep working: Tap.X shows them live as single values. Nothing has to be
migrated.

## Build

```
dotnet build OpenTap.LiveData.sln
dotnet build src/OpenTap.LiveData -c Release   # creates the OpenTAP package OpenTap.LiveData.<version>.TapPackage
```
