using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PriceCrawler.Mediator.Validation;

namespace PriceCrawler.Mediator.DependencyInjection;

/// <summary>Registers the mediator, request handlers, validators and pipeline behaviors.</summary>
public static class MediatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISender"/> and every request handler and request validator found in the
    /// given assemblies. All of them are scoped.
    /// </summary>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="assemblies">Assemblies to scan for handlers and validators.</param>
    /// <returns>A builder to add pipeline behaviors with.</returns>
    /// <exception cref="InvalidOperationException">Two handlers handle the same request type.</exception>
    public static MediatorBuilder AddMediator(this IServiceCollection services, params IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        services.TryAddScoped<ISender, Mediator>();
        foreach (Assembly assembly in assemblies.Distinct())
        {
            RegisterAssembly(services, assembly);
        }

        return new MediatorBuilder(services);
    }

    /// <summary>Registers the handlers and validators of one assembly.</summary>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="assembly">Assembly to scan.</param>
    private static void RegisterAssembly(IServiceCollection services, Assembly assembly)
    {
        IEnumerable<Type> candidates = assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (Type implementation in candidates)
        {
            foreach (Type contract in implementation.GetInterfaces().Where(type => type.IsGenericType))
            {
                Type definition = contract.GetGenericTypeDefinition();
                if (definition == typeof(IRequestHandler<,>))
                {
                    AddHandler(services, contract, implementation);
                }
                else if (definition == typeof(IRequestValidator<>))
                {
                    services.TryAddEnumerable(ServiceDescriptor.Scoped(contract, implementation));
                }
            }
        }
    }

    /// <summary>Registers one handler, refusing a second handler for the same request type.</summary>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="contract">Closed <see cref="IRequestHandler{TRequest, TResponse}"/> type.</param>
    /// <param name="implementation">Handler class.</param>
    /// <exception cref="InvalidOperationException">Another handler is already registered for the request.</exception>
    private static void AddHandler(IServiceCollection services, Type contract, Type implementation)
    {
        ServiceDescriptor? existing = services.FirstOrDefault(descriptor => descriptor.ServiceType == contract);
        if (existing is null)
        {
            services.AddScoped(contract, implementation);
            return;
        }

        if (existing.ImplementationType != implementation)
        {
            throw new InvalidOperationException(
                $"Request '{contract.GenericTypeArguments[0].FullName}' has two handlers: " +
                $"'{existing.ImplementationType?.FullName}' and '{implementation.FullName}'.");
        }
    }
}
