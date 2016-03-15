namespace ServiceControl.Plugin.SagaAudit
{
    using System.IO;
    using NServiceBus;
    using NServiceBus.Serialization;

    class CaptureSagaStateSerializer
    {
        readonly IMessageSerializer serializer;

        public CaptureSagaStateSerializer(IMessageSerializer serializer)
        {
            this.serializer = serializer;
        }

        public string Serialize(IContainSagaData sagaEntity)
        {
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(new[]
                {
                    sagaEntity
                }, memoryStream);
                memoryStream.Position = 0;
                using (var streamReader = new StreamReader(memoryStream))
                {
                    return streamReader.ReadToEnd();
                }
            }
        }
    }
}