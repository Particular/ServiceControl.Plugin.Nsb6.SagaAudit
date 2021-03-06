﻿namespace ServiceControl.Plugin
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Text;
    using System.Threading.Tasks;
    using EndpointPlugin.Messages.SagaState;
    using NServiceBus;
    using NServiceBus.DeliveryConstraints;
    using NServiceBus.Extensibility;
    using NServiceBus.Performance.TimeToBeReceived;
    using NServiceBus.Routing;
    using NServiceBus.Settings;
    using NServiceBus.Transport;
    using NServiceBus.Unicast.Transport;
    using SagaAudit;

    class ServiceControlBackend
    {
        public ServiceControlBackend(IDispatchMessages messageSender, SagaAuditSerializer serializer, ReadOnlySettings settings, CriticalError criticalError)
        {
            this.settings = settings;
            this.messageSender = messageSender;
            this.serializer = serializer;

            serviceControlBackendAddress = GetServiceControlAddress();

            circuitBreaker =
                new RepeatedFailuresOverTimeCircuitBreaker("ServiceControlConnectivity", TimeSpan.FromMinutes(2),
                    ex =>
                        criticalError.Raise(
                            "You have ServiceControl plugins installed in your endpoint, however, this endpoint is repeatedly unable to contact the ServiceControl backend to report endpoint information.", ex));
        }

        async Task Send(object messageToSend, TimeSpan timeToBeReceived, TransportTransaction transportTransaction)
        {
            var bodyString = serializer.Serialize(messageToSend);

            var body = ReplaceTypeToken(bodyString);


            var headers = new Dictionary<string, string>
            {
                [Headers.EnclosedMessageTypes] = messageToSend.GetType().FullName,
                [Headers.ContentType] = ContentTypes.Json, //Needed for ActiveMQ transport
                [Headers.ReplyToAddress] = settings.LocalAddress(),
                [Headers.MessageIntent] = sendIntent
            };

            try
            {
                var outgoingMessage = new OutgoingMessage(Guid.NewGuid().ToString(), headers, body);
                var operation = new TransportOperation(outgoingMessage, new UnicastAddressTag(serviceControlBackendAddress), deliveryConstraints: new List<DeliveryConstraint> { new DiscardIfNotReceivedBefore(timeToBeReceived) });
                await messageSender.Dispatch(new TransportOperations(operation), transportTransaction, new ContextBag()).ConfigureAwait(false);
                circuitBreaker.Success();
            }
            catch (Exception ex)
            {
                await circuitBreaker.Failure(ex).ConfigureAwait(false);
            }
        }

        static byte[] ReplaceTypeToken(string bodyString)
        {
            var toReplace = $", {typeof(SagaUpdatedMessage).Assembly.GetName().Name}";

            bodyString = bodyString.Replace(toReplace, ", ServiceControl");

            return Encoding.UTF8.GetBytes(bodyString);
        }

        public Task Send(SagaUpdatedMessage messageToSend, TransportTransaction transportTransaction)
        {
            return Send(messageToSend, TimeSpan.MaxValue, transportTransaction);
        }

        string GetServiceControlAddress()
        {
            var queueName = ConfigurationManager.AppSettings["ServiceControl/Queue"];

            if (!string.IsNullOrEmpty(queueName))
            {
                return queueName;
            }

            if (settings.HasSetting("ServiceControl.Queue"))
            {
                queueName = settings.Get<string>("ServiceControl.Queue");
            }

            if (!string.IsNullOrEmpty(queueName))
            {
                return queueName;
            }

            const string errMsg = @"You have ServiceControl plugins installed in your endpoint, however, the Particular ServiceControl queue is not specified.
Please ensure that the Particular ServiceControl queue is specified either via code (config.SagaPlugin(servicecontrolQueue)) or AppSettings (eg. <add key=""ServiceControl/Queue"" value=""particular.servicecontrol@machine""/>).";

            throw new Exception(errMsg);
        }

        public async Task VerifyIfServiceControlQueueExists()
        {
            try
            {
                // In order to verify if the queue exists, we are sending a control message to SC.
                // If we are unable to send a message because the queue doesn't exist, then we can fail fast.
                // We currently don't have a way to check if Queue exists in a transport agnostic way,
                // hence the send.
                var outgoingMessage = ControlMessageFactory.Create(MessageIntentEnum.Send);
                outgoingMessage.Headers[Headers.ReplyToAddress] = settings.LocalAddress();
                var operation = new TransportOperation(outgoingMessage, new UnicastAddressTag(serviceControlBackendAddress));
                await messageSender.Dispatch(new TransportOperations(operation), new TransportTransaction(), new ContextBag()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                const string errMsg = @"You have ServiceControl plugins installed in your endpoint, however, this endpoint is unable to contact the ServiceControl Backend to report endpoint information.
Please ensure that the Particular ServiceControl queue specified is correct.";

                throw new Exception(errMsg, ex);
            }
        }

        RepeatedFailuresOverTimeCircuitBreaker circuitBreaker;
        IDispatchMessages messageSender;

        SagaAuditSerializer serializer;
        string serviceControlBackendAddress;
        ReadOnlySettings settings;

        readonly string sendIntent = MessageIntentEnum.Send.ToString();
    }
}