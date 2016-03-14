﻿namespace ServiceControl.EndpointPlugin.Messages.SagaState
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;
    using NServiceBus;

    [DataContract]
    class SagaUpdatedMessage:IMessage
    {
        public SagaUpdatedMessage()
        {
            ResultingMessages = new List<SagaChangeOutput>();
        }

        [DataMember]
        public string SagaState { get; set; }
        [DataMember]
        public Guid SagaId { get; set; }
        [DataMember]
        public SagaChangeInitiator Initiator { get; set; }
        [DataMember]
        public List<SagaChangeOutput> ResultingMessages { get; set; }
        [DataMember]
        public string Endpoint { get; set; }
        [DataMember]
        public bool IsNew { get; set; }
        [DataMember]
        public bool IsCompleted { get; set; }
        [DataMember]
        public DateTime StartTime { get; set; }
        [DataMember]
        public DateTime FinishTime { get; set; }
        [DataMember]
        public string SagaType { get; set; }
    }
}
