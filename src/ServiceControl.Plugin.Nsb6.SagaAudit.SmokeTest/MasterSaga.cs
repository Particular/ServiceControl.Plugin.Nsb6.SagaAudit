namespace ServiceControl.Plugin.Nsb6.SagaAudit.SmokeTest
{
    using System;
    using System.Threading.Tasks;
    using NServiceBus;
    using NServiceBus.Logging;

    class MasterSaga : Saga<MasterSagaData>,
        IAmStartedByMessages<StartMaster>,
        IHandleTimeouts<MasterTimedOut>,
        IHandleMessages<WorkRequestedAt>,
        IHandleMessages<ChildFinished>
    {
        static ILog Log = LogManager.GetLogger<MasterSaga>();

        protected override void ConfigureHowToFindSaga(SagaPropertyMapper<MasterSagaData> mapper)
        {
            mapper.ConfigureMapping<StartMaster>(msg => msg.Identifier).ToSaga(saga => saga.Identifier);
            mapper.ConfigureMapping<WorkRequestedAt>(msg => msg.Identifier).ToSaga(saga => saga.Identifier);
            mapper.ConfigureMapping<ChildFinished>(msg => msg.Identifier).ToSaga(saga => saga.Identifier);
        }

        public Task Handle(StartMaster message, IMessageHandlerContext context)
        {
            Data.Identifier = message.Identifier;
            Data.StartedAt = DateTime.UtcNow;

            Log.Info($"Master {Data.Identifier} started.");

            return Task.WhenAll(
                context.SendLocal(new StartChild
                {
                    Identifier = message.Identifier,
                    WorkRequired = message.WorkRequired
                }),
                RequestTimeout<MasterTimedOut>(context, DateTime.UtcNow.AddSeconds(2))
            );
        }

        public Task Timeout(MasterTimedOut state, IMessageHandlerContext context)
        {
            var checkTime = Data.LastWorkRequestedAt ?? Data.StartedAt;

            if (checkTime.AddSeconds(10) > DateTime.UtcNow)
            {
                Log.Warn($"Master Saga {Data.Identifier} Timed Out");
            }

            return RequestTimeout<MasterTimedOut>(context, DateTime.UtcNow.AddSeconds(10));
        }

        public Task Handle(WorkRequestedAt message, IMessageHandlerContext context)
        {
            Data.LastWorkRequestedAt = message.RequestedAt;

            return Task.FromResult(0);
        }

        public Task Handle(ChildFinished message, IMessageHandlerContext context)
        {
            MarkAsComplete();

            Log.Info($"Master {Data.Identifier} completed.");

            return context.Publish(new MasterFinished
            {
                Identifier = Data.Identifier,
                FinishedAt = DateTime.UtcNow
            });
        }
    }
}