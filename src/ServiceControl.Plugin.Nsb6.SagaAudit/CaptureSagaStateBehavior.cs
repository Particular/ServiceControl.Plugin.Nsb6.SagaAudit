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
    using NServiceBus.Settings;

    class CaptureSagaStateBehavior : Behavior<IIncomingLogicalMessageContext>
    {
        SagaUpdatedMessage sagaAudit;
        ServiceControlBackend backend;
        EndpointName endpointName;
        readonly SagaAuditSerializer serializer;

        public CaptureSagaStateBehavior(ReadOnlySettings settings, SagaAuditSerializer serializer, ServiceControlBackend backend)
        {
            this.endpointName = settings.EndpointName();
            this.serializer = serializer;
            this.backend = backend;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            sagaAudit = new SagaUpdatedMessage();

            context.Extensions.Set(sagaAudit);

            await next().ConfigureAwait(false);

            ActiveSagaInstance activeSagaInstance;

            if (!context.Extensions.TryGet(out activeSagaInstance))
            {
                return; // Message was not handled by the saga
            }

            await AuditSaga(activeSagaInstance, context).ConfigureAwait(false);
        }

        Task AuditSaga(ActiveSagaInstance activeSagaInstance, IIncomingLogicalMessageContext context)
        {
            string messageId;

            if (!context.MessageHeaders.TryGetValue(Headers.MessageId, out messageId))
            {
                return Task.FromResult(0);
            }

            var saga = activeSagaInstance.Instance;

            var sagaStateString = serializer.Serialize(saga.Entity);

            var messageType = context.Message.MessageType.FullName;
            var headers = context.MessageHeaders;

            sagaAudit.StartTime = activeSagaInstance.Created;
            sagaAudit.FinishTime = activeSagaInstance.Modified;
            sagaAudit.Initiator = BuildSagaChangeInitatorMessage(headers, messageId, messageType);
            sagaAudit.IsNew = activeSagaInstance.IsNew;
            sagaAudit.IsCompleted = saga.Completed;
            sagaAudit.Endpoint = endpointName.ToString();
            sagaAudit.SagaId = saga.Entity.Id;
            sagaAudit.SagaType = saga.GetType().FullName;
            sagaAudit.SagaState = sagaStateString;

            AssignSagaStateChangeCausedByMessage(context, activeSagaInstance);
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

        void AssignSagaStateChangeCausedByMessage(IIncomingLogicalMessageContext context, ActiveSagaInstance sagaInstance)
        {
            string sagaStateChange;

            if (!context.MessageHeaders.TryGetValue("ServiceControl.SagaStateChange", out sagaStateChange))
            {
                sagaStateChange = string.Empty;
            }

            var statechange = "Updated";
            if (sagaInstance.IsNew)
            {
                statechange = "New";
            }
            if (sagaInstance.Instance.Completed)
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