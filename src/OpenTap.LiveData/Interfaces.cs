using System;

namespace OpenTap.LiveData
{
    /// <summary>
    /// Carries live data out of the process. The package holds no connection: a host registers one transport with
    /// <see cref="LiveDataRuntime.RegisterTransport"/>. Without one the package collects nothing and sends nothing.
    /// </summary>
    /// <remarks>Implementations must not block for long: <see cref="Publish"/> is called from the collector's timer.</remarks>
    public interface ILiveDataTransport
    {
        /// <summary>Sends one batch (all reports of one value of one device in the last 100 ms).</summary>
        void Publish(LiveDataBatch batch);

        /// <summary>Tells watchers that the description of a device changed (they request it again).</summary>
        void PublishDescriptionChanged(string deviceId);

        /// <summary>
        /// Starts answering description and latest-value requests for a device with <paramref name="endpoint"/>. Called
        /// while this process may publish the device; a second call for the same Device ID replaces the endpoint.
        /// </summary>
        void Serve(string deviceId, ILiveDataEndpoint endpoint);

        /// <summary>Stops answering requests for a device (it was closed, or this process may no longer publish it).</summary>
        void Unserve(string deviceId);
    }

    /// <summary>Answers requests for one device; handed to the transport by <see cref="ILiveDataTransport.Serve"/>.</summary>
    public interface ILiveDataEndpoint
    {
        /// <summary>The description of every live value of the device.</summary>
        LiveDataDescription Describe();

        /// <summary>The newest report of every value that has one.</summary>
        LiveDataLatest Latest();
    }

    /// <summary>
    /// Decides whether this process may publish a resource right now. Hosts where several processes can have the same
    /// device open at once (for example a device sharing service that hands out locks on devices and their parts)
    /// register one so only the lock holder publishes and watchers never get a value twice. Without a registered gate
    /// every open resource publishes.
    /// </summary>
    public interface ILiveDataPublishGate
    {
        /// <summary>Whether this process may publish the resource now.</summary>
        bool MayPublish(IResource resource);

        /// <summary>Raised when the answer for a resource may have changed (argument: the resource; null = check all).</summary>
        event Action<IResource?>? Changed;
    }
}
