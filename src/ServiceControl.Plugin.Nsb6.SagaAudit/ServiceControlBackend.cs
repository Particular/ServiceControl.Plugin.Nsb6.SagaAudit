namespace ServiceControl.Plugin
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using EndpointPlugin.Messages.SagaState;
    using NServiceBus;
    using NServiceBus.DeliveryConstraints;
    using NServiceBus.Extensibility;
    using NServiceBus.Performance.TimeToBeReceived;
    using NServiceBus.Routing;
    using NServiceBus.Settings;
    using NServiceBus.Support;
    using NServiceBus.Transport;
    using NServiceBus.Unicast.Transport;
    using SagaAudit;

    class ServiceControlBackend
    {
        public ServiceControlBackend(IDispatchMessages messageSender, SagaAuditSerializer serializer, ReadOnlySettings settings, CriticalError criticalError)
        {
            this.settings = settings;
            this.criticalError = criticalError;
            this.messageSender = messageSender;
            this.serializer = serializer;

            serviceControlBackendAddress = GetServiceControlAddress();

            circuitBreaker =
                new RepeatedFailuresOverTimeCircuitBreaker("ServiceControlConnectivity", TimeSpan.FromMinutes(2),
                    ex =>
                        criticalError.Raise(
                            "This endpoint is repeatedly unable to contact the ServiceControl backend to report endpoint information. You have the ServiceControl plugins installed in your endpoint. However, please ensure that the Particular ServiceControl service is installed on this machine, " +
                            "or if running ServiceControl on a different machine, then ensure that your endpoint's app.config / web.config, AppSettings has the following key set appropriately: ServiceControl/Queue. \r\n" +
                            @"For example: <add key=""ServiceControl/Queue"" value=""particular.servicecontrol@machine""/>" +
                            "\r\n", ex));
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
                [Headers.MessageIntent] = MessageIntentEnum.Send.ToString()
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
            var toReplace = ", " + typeof(SagaUpdatedMessage).Assembly.GetName().Name;

            bodyString = bodyString.Replace(toReplace, ", ServiceControl");

            return Encoding.UTF8.GetBytes(bodyString);
        }

        public Task Send(SagaUpdatedMessage messageToSend, TransportTransaction transportTransaction)
        {
            return Send(messageToSend, TimeSpan.MaxValue, transportTransaction);
        }

        string GetServiceControlAddress()
        {
            var queueName = ConfigurationManager.AppSettings[@"ServiceControl/Queue"];
            if (!string.IsNullOrEmpty(queueName))
            {
                return queueName;
            }

            string errorAddress;
            if (TryGetErrorQueueAddress(out errorAddress))
            {
                var qm = Parse(errorAddress);
                return "Particular.ServiceControl" + "@" + qm.Item2;
            }

            string auditAddress;
            if (settings.TryGetAuditQueueAddress(out auditAddress))
            {
                var qm = Parse(auditAddress);
                return "Particular.ServiceControl" + "@" + qm.Item2;
            }

            return null;
        }

        bool TryGetErrorQueueAddress(out string address)
        {
            try
            {
                address = settings.ErrorQueueAddress();
                return true;
            }
            catch
            {
                address = null;
                return false;
            }
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
                const string errMsg = "This endpoint is unable to contact the ServiceControl Backend to report endpoint information. You have the ServiceControl plugins installed in your endpoint. However, please ensure that the Particular ServiceControl service is installed on this machine, " +
                                      "or if running ServiceControl on a different machine, then ensure that your endpoint's app.config / web.config, AppSettings has the following key set appropriately: ServiceControl/Queue. \r\n" +
                                      @"For example: <add key=""ServiceControl/Queue"" value=""particular.servicecontrol@machine""/>" +
                                      "\r\n Additional details: {0}";
                criticalError.Raise(errMsg, ex);
            }
        }

        static Tuple<string, string> Parse(string destination)
        {
            if (string.IsNullOrEmpty(destination))
            {
                throw new ArgumentException("Invalid destination address specified", nameof(destination));
            }

            var arr = destination.Split('@');

            var queue = arr[0];
            var machine = RuntimeEnvironment.MachineName;

            if (string.IsNullOrWhiteSpace(queue))
            {
                throw new ArgumentException("Invalid destination address specified", nameof(destination));
            }

            if (arr.Length == 2)
                if (arr[1] != "." && arr[1].ToLower() != "localhost" && arr[1] != IPAddress.Loopback.ToString())
                    machine = arr[1];

            return new Tuple<string, string>(queue, machine);
        }

        RepeatedFailuresOverTimeCircuitBreaker circuitBreaker;
        CriticalError criticalError;
        IDispatchMessages messageSender;

        SagaAuditSerializer serializer;
        string serviceControlBackendAddress;
        ReadOnlySettings settings;
    }
}