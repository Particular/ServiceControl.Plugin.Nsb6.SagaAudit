namespace ServiceControl.Features
{
    using System.Threading.Tasks;
    using NServiceBus;    
    using NServiceBus.Features;
    using NServiceBus.Logging;
    using Plugin;
    using Plugin.SagaAudit;

    /// <summary>
    /// The ServiceControl SagaAudit plugin.
    /// </summary>
    public class SagaAudit : Feature
    {
    	static ILog Log = LogManager.GetLogger<SagaAudit>();

        internal SagaAudit()
        {
            EnableByDefault();
            DependsOn<Sagas>();
        }

        /// <summary>Called when the features is activated.</summary>
        protected override void Setup(FeatureConfigurationContext context)
        {
            context.Container.ConfigureComponent<SagaAuditSerializer>(DependencyLifecycle.SingleInstance);

            context.Container.ConfigureComponent<ServiceControlBackend>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<CaptureSagaStateBehavior>(DependencyLifecycle.SingleInstance);

            context.Pipeline.Register(new CaptureSagaStateBehavior.CaptureSagaStateRegistration());

            context.Pipeline.Register("ReportSagaStateChanges", new CaptureSagaResultingMessagesBehavior(), "Reports the saga state changes to ServiceControl");

            context.Pipeline.Register("AuditInvokedSaga", new AuditInvokedSagaBehavior(), "Adds audit saga information");

            context.RegisterStartupTask(b => new SagaAuditStartupTask(b.Build<ServiceControlBackend>()));

            Log.Warn("The ServiceControl.Plugin.Nsb6.SagaAudit package has been replaced by the NServiceBus.SagaAudit package. See the upgrade guide for more details.");
        }

        class SagaAuditStartupTask : FeatureStartupTask
        {
            ServiceControlBackend serviceControlBackend;

            public SagaAuditStartupTask(ServiceControlBackend backend)
            {
                serviceControlBackend = backend;
            }

            protected override Task OnStart(IMessageSession session)
            {
                return serviceControlBackend.VerifyIfServiceControlQueueExists();
            }

            protected override Task OnStop(IMessageSession session)
            {
                return Task.FromResult(0);
            }
        }
    }
}