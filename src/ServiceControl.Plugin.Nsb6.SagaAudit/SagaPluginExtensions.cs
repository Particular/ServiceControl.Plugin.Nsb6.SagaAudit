namespace NServiceBus
{
    using Configuration.AdvanceExtensibility;

    public static class SagaPluginExtensions
    {
        /// <summary>
        /// Sets the ServiceControl queue address.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="serviceControlQueue">ServiceControl queue address.</param>
        public static void SagaPlugin(this EndpointConfiguration config, string serviceControlQueue)
        {
            config.GetSettings().Set("ServiceControl.Queue", serviceControlQueue);
        }
    }
}