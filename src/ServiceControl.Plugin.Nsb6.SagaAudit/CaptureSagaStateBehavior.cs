namespace ServiceControl.Plugin.SagaAudit
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using EndpointPlugin.Messages.SagaState;
    using NServiceBus;
    using NServiceBus.Pipeline;
    using NServiceBus.Sagas;
    using NServiceBus.Settings;
    using NServiceBus.Transport;

    class CaptureSagaStateBehavior : Behavior<IInvokeHandlerContext>
    {
        SagaUpdatedMessage sagaAudit;
        ServiceControlBackend backend;
        string endpointName;
        readonly SagaAuditSerializer serializer;

        public CaptureSagaStateBehavior(ReadOnlySettings settings, SagaAuditSerializer serializer, ServiceControlBackend backend)
        {
            endpointName = settings.EndpointName();
            this.serializer = serializer;
            this.backend = backend;
        }

        public class CaptureSagaStateRegistration : RegisterStep
        {
            public CaptureSagaStateRegistration()
                : base("CaptureSagaState", typeof(CaptureSagaStateBehavior), "Records saga state changes")
            {
                InsertBefore("InvokeSaga");
            }
        }

        public override async Task Invoke(IInvokeHandlerContext context, Func<Task> next)
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

        Task AuditSaga(ActiveSagaInstance activeSagaInstance, IInvokeHandlerContext context)
        {
            string messageId;

            if (!context.MessageHeaders.TryGetValue(Headers.MessageId, out messageId))
            {
                return Task.FromResult(0);
            }

            var saga = activeSagaInstance.Instance;

            var sagaStateString = serializer.Serialize(saga.Entity);

            var messageType = context.MessageMetadata.MessageType.FullName;
            var headers = context.MessageHeaders;

            sagaAudit.StartTime = activeSagaInstance.Created;
            sagaAudit.FinishTime = activeSagaInstance.Modified;
            sagaAudit.Initiator = BuildSagaChangeInitiatorMessage(headers, messageId, messageType);
            sagaAudit.IsNew = activeSagaInstance.IsNew;
            sagaAudit.IsCompleted = saga.Completed;
            sagaAudit.Endpoint = endpointName;
            sagaAudit.SagaId = saga.Entity.Id;
            sagaAudit.SagaType = saga.GetType().FullName;
            sagaAudit.SagaState = sagaStateString;

            AssignSagaStateChangeCausedByMessage(context, activeSagaInstance);

            var transportTransaction = context.Extensions.Get<TransportTransaction>();
            return backend.Send(sagaAudit, transportTransaction);
        }

        public SagaChangeInitiator BuildSagaChangeInitiatorMessage(IReadOnlyDictionary<string, string> headers, string messageId, string messageType)
        {
            string originatingMachine;
            headers.TryGetValue(Headers.OriginatingMachine, out originatingMachine);

            string originatingEndpoint;
            headers.TryGetValue(Headers.OriginatingEndpoint, out originatingEndpoint);

            string timeSent;
            var timeSentConvertedToUtc = headers.TryGetValue(Headers.TimeSent, out timeSent) ?
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
                TimeSent = timeSentConvertedToUtc,
                Intent = intent
            };
        }

        void AssignSagaStateChangeCausedByMessage(IInvokeHandlerContext context, ActiveSagaInstance sagaInstance)
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