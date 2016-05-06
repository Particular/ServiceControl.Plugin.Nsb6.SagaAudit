using System;
using System.Threading.Tasks;

namespace ServiceControl.Plugin.Nsb6.SagaAudit.SmokeTest
{
    using System.Collections.Concurrent;
    using System.Threading;
    using Autofac;
    using NServiceBus;

    class Program
    {
        static void Main()
        {
            AsyncMain().GetAwaiter().GetResult();
        }

        static async Task AsyncMain()
        {
            Console.Title = "ServiceControl.Plugin.Nsb6.SagaAudit.SmokeTest";

            var builder = new ContainerBuilder();

            var masters = new ConcurrentDictionary<Guid,bool>();
            var cancellationSource = new CancellationTokenSource();

            builder.RegisterInstance(masters);
            builder.RegisterInstance(cancellationSource);
            var container = builder.Build();

            var busConfiguration = new EndpointConfiguration("ServiceControl.Plugin.Nsb6.SagaAudit.SmokeTest");
            busConfiguration.UseContainer<AutofacBuilder>(c => c.ExistingLifetimeScope(container));
            busConfiguration.UseSerialization<JsonSerializer>();
            busConfiguration.EnableInstallers();
            busConfiguration.UsePersistence<InMemoryPersistence>();
            busConfiguration.SendFailedMessagesTo("error");
            busConfiguration.AuditProcessedMessagesTo("audit");

            var endpoint = await Endpoint.Start(busConfiguration);

            var token = cancellationSource.Token;

            try
            {
                for (var i = 1; i <= 10; i++)
                {
                    var masterId = Guid.NewGuid();
                    masters.TryAdd(masterId,false);
                    Console.WriteLine($"Sending StartMaster for {masterId}");
                    await endpoint.SendLocal(new StartMaster
                    {
                        Identifier = masterId,
                        WorkRequired = i
                    });
                }
                do
                {
                    try
                    {
                        await Task.Delay(2000, token);
                    } catch (TaskCanceledException)
                    { }
                } while (!token.IsCancellationRequested);
            }
            finally
            {
                await endpoint.Stop();
                Console.ReadKey();
            }
        }
    }
}
