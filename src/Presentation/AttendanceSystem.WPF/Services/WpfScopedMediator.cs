using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AttendanceSystem.WPF.Services;

/// <summary>
/// Implementación de IMediator para WPF que ejecuta cada operación (Send / Publish)
/// dentro de un IServiceScope independiente, garantizando que el DbContext, repositorios
/// y UnitOfWork tengan un ciclo de vida acotado y se liberen inmediatamente (H15).
/// </summary>
public class WpfScopedMediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;

    public WpfScopedMediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>Ámbito de una operación de la interfaz: la hace el usuario con sesión (ver <see cref="WpfOperationScope"/>).</summary>
    private IServiceScope CreateUserScope()
    {
        var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<WpfOperationScope>().RequestedByUser = true;
        return scope;
    }

    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        using var scope = CreateUserScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(request, cancellationToken);
    }

    public async Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        using var scope = CreateUserScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.Send(request, cancellationToken);
    }

    public async Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        using var scope = CreateUserScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(request, cancellationToken);
    }

    public async Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        using var scope = CreateUserScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.Publish(notification, cancellationToken);
    }

    public async Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        using var scope = CreateUserScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.Publish(notification, cancellationToken);
    }

    public async IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var scope = CreateUserScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await foreach (var item in mediator.CreateStream(request, cancellationToken))
        {
            yield return item;
        }
    }

    public async IAsyncEnumerable<object?> CreateStream(object request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var scope = CreateUserScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await foreach (var item in mediator.CreateStream(request, cancellationToken))
        {
            yield return item;
        }
    }
}
