namespace NServiceBus.AcceptanceTests.EndpointTemplates
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using AcceptanceTesting.Customization;
    using AcceptanceTesting.Support;
    using Config.ConfigurationSource;
    using Configuration.AdvanceExtensibility;
    using Features;
    using Hosting.Helpers;
    using ObjectBuilder;
    using Serialization;

    public class DefaultServer : IEndpointSetupTemplate
    {
        public DefaultServer()
        {
            typesToInclude = new List<Type>();
        }

        public DefaultServer(List<Type> typesToInclude)
        {
            this.typesToInclude = typesToInclude;
        }

        public async Task<EndpointConfiguration> GetConfiguration(RunDescriptor runDescriptor, EndpointCustomizationConfiguration customizationConfiguration, IConfigurationSource configSource, Action<EndpointConfiguration> configurationBuilderCustomization)
        {
            var settings = runDescriptor.Settings;

            var types = GetTypesScopedByTestClass(customizationConfiguration);

            typesToInclude.AddRange(types);

            var endpointConfiguration = new EndpointConfiguration(customizationConfiguration.EndpointName);

            endpointConfiguration.TypesToIncludeInScan(typesToInclude);
            endpointConfiguration.CustomConfigurationSource(configSource);
            endpointConfiguration.EnableInstallers();

            endpointConfiguration.DisableFeature<TimeoutManager>();
            endpointConfiguration.Recoverability().Delayed(cfg => cfg.NumberOfRetries(0));
            endpointConfiguration.Recoverability().Immediate(cfg => cfg.NumberOfRetries(0));

            await endpointConfiguration.DefineTransport(settings, customizationConfiguration.EndpointName).ConfigureAwait(false);

            endpointConfiguration.DefineBuilder(settings);
            endpointConfiguration.RegisterComponents(r => { RegisterInheritanceHierarchyOfContextOnContainer(runDescriptor, r); });

            Type serializerType;
            if (settings.TryGet("Serializer", out serializerType))
            {
                endpointConfiguration.UseSerialization((SerializationDefinition) Activator.CreateInstance(serializerType));
            }
            await endpointConfiguration.DefinePersistence(settings, customizationConfiguration.EndpointName).ConfigureAwait(false);

            endpointConfiguration.GetSettings().SetDefault("ScaleOut.UseSingleBrokerQueue", true);
            configurationBuilderCustomization(endpointConfiguration);

            return endpointConfiguration;
        }

        static void RegisterInheritanceHierarchyOfContextOnContainer(RunDescriptor runDescriptor, IConfigureComponents r)
        {
            var type = runDescriptor.ScenarioContext.GetType();
            while (type != typeof(object))
            {
                r.RegisterSingleton(type, runDescriptor.ScenarioContext);
                type = type.BaseType;
            }
        }

        static IEnumerable<Type> GetTypesScopedByTestClass(EndpointCustomizationConfiguration endpointConfiguration)
        {
            var assemblies = new AssemblyScanner().GetScannableAssemblies();

            var types = assemblies.Assemblies
                //exclude all test types by default
                .Where(a =>
                {
                    var references = a.GetReferencedAssemblies();

                    return references.All(an => an.Name != "nunit.framework");
                })
                .SelectMany(a => a.GetTypes());


            types = types.Union(GetNestedTypeRecursive(endpointConfiguration.BuilderType.DeclaringType, endpointConfiguration.BuilderType));

            types = types.Union(endpointConfiguration.TypesToInclude);

            return types.Where(t => !endpointConfiguration.TypesToExclude.Contains(t)).ToList();
        }

        static IEnumerable<Type> GetNestedTypeRecursive(Type rootType, Type builderType)
        {
            if (rootType == null)
            {
                throw new InvalidOperationException("Make sure you nest the endpoint infrastructure inside the TestFixture as nested classes");
            }

            yield return rootType;

            if (typeof(IEndpointConfigurationFactory).IsAssignableFrom(rootType) && rootType != builderType)
            {
                yield break;
            }

            foreach (var nestedType in rootType.GetNestedTypes(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).SelectMany(t => GetNestedTypeRecursive(t, builderType)))
            {
                yield return nestedType;
            }
        }

        List<Type> typesToInclude;
    }
}