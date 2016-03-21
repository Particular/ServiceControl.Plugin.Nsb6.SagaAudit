namespace ServiceControl.Features
{
    using System.Threading.Tasks;
    using NServiceBus;
    using NServiceBus.Features;
    using NServiceBus.MessageInterfaces.MessageMapper.Reflection;
    using NServiceBus.Settings;
    using Plugin;
    using Plugin.SagaAudit;

    public class SagaAudit : Feature
    {
        public SagaAudit()
        {
            EnableByDefault();
            DependsOn<Sagas>();
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            context.Container.ConfigureComponent<ServiceControlBackend>(DependencyLifecycle.SingleInstance);

            context.Pipeline.Register("CaptureSagaState", b => b.Build<CaptureSagaStateBehavior>(), "Records saga state changes");
            context.Pipeline.Register("ReportSagaStateChanges", new CaptureSagaResultingMessagesBehavior(), "Reports the saga state changes to ServiceControl");

            context.Container.ConfigureComponent(b => new CaptureSagaStateBehavior(context.Settings.EndpointName(), BuildSerializer(context.Settings), b.Build<ServiceControlBackend>()), DependencyLifecycle.SingleInstance);

            context.RegisterStartupTask(b => new SagaAuditStartupTask(b.Build<ServiceControlBackend>()));
        }

        static CaptureSagaStateSerializer BuildSerializer(ReadOnlySettings settings)
        {
            var definition = new JsonSerializer();

            var factory = definition.Configure(settings);

            var serializer = factory(new MessageMapper());

            return new CaptureSagaStateSerializer(serializer);
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