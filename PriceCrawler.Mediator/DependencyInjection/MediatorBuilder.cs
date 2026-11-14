using Microsoft.Extensions.DependencyInjection;

using PriceCrawler.Mediator.Behaviors;
using PriceCrawler.Mediator.Validation;

namespace PriceCrawler.Mediator.DependencyInjection;

/// <summary>
/// Adds pipeline behaviors after <see cref="MediatorServiceCollectionExtensions.AddMediator"/>.
/// Behaviors run in the order they are added: the first one is the outermost.
/// </summary>
/// <param name="services">Service collection the mediator was added to.</param>
public sealed class MediatorBuilder(IServiceCollection services)
{
    /// <summary>The service collection the mediator was added to.</summary>
    public IServiceCollection Services { get; } = services;

    /// <summary>Adds an open generic behavior that applies to every request.</summary>
    /// <param name="openBehaviorType">
    /// An open generic class implementing <see cref="IPipelineBehavior{TRequest, TResponse}"/>,
    /// for example <c>typeof(LoggingBehavior&lt;,&gt;)</c>.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The type is not an open generic pipeline behavior.</exception>
    public MediatorBuilder AddOpenBehavior(Type openBehaviorType)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);
        bool isBehavior = openBehaviorType.IsGenericTypeDefinition
            && openBehaviorType.GetInterfaces().Any(contract =>
                contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));
        if (!isBehavior)
        {
            throw new ArgumentException(
                $"'{openBehaviorType.FullName}' is not an open generic IPipelineBehavior<,>.",
                nameof(openBehaviorType));
        }

        Services.AddScoped(typeof(IPipelineBehavior<,>), openBehaviorType);
        return this;
    }

    /// <summary>Adds <see cref="LoggingBehavior{TRequest, TResponse}"/>.</summary>
    /// <returns>This builder.</returns>
    public MediatorBuilder AddLogging() => AddOpenBehavior(typeof(LoggingBehavior<,>));

    /// <summary>Adds <see cref="ValidationBehavior{TRequest, TResponse}"/>.</summary>
    /// <returns>This builder.</returns>
    public MediatorBuilder AddValidation() => AddOpenBehavior(typeof(ValidationBehavior<,>));
}
