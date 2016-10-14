namespace NServiceBus
{
    using Configuration.AdvanceExtensibility;

    public static class SagaPluginExtensions
    {
        /// <summary>
        /// Sets the ServiceControl url.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="serviceControlUrl">ServiceControl url.</param>
        public static void SagaPlugin(this EndpointConfiguration config, string serviceControlUrl)
        {
            config.GetSettings().Set("ServiceControl.Url", serviceControlUrl);
        }
    }
}