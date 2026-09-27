using Adventures.Ioc.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Adventures.Ioc.Extensions
{
    public static class ServiceCollectionExtensions
    {
        private static readonly Type[] BaseLifetimeInterfaces =
        [
            typeof(ISingletonLifetime), typeof(IScopedLifetime), typeof(ITransientLifetime)
        ];

        public static IServiceCollection AddLifetimeServices(this IServiceCollection services)
        {
            var registrations = DiscoverLifetimeRegistrations();

            foreach (var group in registrations.GroupBy(r => r.InterfaceType))
            {
                var implementations = group.ToList();

                foreach (var registration in implementations)
                {
                    services.Add(new ServiceDescriptor(
                        registration.InterfaceType,
                        registration.ImplementationType.Name,
                        registration.ImplementationType,
                        registration.Lifetime));
                }

                // Only add the unkeyed convenience registration when it is unambiguous. A base lifetime
                // marker (ISingletonLifetime/IScopedLifetime/ITransientLifetime) is multi-implementation by
                // design and never gets one; a derived interface only gets one when exactly one type
                // implements it - two or more, and callers are forced onto ResolveKey instead of silently
                // getting whichever implementation the scan happened to see last.
                if (!IsBaseLifetimeInterface(group.Key) && implementations.Count == 1)
                {
                    var registration = implementations[0];
                    services.Add(new ServiceDescriptor(
                        registration.InterfaceType,
                        registration.ImplementationType,
                        registration.Lifetime));
                }
            }

            return services;
        }

        private static bool IsBaseLifetimeInterface(Type interfaceType) => Array.IndexOf(BaseLifetimeInterfaces, interfaceType) >= 0;

        private static List<(Type ImplementationType, Type InterfaceType, ServiceLifetime Lifetime)> DiscoverLifetimeRegistrations()
        {
            var implementationTypes = AppDomain.CurrentDomain.GetAssemblies()
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
                .Where(type => type is { IsClass: true, IsAbstract: false })
                .Select(type => type!);

            var registrations = new List<(Type ImplementationType, Type InterfaceType, ServiceLifetime Lifetime)>();

            foreach (var implementationType in implementationTypes)
            {
                foreach (var markerInterface in BaseLifetimeInterfaces)
                {
                    if (markerInterface.IsAssignableFrom(implementationType))
                    {
                        registrations.Add((implementationType, markerInterface, LifetimeFor(markerInterface)));
                    }
                }

                foreach (var interfaceType in implementationType.GetInterfaces())
                {
                    if (IsBaseLifetimeInterface(interfaceType))
                    {
                        continue;
                    }

                    var lifetime = InferLifetime(interfaceType);
                    if (lifetime is null)
                    {
                        continue;
                    }

                    registrations.Add((implementationType, interfaceType, lifetime.Value));
                }
            }

            return registrations;
        }

        private static ServiceLifetime LifetimeFor(Type markerInterface) => markerInterface switch
        {
            _ when markerInterface == typeof(ISingletonLifetime) => ServiceLifetime.Singleton,
            _ when markerInterface == typeof(IScopedLifetime) => ServiceLifetime.Scoped,
            _ when markerInterface == typeof(ITransientLifetime) => ServiceLifetime.Transient,
            _ => throw new ArgumentOutOfRangeException(nameof(markerInterface), markerInterface, "Not a base lifetime marker interface."),
        };

        private static ServiceLifetime? InferLifetime(Type interfaceType) => interfaceType switch
        {
            _ when typeof(ISingletonLifetime).IsAssignableFrom(interfaceType) => ServiceLifetime.Singleton,
            _ when typeof(IScopedLifetime).IsAssignableFrom(interfaceType) => ServiceLifetime.Scoped,
            _ when typeof(ITransientLifetime).IsAssignableFrom(interfaceType) => ServiceLifetime.Transient,
            _ => null,
        };

        public static T? ResolveKey<T>(this IServiceProvider serviceProvider, string key)
             where T : class
        {
            return serviceProvider.GetRequiredKeyedService<T>(key);
        }

        public static T? ResolveKey<T>(this IServiceScope serviceScope, string key)
             where T : class
        {
            return serviceScope.ServiceProvider.GetRequiredKeyedService<T>(key);
        }

        public static bool ResolveKey<T>(this IServiceProvider serviceProvider, string key, out T? service)
             where T : class
        {
            service = serviceProvider.GetKeyedService<T>(key);
            return service is not null;
        }

        public static bool ResolveKey<T>(this IServiceScope serviceScope, string key, out T? service)
             where T : class
        {
            return serviceScope.ServiceProvider.ResolveKey(key, out service);
        }

        public static bool Resolve<T>(this IServiceProvider serviceProvider, out T? service)
             where T : class
        {
            service = serviceProvider.GetService<T>();
            return service is not null;
        }

        public static bool Resolve<T>(this IServiceScope serviceScope, out T? service)
             where T : class
        {
            return serviceScope.ServiceProvider.Resolve(out service);
        }
    }
}
