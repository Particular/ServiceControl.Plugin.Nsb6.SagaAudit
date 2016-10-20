﻿namespace ServiceControl.Features
{
    using System.Threading.Tasks;
    using NServiceBus;
    using NServiceBus.Features;
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
            context.Container.ConfigureComponent<SagaAuditSerializer>(DependencyLifecycle.SingleInstance);

            context.Container.ConfigureComponent<ServiceControlBackend>(DependencyLifecycle.SingleInstance);
            context.Container.ConfigureComponent<CaptureSagaStateBehavior>(DependencyLifecycle.SingleInstance);

            context.Pipeline.Register(new CaptureSagaStateBehavior.CaptureSagaStateRegistration());

            context.Pipeline.Register("ReportSagaStateChanges", new CaptureSagaResultingMessagesBehavior(), "Reports the saga state changes to ServiceControl");

            context.Pipeline.Register("AuditInvokedSaga", new AuditInvokedSagaBehavior(), "Adds audit saga information");

            context.RegisterStartupTask(b => new SagaAuditStartupTask(b.Build<ServiceControlBackend>()));
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