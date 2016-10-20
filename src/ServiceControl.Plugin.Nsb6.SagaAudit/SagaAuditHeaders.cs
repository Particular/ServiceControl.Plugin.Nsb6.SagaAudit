namespace ServiceControl.Plugin.SagaAudit
{
    /// <summary>
    /// Headers used by the plugin.
    /// </summary>
    public class SagaAuditHeaders
    {
        /// <summary>
        /// Captures the invoked sagas.
        /// </summary>
        public const string InvokedSagas = "NServiceBus.InvokedSagas";
    }
}