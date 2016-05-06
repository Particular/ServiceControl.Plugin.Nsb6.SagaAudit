namespace ServiceControl.Plugin.SagaAudit
{
    using System.IO;
    using NServiceBus;
    using NServiceBus.MessageInterfaces.MessageMapper.Reflection;
    using NServiceBus.Serialization;
    using NServiceBus.Settings;

    class SagaAuditSerializer
    {
        readonly IMessageSerializer serializer;

        public SagaAuditSerializer(ReadOnlySettings settings)
        {
            var definition = new JsonSerializer();

            var factory = definition.Configure(settings);

            serializer = factory(new MessageMapper());
        }

        public string Serialize<T>(T entity)
        {
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(new []
                {
                    entity
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