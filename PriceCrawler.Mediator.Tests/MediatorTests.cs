using Microsoft.Extensions.DependencyInjection;

using PriceCrawler.Mediator.DependencyInjection;
using PriceCrawler.Mediator.Validation;

namespace PriceCrawler.Mediator.Tests;

/// <summary>Dispatching, pipeline order, validation and registration of the own mediator.</summary>
public sealed class MediatorTests
{
    [Fact]
    public async Task SendAsync_ReturnsHandlerResult()
    {
        await using ServiceProvider provider = BuildProvider(builder => builder);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

        int result = await sender.SendAsync(new AddQuery(2, 3));

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task SendAsync_CommandWithoutResult_ReturnsUnit()
    {
        await using ServiceProvider provider = BuildProvider(builder => builder);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var command = new RememberCommand("milk");

        Unit result = await sender.SendAsync(command);

        Assert.Equal(Unit.Value, result);
        Assert.Equal(["milk"], provider.GetRequiredService<CallLog>().Entries);
    }

    [Fact]
    public async Task SendAsync_RunsBehaviorsInRegistrationOrder()
    {
        await using ServiceProvider provider = BuildProvider(builder => builder
            .AddOpenBehavior(typeof(OuterBehavior<,>))
            .AddOpenBehavior(typeof(InnerBehavior<,>)));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await sender.SendAsync(new RememberCommand("handler"));

        Assert.Equal(["outer:before", "inner:before", "handler", "inner:after", "outer:after"],
            provider.GetRequiredService<CallLog>().Entries);
    }

    [Fact]
    public async Task SendAsync_InvalidRequest_ThrowsAndSkipsHandler()
    {
        await using ServiceProvider provider = BuildProvider(builder => builder.AddValidation());
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

        RequestValidationException exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => sender.SendAsync(new RememberCommand("")));

        ValidationFailure failure = Assert.Single(exception.Failures);
        Assert.Equal(nameof(RememberCommand.Text), failure.PropertyName);
        Assert.Empty(provider.GetRequiredService<CallLog>().Entries);
        Assert.Equal(["Text is required."], exception.ToDictionary()[nameof(RememberCommand.Text)]);
    }

    [Fact]
    public async Task SendAsync_ValidRequest_PassesValidation()
    {
        await using ServiceProvider provider = BuildProvider(builder => builder.AddLogging().AddValidation());
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await sender.SendAsync(new RememberCommand("bread"));

        Assert.Equal(["bread"], provider.GetRequiredService<CallLog>().Entries);
    }

    [Fact]
    public async Task SendAsync_NoHandler_ThrowsHandlerNotFound()
    {
        await using ServiceProvider provider = BuildProvider(builder => builder);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

        HandlerNotFoundException exception = await Assert.ThrowsAsync<HandlerNotFoundException>(
            () => sender.SendAsync(new OrphanQuery()));

        Assert.Contains(nameof(OrphanQuery), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_PassesCancellationTokenToHandler()
    {
        await using ServiceProvider provider = BuildProvider(builder => builder.AddLogging());
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync(new WaitQuery(), cancellation.Token));
    }

    [Fact]
    public void AddOpenBehavior_RejectsNonBehaviorType()
    {
        var services = new ServiceCollection();
        MediatorBuilder builder = services.AddMediator([]);

        Assert.Throws<ArgumentException>(() => builder.AddOpenBehavior(typeof(List<>)));
    }

    [Fact]
    public void AddMediator_SameAssemblyTwice_RegistersHandlerOnce()
    {
        var services = new ServiceCollection();

        services.AddMediator(typeof(MediatorTests).Assembly, typeof(MediatorTests).Assembly);
        services.AddMediator(typeof(MediatorTests).Assembly);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRequestHandler<AddQuery, int>));
    }

    /// <summary>Builds a container with the mediator over this assembly and a shared call log.</summary>
    private static ServiceProvider BuildProvider(Func<MediatorBuilder, MediatorBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CallLog>();
        configure(services.AddMediator(typeof(MediatorTests).Assembly));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
