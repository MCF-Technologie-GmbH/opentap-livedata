using System;
using OpenTap;

namespace OpenTap.LiveData
{
    /// <summary>The driver's own copy of the attribute (same full name, no reference to the package; internal so it never clashes).</summary>
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class LiveDataAttribute : Attribute
    {
        public double IntervalSeconds { get; set; } = 1.0;
    }
}

namespace OwnAttributeDriver
{
    [Display("Own attribute meter")]
    public class OwnAttributeMeter : Instrument
    {
        [OpenTap.LiveData.LiveData(IntervalSeconds = 2.5)]
        [Unit("V")]
        public double Voltage => 1.5;
    }
}
