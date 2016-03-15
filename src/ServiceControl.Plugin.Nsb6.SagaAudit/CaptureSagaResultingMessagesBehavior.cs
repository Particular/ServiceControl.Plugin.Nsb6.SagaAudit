namespace ServiceControl.Plugin.SagaAudit
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using EndpointPlugin.Messages.SagaState;
    using NServiceBus;
    using NServiceBus.DelayedDelivery;
    using NServiceBus.DeliveryConstraints;
    using NServiceBus.Pipeline;
    using NServiceBus.Routing;

    class CaptureSagaResultingMessagesBehavior : Behavior<IOutgoingLogicalMessageContext>
    {
        public override Task Invoke(IOutgoingLogicalMessageContext context, Func<Task> next)
        {
            AppendMessageToState(context);
            return next();
        }

        void AppendMessageToState(IOutgoingLogicalMessageContext context)
        {
            if (!context.Extensions.TryGet(out sagaUpdatedMessage))
            {
                return;
            }
            var logicalMessage = context.Message;
            if (logicalMessage == null)
            {
                //this can happen on control messages
                return;
            }

            TimeSpan? deliveryDelay = null;
            DelayDeliveryWith delayDeliveryWith;
            if (context.Extensions.TryGetDeliveryConstraint(out delayDeliveryWith))
            {
                deliveryDelay = delayDeliveryWith.Delay;
            }

            DateTime? doNotDeliverBefore = null;
            DoNotDeliverBefore notDeliverBefore;
            if (context.Extensions.TryGetDeliveryConstraint(out notDeliverBefore))
            {
                doNotDeliverBefore = notDeliverBefore.At;
            }

            var sagaResultingMessage = new SagaChangeOutput
            {
                ResultingMessageId = context.MessageId,
                TimeSent = DateTimeExtensions.ToUtcDateTime(context.Headers[Headers.TimeSent]),
                MessageType = logicalMessage.MessageType.ToString(),
                DeliveryDelay = deliveryDelay,
                DeliveryAt = doNotDeliverBefore,
                Destination = GetDestinationForUnicastMessages(context),
                Intent = context.Headers[Headers.MessageIntent]
            };
            sagaUpdatedMessage.ResultingMessages.Add(sagaResultingMessage);
        }

        SagaUpdatedMessage sagaUpdatedMessage;

        static string GetDestinationForUnicastMessages(IOutgoingLogicalMessageContext context)
        {
            var sendAddressTags = context.RoutingStrategies.OfType<UnicastRoutingStrategy>().Select(urs => urs.Apply(context.Headers)).Cast<UnicastAddressTag>().ToList();
            return sendAddressTags.Count() != 1 ? null : sendAddressTags.First().Destination;
        }
    }
}