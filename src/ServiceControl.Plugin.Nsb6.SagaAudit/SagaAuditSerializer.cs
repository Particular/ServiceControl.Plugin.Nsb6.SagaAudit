namespace ServiceControl.Plugin.SagaAudit
{
    using System.IO;
    using System.Text;
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

            this.serializer = factory(new MessageMapper());
        }

        public string Serialize<T>(T entity)
        {
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(new []
                {
                    entity
                }, memoryStream);

                return Encoding.UTF8.GetString(memoryStream.ToArray());
            }
        }
    }
}