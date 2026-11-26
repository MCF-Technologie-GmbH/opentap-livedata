using System;
using System.Globalization;
using OpenTap;
using OpenTap.LiveData;

namespace ExampleDrivers
{
    /// <summary>
    /// A SCPI meter that shows its values live: one pushed value, two asked values and one device function.
    /// </summary>
    [Display("Example meter", Group: "Examples", Description: "Shows how a driver marks live data with OpenTap.LiveData.")]
    public class ExampleMeter : ScpiInstrument
    {
        private double _voltage;
        private string _lastBarcode = "";

        /// <summary>Pushed: the driver reports every new reading; every report is sent, also the same value twice.</summary>
        [LiveData, Display("Voltage", Group: "Measure"), Unit("V")]
        public double Voltage { get => _voltage; private set { _voltage = value; OnPropertyChanged(nameof(Voltage)); } }

        /// <summary>Pushed text: a barcode read twice is two reads.</summary>
        [LiveData, Display("Last barcode")]
        public string LastBarcode { get => _lastBarcode; private set { _lastBarcode = value; OnPropertyChanged(nameof(LastBarcode)); } }

        /// <summary>Asked once a second while the meter is open; never while a test uses the connection.</summary>
        [LiveData, Display("Temperature", Group: "Status"), Unit("°C")]
        public double Temperature => double.Parse(ScpiQuery("SYST:TEMP?"), CultureInfo.InvariantCulture);

        /// <summary>Asked at the driver's own interval: the device's last reading, without measuring again.</summary>
        [LiveData(IntervalSeconds = 10), Display("Last error", Group: "Status")]
        public string LastError => ScpiQuery("SYST:ERR?").Trim();

        /// <summary>A function an operator may run from a screen while no test uses the meter.</summary>
        [DeviceFunction, Display("Zero")]
        public void Zero() => ScpiCommand("SENS:ZERO:AUTO ONCE");

        /// <summary>The driver's own measurement, called from its test steps.</summary>
        public double MeasureVoltage()
        {
            Voltage = double.Parse(ScpiQuery("MEAS:VOLT:DC?"), CultureInfo.InvariantCulture);
            return Voltage;
        }

        /// <summary>Called by the driver when its scanner reports a code.</summary>
        public void ReportBarcode(string code) => LastBarcode = code ?? "";
    }
}
