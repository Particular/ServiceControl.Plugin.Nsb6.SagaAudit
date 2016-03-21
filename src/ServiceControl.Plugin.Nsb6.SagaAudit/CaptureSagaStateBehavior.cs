namespace ServiceControl.Plugin.SagaAudit
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using EndpointPlugin.Messages.SagaState;
    using NServiceBus;
    using NServiceBus.Pipeline;
    using NServiceBus.Routing;
    using NServiceBus.Sagas;

    class CaptureSagaStateBehavior : Behavior<IInvokeHandlerContext>
    {
        SagaUpdatedMessage sagaAudit;
        ServiceControlBackend backend;
        EndpointName endpointName;
        readonly CaptureSagaStateSerializer serializer;

        public CaptureSagaStateBehavior(EndpointName endpointName, CaptureSagaStateSerializer serializer, ServiceControlBackend backend)
        {
            this.endpointName = endpointName;
            this.serializer = serializer;
            this.backend = backend;
        }

        public override async Task Invoke(IInvokeHandlerContext context, Func<Task> next)
        {
            var saga = context.MessageHandler.Instance as Saga;

            if (saga == null)
            {
                await next().ConfigureAwait(false);
            }

            sagaAudit = new SagaUpdatedMessage
            {
                StartTime = DateTime.UtcNow
            };
            context.Extensions.Set(sagaAudit);

            await next().ConfigureAwait(false);

            if (saga.Entity == null)
            {
                return; // Message was not handled by the saga
            }

            sagaAudit.FinishTime = DateTime.UtcNow;
            await AuditSaga(saga, context).ConfigureAwait(false);
        }

        Task AuditSaga(Saga saga, IInvokeHandlerContext context)
        {
            var messageId = context.MessageId;

            var activeSagaInstance = context.Extensions.Get<ActiveSagaInstance>();

            var sagaStateString = serializer.Serialize(saga.Entity);

            var messageType = context.MessageMetadata.MessageType.FullName;
            var headers = context.MessageHeaders;

            sagaAudit.Initiator = BuildSagaChangeInitatorMessage(headers, messageId, messageType);
            sagaAudit.IsNew = activeSagaInstance.IsNew;
            sagaAudit.IsCompleted = saga.Completed;
            sagaAudit.Endpoint = endpointName.ToString();
            sagaAudit.SagaId = saga.Entity.Id;
            sagaAudit.SagaType = saga.GetType().FullName;
            sagaAudit.SagaState = sagaStateString;

            AssignSagaStateChangeCausedByMessage(context);
            return backend.Send(sagaAudit);
        }

        public SagaChangeInitiator BuildSagaChangeInitatorMessage(IReadOnlyDictionary<string, string> headers, string messageId, string messageType)
        {

            string originatingMachine;
            headers.TryGetValue(Headers.OriginatingMachine, out originatingMachine);

            string originatingEndpoint;
            headers.TryGetValue(Headers.OriginatingEndpoint, out originatingEndpoint);

            string timeSent;
            var timeSentConveredToUtc = headers.TryGetValue(Headers.TimeSent, out timeSent) ?
                DateTimeExtensions.ToUtcDateTime(timeSent) :
                DateTime.MinValue;

            string messageIntent;
            var intent = headers.TryGetValue(Headers.MessageIntent, out messageIntent) ? messageIntent : "Send"; // Just in case the received message is from an early version that does not have intent, should be a rare occasion.

            string isTimeout;
            var isTimeoutMessage = headers.TryGetValue(Headers.IsSagaTimeoutMessage, out isTimeout) && isTimeout.ToLowerInvariant() == "true";

            return new SagaChangeInitiator
            {
                IsSagaTimeoutMessage = isTimeoutMessage,
                InitiatingMessageId = messageId,
                OriginatingMachine = originatingMachine,
                OriginatingEndpoint = originatingEndpoint,
                MessageType = messageType,
                TimeSent = timeSentConveredToUtc,
                Intent = intent
            };
        }

        void AssignSagaStateChangeCausedByMessage(IInvokeHandlerContext context)
        {
            string sagaStateChange;

            if (!context.MessageHeaders.TryGetValue("ServiceControl.SagaStateChange", out sagaStateChange))
            {
                sagaStateChange = string.Empty;
            }

            var statechange = "Updated";
            if (sagaAudit.IsNew)
            {
                statechange = "New";
            }
            if (sagaAudit.IsCompleted)
            {
                statechange = "Completed";
            }

            if (!string.IsNullOrEmpty(sagaStateChange))
            {
                sagaStateChange += ";";
            }
            sagaStateChange += $"{sagaAudit.SagaId}:{statechange}";

            context.Headers["ServiceControl.SagaStateChange"] = sagaStateChange;
        }
    }
}