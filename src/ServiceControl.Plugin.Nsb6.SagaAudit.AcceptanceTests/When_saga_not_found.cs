namespace ServiceControl.Plugin.Nsb6.SagaAudit.AcceptanceTests
{
    using System;
    using System.Threading.Tasks;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NServiceBus.Sagas;
    using NUnit.Framework;

    public class When_saga_not_found : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_skip_auditing()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<EndpointWithASaga>(b => b.When((messageSession, ctx) =>
                        messageSession.SendLocal(new MessageToBeAudited())
                ))
                .Done(c => c.Done)
                .Run();

            Assert.IsTrue(context.Done);
        }

        class MessageToBeAudited : ICommand
        {
            public Guid MessageId { get; set; }
        }

        class NotSent : ICommand
        {
            public Guid MessageId { get; set; }
        }

        class EndpointWithASaga : EndpointConfigurationBuilder
        {
            public EndpointWithASaga()
            {
                EndpointSetup<DefaultServer>();
            }

            public class NotStartableSaga : Saga<NotStartableSaga.MyData>, IAmStartedByMessages<NotSent>, IHandleMessages<MessageToBeAudited>
            {
                public Task Handle(NotSent message, IMessageHandlerContext context)
                {
                    throw new NotImplementedException();
                }

                public Task Handle(MessageToBeAudited message, IMessageHandlerContext context)
                {
                    return Task.FromResult(0);
                }

                public class MyData : ContainSagaData
                {
                    public virtual Guid MessageId { get; set; }
                }

                protected override void ConfigureHowToFindSaga(SagaPropertyMapper<MyData> mapper)
                {
                    mapper.ConfigureMapping<NotSent>(message => message.MessageId).ToSaga(saga => saga.MessageId);
                    mapper.ConfigureMapping<MessageToBeAudited>(message => message.MessageId).ToSaga(saga => saga.MessageId);
                }
            }

            public class SagaNotFound : IHandleSagaNotFound
            {
                public Context TestContext { get; set; }

                public Task Handle(object message, IMessageProcessingContext context)
                {
                    TestContext.Done = true;
                    return Task.FromResult(0);
                }
            }
        }
        
        class Context : ScenarioContext
        {
            public bool Done { get; set; }
        }
    }
}