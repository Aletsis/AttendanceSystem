using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using AttendanceSystem.ZKTeco.Service.Services;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.ZKTeco.Adapters;

namespace AttendanceSystem.ZKTeco.Service;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configurar como servicio de Windows
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "AttendanceSystem.ZKTeco.Service";
        });

        // Agregar el worker (necesario para que funcione como servicio de Windows)
        builder.Services.AddHostedService<Worker>();

        // Configurar gRPC Server
        builder.Services.AddGrpc();
        
        // ZKTeco SDK Service registration
        builder.Services.AddSingleton<IZKTecoDeviceClient, ZKTecoDeviceClient>(); 

        // Configurar Kestrel explícitamente si es necesario, o usar appsettings
        builder.WebHost.ConfigureKestrel(options =>
        {
            var port = builder.Configuration.GetValue<int>("GrpcPort", 5001);
            options.ListenAnyIP(port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        var app = builder.Build();

        app.MapGrpcService<ZKTecoGrpcService>();
        app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

        app.Run();
    }
}
