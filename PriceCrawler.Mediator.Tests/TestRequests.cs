using PriceCrawler.Mediator.Validation;

namespace PriceCrawler.Mediator.Tests;

/// <summary>Collects what handlers and behaviors did, in order.</summary>
public sealed class CallLog
{
    public List<string> Entries { get; } = [];
}

public sealed record AddQuery(int Left, int Right) : IQuery<int>;

public sealed class AddQueryHandler : IQueryHandler<AddQuery, int>
{
    public Task<int> HandleAsync(AddQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(request.Left + request.Right);
}

public sealed record RememberCommand(string Text) : ICommand;

public sealed class RememberCommandHandler(CallLog log) : ICommandHandler<RememberCommand>
{
    public Task<Unit> HandleAsync(RememberCommand request, CancellationToken cancellationToken)
    {
        log.Entries.Add(request.Text);
        return Task.FromResult(Unit.Value);
    }
}

public sealed class RememberCommandValidator : IRequestValidator<RememberCommand>
{
    public IEnumerable<ValidationFailure> Validate(RememberCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            yield return new ValidationFailure(nameof(RememberCommand.Text), "Text is required.");
        }
    }
}

public sealed record OrphanQuery : IQuery<int>;

public sealed record WaitQuery : IQuery<int>;

public sealed class WaitQueryHandler : IQueryHandler<WaitQuery, int>
{
    public async Task<int> HandleAsync(WaitQuery request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }
}

public sealed class OuterBehavior<TRequest, TResponse>(CallLog log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, PipelineStep<TResponse> nextStep, CancellationToken cancellationToken)
    {
        log.Entries.Add("outer:before");
        TResponse response = await nextStep(cancellationToken);
        log.Entries.Add("outer:after");
        return response;
    }
}

public sealed class InnerBehavior<TRequest, TResponse>(CallLog log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, PipelineStep<TResponse> nextStep, CancellationToken cancellationToken)
    {
        log.Entries.Add("inner:before");
        TResponse response = await nextStep(cancellationToken);
        log.Entries.Add("inner:after");
        return response;
    }
}
