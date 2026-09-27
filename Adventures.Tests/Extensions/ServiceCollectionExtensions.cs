using System;
using System.Linq;
using System.Reflection;
using Adventures.Ioc.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Tests.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLifetimeServices(this IServiceCollection services)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            var implementationTypes = assemblies
                .SelectMany(assembly =>
                {
                    try
                    {
                        return assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        return ex.Types.Where(t => t is not null)!;
                    }
                })
                .Where(type => type is { IsClass: true, IsAbstract: false });

            foreach (var implementationType in implementationTypes)
            {
                RegisterIfImplements<ISingletonLifetime>(services, implementationType, ServiceLifetime.Singleton);
                RegisterIfImplements<IScopedLifetime>(services, implementationType, ServiceLifetime.Scoped);
                RegisterIfImplements<ITransientLifetime>(services, implementationType, ServiceLifetime.Transient);

                RegisterDerivedLifetimeInterfaces(services, implementationType);
            }

            return services;
        }

        private static void RegisterIfImplements<TLifetimeInterface>(
            IServiceCollection services,
            Type implementationType,
            ServiceLifetime lifetime)
            where TLifetimeInterface : class
        {
            if (!typeof(TLifetimeInterface).IsAssignableFrom(implementationType))
            {
                return;
            }

            services.Add(new ServiceDescriptor(
                typeof(TLifetimeInterface),
                implementationType.Name,
                implementationType,
                lifetime));
        }

        private static void RegisterDerivedLifetimeInterfaces(IServiceCollection services, Type implementationType)
        {
            foreach (var interfaceType in implementationType.GetInterfaces())
            {
                if (interfaceType == typeof(ISingletonLifetime)
                    || interfaceType == typeof(IScopedLifetime)
                    || interfaceType == typeof(ITransientLifetime))
                {
                    continue;
                }

                ServiceLifetime? lifetime = interfaceType switch
                {
                    _ when typeof(ISingletonLifetime).IsAssignableFrom(interfaceType) => ServiceLifetime.Singleton,
                    _ when typeof(IScopedLifetime).IsAssignableFrom(interfaceType) => ServiceLifetime.Scoped,
                    _ when typeof(ITransientLifetime).IsAssignableFrom(interfaceType) => ServiceLifetime.Transient,
                    _ => null,
                };

                if (lifetime is null)
                {
                    continue;
                }

                services.Add(new ServiceDescriptor(interfaceType, implementationType, lifetime.Value));
            }
        }
    }
}
