namespace ServiceControl.Plugin.Nsb6.SagaAudit.SmokeTest
{
    using System;
    using NServiceBus;

    class StartMaster : ICommand
    {
        public Guid Identifier { get; set; }
        public int WorkRequired { get; set; }
    }
}