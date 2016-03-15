namespace ServiceControl.Features
{
    using NServiceBus;
    using NServiceBus.Features;
    using NServiceBus.MessageInterfaces.MessageMapper.Reflection;
    using NServiceBus.Pipeline;
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

            context.Pipeline.Register<CaptureSagaStateRegistration>();
            context.Pipeline.Register<CaptureSagaResultingMessageRegistration>();

            context.Container.RegisterSingleton(BuildSerializer(context.Settings));
        }

        static CaptureSagaStateSerializer BuildSerializer(ReadOnlySettings settings)
        {
            var definition = new JsonSerializer();

            var factory = definition.Configure(settings);

            var serializer = factory(new MessageMapper());

            return new CaptureSagaStateSerializer(serializer);
        }

        class CaptureSagaStateRegistration : RegisterStep
        {
            public CaptureSagaStateRegistration()
                : base("CaptureSagaState", typeof(CaptureSagaStateBehavior), "Records saga state changes")
            {
                InsertBefore(WellKnownStep.InvokeSaga);
            }
        }

        class CaptureSagaResultingMessageRegistration : RegisterStep
        {
            public CaptureSagaResultingMessageRegistration()
                : base("ReportSagaStateChanges", typeof(CaptureSagaResultingMessagesBehavior), "Reports the saga state changes to ServiceControl")
            {
                InsertAfter(WellKnownStep.InvokeSaga);
            }
        }
    }
}