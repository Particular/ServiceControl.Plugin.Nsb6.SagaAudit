namespace ServiceControl.Plugin.Nsb6.SagaAudit.SmokeTest
{
    using System;
    using NServiceBus;

    class MasterSagaData : ContainSagaData
    {
        public Guid Identifier { get; set; }
        public DateTime? LastWorkRequestedAt { get; set; }
        public DateTime StartedAt { get; set; }
    }
}