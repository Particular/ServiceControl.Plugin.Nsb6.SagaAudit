namespace ServiceControl.Plugin.Nsb6.CustomChecks.AcceptanceTests
{
    using System;
    using System.Configuration;
    using System.Threading.Tasks;
    using EndpointPlugin.Messages.SagaState;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;

    public class When_saga_changes_state : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_send_result_to_service_control()
        {
            var context = await Scenario.Define<Context>(c => { c.Id = Guid.NewGuid(); })
                .WithEndpoint<FakeServiceControl>()
                .WithEndpoint<Sender>()
                .Done(c => c.WasCalled && c.TimeoutReceived)
                .Run();

            Assert.True(context.WasCalled);

            // TODO: Add more asserts
        }

        public class Context : ScenarioContext
        {
            public bool WasCalled { get; set; }

            public bool TimeoutReceived { get; set; }

            public Guid Id { get; set; }
        }

        class Sender : EndpointConfigurationBuilder
        {
            public Sender()
            {
                var receiverEndpoint = NServiceBus.AcceptanceTesting.Customization.Conventions.EndpointNamingConvention(typeof(FakeServiceControl));
                ConfigurationManager.AppSettings[@"ServiceControl/Queue"] = receiverEndpoint;

                EndpointSetup<DefaultServer>();
            }

            public class MySaga : Saga<MySaga.MySagaData>,
                                        IAmStartedByMessages<StartSaga>,
                                        IHandleTimeouts<MySaga.TimeHasPassed>
            {
                public Context TestContext { get; set; }

                public Task Handle(StartSaga message, IMessageHandlerContext context)
                {
                    Data.DataId = message.DataId;

                    return RequestTimeout(context, TimeSpan.FromMilliseconds(1), new TimeHasPassed());
                }

                public Task Timeout(TimeHasPassed state, IMessageHandlerContext context)
                {
                    MarkAsComplete();

                    TestContext.TimeoutReceived = true;
                    return Task.FromResult(0);
                }

                protected override void ConfigureHowToFindSaga(SagaPropertyMapper<MySagaData> mapper)
                {
                    mapper.ConfigureMapping<StartSaga>(m => m.DataId).ToSaga(s => s.DataId);
                }

                public class MySagaData : ContainSagaData
                {
                    public virtual Guid DataId { get; set; }
                }

                public class TimeHasPassed
                {
                }
            }
        }

        public class StartSaga : IMessage
        {
            public Guid DataId { get; set; }
        }

        class FakeServiceControl : EndpointConfigurationBuilder
        {
            public FakeServiceControl()
            {
                EndpointSetup<DefaultServer>(c => c.UseSerialization<JsonSerializer>());
            }

            public class MyMessageHandler : IHandleMessages<SagaUpdatedMessage>
            {
                public Context TestContext { get; set; }

                public Task Handle(SagaUpdatedMessage message, IMessageHandlerContext context)
                {
                    TestContext.WasCalled = true;
                    return Task.FromResult(0);
                }
            }
        }
    }
}