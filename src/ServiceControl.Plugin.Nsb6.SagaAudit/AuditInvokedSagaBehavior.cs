namespace ServiceControl.Plugin.SagaAudit
{
    using System;
    using System.Threading.Tasks;
    using NServiceBus.Pipeline;
    using NServiceBus.Sagas;

    class AuditInvokedSagaBehavior : Behavior<IInvokeHandlerContext>
    {
        public override async Task Invoke(IInvokeHandlerContext context, Func<Task> next)
        {
            await next().ConfigureAwait(false);

            ActiveSagaInstance activeSagaInstance;

            if (!context.Extensions.TryGet(out activeSagaInstance))
            {
                return;
            }

            var invokedSagaAuditData = $"{activeSagaInstance.Instance.GetType().FullName}:{activeSagaInstance.Instance.Entity.Id}";

            string invokedSagasHeader;

            if (context.MessageHeaders.TryGetValue(SagaAuditHeaders.InvokedSagas, out invokedSagasHeader))
            {
                context.Headers[SagaAuditHeaders.InvokedSagas] += $"{invokedSagasHeader};{invokedSagaAuditData}";
            }
            else
            {
                context.Headers.Add(SagaAuditHeaders.InvokedSagas, invokedSagaAuditData);
            }
        }
    }
}
