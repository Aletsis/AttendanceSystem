using System.Security.Claims;
using AttendanceSystem.Blazor.Server.Services;
using MediatR;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AttendanceSystem.Blazor.UnitTests.Services;

public class CircuitMediatorTests
{
    private sealed record Ping : IRequest<string>;

    /// <summary>Una instancia por ámbito: cuenta cuántos ámbitos se crearon.</summary>
    private sealed class ScopeProbe
    {
        public static readonly List<ScopeProbe> Created = new();
        public ScopeProbe() { lock (Created) Created.Add(this); }
    }

    [Fact]
    public async Task Each_request_runs_in_its_own_scope_as_the_circuit_user()
    {
        ScopeProbe.Created.Clear();
        var services = new ServiceCollection();
        services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
        services.AddScoped<ScopeProbe>();
        services.AddScoped<IMediator>(sp =>
        {
            var probe = sp.GetRequiredService<ScopeProbe>();
            var auth = sp.GetRequiredService<AuthenticationStateProvider>();
            var mediator = new Mock<IMediator>();
            mediator.Setup(m => m.Send(It.IsAny<Ping>(), It.IsAny<CancellationToken>()))
                .Returns(async () => (await auth.GetAuthenticationStateAsync()).User.Identity?.Name ?? "anónimo");
            return mediator.Object;
        });
        services.AddScoped<CircuitMediator>();
        await using var root = services.BuildServiceProvider();

        await using var circuit = root.CreateAsyncScope();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "ana") }, "test"));
        ((IHostEnvironmentAuthenticationStateProvider)circuit.ServiceProvider.GetRequiredService<AuthenticationStateProvider>())
            .SetAuthenticationState(Task.FromResult(new AuthenticationState(user)));
        var sut = circuit.ServiceProvider.GetRequiredService<CircuitMediator>();

        var results = await Task.WhenAll(sut.Send(new Ping()), sut.Send(new Ping()));

        Assert.All(results, name => Assert.Equal("ana", name));
        Assert.Equal(2, ScopeProbe.Created.Count);
        Assert.NotSame(ScopeProbe.Created[0], ScopeProbe.Created[1]);
    }
}
