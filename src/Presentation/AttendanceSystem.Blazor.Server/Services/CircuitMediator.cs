namespace AttendanceSystem.Blazor.Server.Services;

using System.Runtime.CompilerServices;
using MediatR;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// El <see cref="IMediator"/> que usan los componentes. En Blazor Server el ámbito de DI dura todo el circuito, así
/// que todos los componentes abiertos (menú, página, rejilla, diálogos) compartían un solo DbContext: dos consultas a
/// la vez fallaban ("A second operation was started on this context") y un comando largo (p. ej. conectar a un reloj)
/// usaba un contexto ya desechado si el circuito se cerraba mientras esperaba. Cada petición corre aquí en su propio
/// ámbito, con el usuario del circuito, y ese ámbito vive lo que dura la petición.
/// </summary>
public sealed class CircuitMediator : IMediator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AuthenticationStateProvider _circuitAuthState;

    public CircuitMediator(IServiceScopeFactory scopeFactory, AuthenticationStateProvider circuitAuthState)
    {
        _scopeFactory = scopeFactory;
        _circuitAuthState = circuitAuthState;
    }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => RunAsync(m => m.Send(request, cancellationToken));

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
        => RunAsync(async m => { await m.Send(request, cancellationToken); return true; });

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        => RunAsync(m => m.Send(request, cancellationToken));

    public Task Publish(object notification, CancellationToken cancellationToken = default)
        => RunAsync(async m => { await m.Publish(notification, cancellationToken); return true; });

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
        => RunAsync(async m => { await m.Publish(notification, cancellationToken); return true; });

    public async IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var scope = await CreateScopeAsync();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await foreach (var item in mediator.CreateStream(request, cancellationToken))
        {
            yield return item;
        }
    }

    public async IAsyncEnumerable<object?> CreateStream(
        object request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var scope = await CreateScopeAsync();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await foreach (var item in mediator.CreateStream(request, cancellationToken))
        {
            yield return item;
        }
    }

    private async Task<T> RunAsync<T>(Func<IMediator, Task<T>> send)
    {
        await using var scope = await CreateScopeAsync();
        return await send(scope.ServiceProvider.GetRequiredService<IMediator>());
    }

    /// <summary>
    /// Ámbito nuevo cuyo proveedor de autenticación lleva el usuario del circuito: sin él, el ámbito nuevo no sabría
    /// quién pide y la autorización por permisos (y la auditoría) verían un anónimo.
    /// </summary>
    private async Task<AsyncServiceScope> CreateScopeAsync()
    {
        var state = await _circuitAuthState.GetAuthenticationStateAsync();
        var scope = _scopeFactory.CreateAsyncScope();
        if (scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>() is IHostEnvironmentAuthenticationStateProvider host)
        {
            host.SetAuthenticationState(Task.FromResult(state));
        }
        return scope;
    }
}
