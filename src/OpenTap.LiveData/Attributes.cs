using System;

namespace OpenTap.LiveData
{
    /// <summary>
    /// Marks a property of an instrument or DUT as live data: its value is shown live wherever the device is watched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A property with a <c>private set</c> that raises <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>
    /// is <b>pushed</b>: every change the driver reports is sent, also when the same value is reported twice (a barcode read
    /// twice is two reads). A property without a setter is <b>asked</b>: it is read every <see cref="IntervalSeconds"/>
    /// while the device is open, never from two threads at once, and sent when its value changed.
    /// </para>
    /// <para>
    /// Name, description, group and order come from OpenTAP's <c>[Display]</c>, the unit from <c>[Unit]</c>. The package
    /// recognises the attribute by its full name <c>OpenTap.LiveData.LiveDataAttribute</c>, so a driver may also declare
    /// its own copy with that name instead of referencing this package.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class LiveDataAttribute : Attribute
    {
        /// <summary>The default time between two reads of an asked value, in seconds.</summary>
        public const double DefaultIntervalSeconds = 1.0;

        /// <summary>
        /// Time between two reads of an asked value (a property without a setter), in seconds. Ignored for pushed values.
        /// Values below 0.1 s are read every 0.1 s.
        /// </summary>
        public double IntervalSeconds { get; set; } = DefaultIntervalSeconds;
    }

    /// <summary>
    /// Marks a method of an instrument or DUT as a device function an operator may run from a screen while no test uses
    /// the device. Declared now; calls through the transport follow in a later version of the package.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class DeviceFunctionAttribute : Attribute
    {
        /// <summary>The function's name on the transport; the method name when empty.</summary>
        public string? Name { get; set; }
    }
}
