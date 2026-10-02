using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace AttendanceSystem.ZKTeco.Service.Interceptors;

public class ApiKeyAuthInterceptor : Interceptor
{
    private readonly string _apiKey;
    private readonly ILogger<ApiKeyAuthInterceptor> _logger;
    private const string ApiKeyHeaderName = "x-api-key";

    public ApiKeyAuthInterceptor(IConfiguration configuration, ILogger<ApiKeyAuthInterceptor> logger)
    {
        _logger = logger;
        var configuredKey = configuration["ApiKey"] ?? configuration["ZKTecoService:ApiKey"];

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            _logger.LogWarning("⚠️ No se ha configurado 'ApiKey' en appsettings o variables de entorno. Usando clave de desarrollo por defecto. Configure una clave segura en producción.");
            _apiKey = "AttendanceSystemSecretApiKey123!";
        }
        else
        {
            _apiKey = configuredKey;
        }
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ValidateApiKey(context);
        return await continuation(request, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ValidateApiKey(context);
        await continuation(request, responseStream, context);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ValidateApiKey(context);
        return await continuation(requestStream, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ValidateApiKey(context);
        await continuation(requestStream, responseStream, context);
    }

    private void ValidateApiKey(ServerCallContext context)
    {
        // Permitir llamadas al servicio de salud estándar sin requerir autenticación
        if (context.Method.StartsWith("/grpc.health.v1.Health/"))
        {
            return;
        }

        var metadata = context.RequestHeaders;
        var apiKeyHeader = metadata.Get(ApiKeyHeaderName);

        if (apiKeyHeader == null || string.IsNullOrWhiteSpace(apiKeyHeader.Value))
        {
            _logger.LogWarning("Falta la cabecera '{HeaderName}' en la solicitud gRPC {Method}", ApiKeyHeaderName, context.Method);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Acceso no autorizado: x-api-key requerida"));
        }

        if (!IsApiKeyValid(apiKeyHeader.Value))
        {
            _logger.LogWarning("Intento de acceso no autorizado con clave de API inválida para {Method}", context.Method);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Acceso no autorizado: x-api-key inválida"));
        }
    }

    private bool IsApiKeyValid(string providedKey)
    {
        byte[] providedBytes = Encoding.UTF8.GetBytes(providedKey);
        byte[] configuredBytes = Encoding.UTF8.GetBytes(_apiKey);

        if (providedBytes.Length != configuredBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }
}
