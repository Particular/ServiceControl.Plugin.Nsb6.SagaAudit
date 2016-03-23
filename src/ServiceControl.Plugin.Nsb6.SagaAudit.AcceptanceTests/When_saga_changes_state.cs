namespace ServiceControl.Plugin.Nsb6.SagaAudit.AcceptanceTests
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Linq;
    using System.Threading.Tasks;
    using EndpointPlugin.Messages.SagaState;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NServiceBus.Features;
    using NUnit.Framework;

    public class When_saga_changes_state : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_send_result_to_service_control()
        {
            var contextId = Guid.NewGuid();
            var context = await Scenario.Define<Context>(c => { c.Id = contextId; })
                .WithEndpoint<Sender>(b => b.When(session => session.SendLocal(new StartSaga
                {
                    DataId = contextId
                })))
                .WithEndpoint<FakeServiceControl>()
                .Done(c => c.MessagesReceived.Count == 2)
                .Run();

            //Process Asserts
            Assert.True(context.WasStarted);
            Assert.True(context.TimeoutReceived);

            //SagaUpdateMessage Asserts
            Assert.IsNotNullOrEmpty(context.MessagesReceived.First().SagaState, "SagaState is not set");
            Assert.AreNotEqual(context.MessagesReceived.First().SagaId, Guid.Empty, "SagaId is not set");
            Assert.AreEqual(context.MessagesReceived.First().Endpoint, "SagaChangesState.Sender", "Endpoint name is not set or incorrect");
            Assert.True(context.MessagesReceived.First().IsNew, "First message is not marked new");
            Assert.False(context.MessagesReceived.Last().IsNew, "Last message is marked new");
            Assert.False(context.MessagesReceived.First().IsCompleted, "First message is marked completed");
            Assert.True(context.MessagesReceived.Last().IsCompleted, "Last Message is not marked completed");
            Assert.Greater(context.MessagesReceived.First().StartTime, DateTime.MinValue, "StartTime is not set");
            Assert.Greater(context.MessagesReceived.First().FinishTime, DateTime.MinValue, "FinishTime is not set");
            Assert.AreEqual(context.MessagesReceived.First().SagaType, "ServiceControl.Plugin.Nsb6.SagaAudit.AcceptanceTests.When_saga_changes_state+Sender+MySaga", "SagaType is not set or incorrect");

            //SagaUpdateMessage.Initiator Asserts
            Assert.True(context.MessagesReceived.Last().Initiator.IsSagaTimeoutMessage, "Last message initiator is not a timeout");
            Assert.IsNotNull(context.MessagesReceived.First().Initiator,"Initiator has not been set");
            Assert.IsNotNullOrEmpty(context.MessagesReceived.First().Initiator.InitiatingMessageId, "Initiator.InitiatingMessageId has not been set");
            Assert.IsNotNullOrEmpty(context.MessagesReceived.First().Initiator.OriginatingMachine, "Initiator.OriginatingMachine has not been set");
            Assert.IsNotNullOrEmpty(context.MessagesReceived.First().Initiator.OriginatingEndpoint, "Initiator.OriginatingEndpoint has not been set");
            Assert.AreEqual(context.MessagesReceived.First().Initiator.MessageType, "ServiceControl.Plugin.Nsb6.SagaAudit.AcceptanceTests.When_saga_changes_state+StartSaga", "First message initiator MessageType is incorrect");
            Assert.IsNotNull(context.MessagesReceived.First().Initiator.TimeSent, "Initiator.TimeSent has not been set");

            //SagaUpdateMessages.ResultingMessages Asserts
            Assert.AreEqual(context.MessagesReceived.First().ResultingMessages.First().MessageType, "ServiceControl.Plugin.Nsb6.SagaAudit.AcceptanceTests.When_saga_changes_state+Sender+MySaga+TimeHasPassed", "ResultingMessage.MessageType is not set or incorrect");
            Assert.Greater(context.MessagesReceived.First().ResultingMessages.First().TimeSent, DateTime.MinValue, "ResultingMessage.TimeSent has not been set");
            //Assert.IsNotNull(context.MessagesReceived.First().ResultingMessages.First().DeliveryAt, "ResultingMessage.DeliveryAt has not been set");
            //Assert.IsNotNull(context.MessagesReceived.First().ResultingMessages.First().DeliveryDelay, "ResultingMessage.DeliveryDelay has not been set");
            Assert.IsNotNullOrEmpty(context.MessagesReceived.First().ResultingMessages.First().Destination, "ResultingMessage.Destination has not been set");
            Assert.IsNotNullOrEmpty(context.MessagesReceived.First().ResultingMessages.First().ResultingMessageId, "ResultingMessage.ResultingMessageId has not been not set");
            Assert.IsNotNullOrEmpty(context.MessagesReceived.First().ResultingMessages.First().Intent,"ResultingMessage.Intent has not been set");
        }

        public class Context : ScenarioContext
        {
            public Guid Id { get; set; }

            internal List<SagaUpdatedMessage> MessagesReceived { get; } = new List<SagaUpdatedMessage>();
            public bool WasStarted { get; set; }
            public bool TimeoutReceived { get; set; }
        }

        public class Sender : EndpointConfigurationBuilder
        {
            public Sender()
            {
                var receiverEndpoint = NServiceBus.AcceptanceTesting.Customization.Conventions.EndpointNamingConvention(typeof(FakeServiceControl));
                ConfigurationManager.AppSettings[@"ServiceControl/Queue"] = receiverEndpoint;

                EndpointSetup<DefaultServer>(config => config.EnableFeature<TimeoutManager>());
            }

            public class MySaga : Saga<MySaga.MySagaData>,
                                        IAmStartedByMessages<StartSaga>,
                                        IHandleTimeouts<MySaga.TimeHasPassed>
            {
                public Context TestContext { get; set; }

                public Task Handle(StartSaga message, IMessageHandlerContext context)
                {
                    TestContext.WasStarted = true;
                    Data.DataId = message.DataId;

                    Console.WriteLine("Handled");

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
                IncludeType<SagaUpdatedMessage>();

                EndpointSetup<DefaultServer>(c =>
                {
                    c.UseSerialization<JsonSerializer>();
                });
            }

            public class SagaUpdatedMessageHandler : IHandleMessages<SagaUpdatedMessage>
            {
                public Context TestContext { get; set; }

                public Task Handle(SagaUpdatedMessage message, IMessageHandlerContext context)
                {
                    TestContext.MessagesReceived.Add(message);
                    return Task.FromResult(0);
                }
            }
        }
    }
}