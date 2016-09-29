namespace ServiceControl.Plugin.Nsb6.SagaAudit.AcceptanceTests
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using EndpointPlugin.Messages.SagaState;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTests;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;
    using Plugin.SagaAudit;

    public class When_a_message_is_handled_by_a_saga : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_populate_InvokedSagas_header_for_audits()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<FakeServiceControl>()
                .WithEndpoint<EndpointWithASaga>(b => b.When((messageSession, ctx) =>
                    messageSession.SendLocal(new MessageToBeAudited
                    {
                        Id = ctx.TestRunId
                    })
                ))
                .Done(c => c.MessageAudited)
                .Run();

            string invokedSagasHeaderValue;
            Assert.IsTrue(context.Headers.TryGetValue(SagaAuditHeaders.InvokedSagas, out invokedSagasHeaderValue), "InvokedSagas header is missing");
            Assert.AreEqual($"{typeof(EndpointWithASaga.TheEndpointsSaga).FullName}:{context.SagaId}", invokedSagasHeaderValue);
        }

        class MessageToBeAudited : ICommand
        {
            public Guid Id { get; set; }
        }

        class Context : ScenarioContext
        {
            public bool MessageAudited { get; set; }
            public IReadOnlyDictionary<string, string> Headers { get; set; }
            public Guid SagaId { get; set; }
        }

        class EndpointWithASaga : EndpointConfigurationBuilder
        {
            public EndpointWithASaga()
            {
                var receiverEndpoint = NServiceBus.AcceptanceTesting.Customization.Conventions.EndpointNamingConvention(typeof(FakeServiceControl));

                EndpointSetup<DefaultServer>(c =>
                {
                    c.UseSerialization<JsonSerializer>();
                    c.AuditProcessedMessagesTo(receiverEndpoint);
                });
            }

            public class TheEndpointsSaga : Saga<TheEndpointsSagaData>, IAmStartedByMessages<MessageToBeAudited>
            {
                public Context TestContext { get; set; }

                protected override void ConfigureHowToFindSaga(SagaPropertyMapper<TheEndpointsSagaData> mapper)
                {
                    mapper.ConfigureMapping<MessageToBeAudited>(msg => msg.Id).ToSaga(saga => saga.TestRunId);
                }


                public Task Handle(MessageToBeAudited message, IMessageHandlerContext context)
                {
                    Data.TestRunId = message.Id;
                    TestContext.SagaId = Data.Id;
                    return Task.FromResult(0);
                }
            }

            public class TheEndpointsSagaData : ContainSagaData
            {
                public Guid TestRunId { get; set; }
            }
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

            public class MessageToBeAuditedHandler : IHandleMessages<MessageToBeAudited>
            {
                public Context TestContext { get; set; }

                public Task Handle(MessageToBeAudited message, IMessageHandlerContext context)
                {
                    TestContext.MessageAudited = true;
                    TestContext.Headers = context.MessageHeaders;
                    return Task.FromResult(0);
                }
            }
        }
    }
}
