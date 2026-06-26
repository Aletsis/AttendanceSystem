using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace AttendanceSystem.ZKTeco.Service.Interceptors
{
    public class ApiKeyAuthInterceptor : Interceptor
    {
        private readonly string _apiKey;
        private readonly ILogger<ApiKeyAuthInterceptor> _logger;
        private const string ApiKeyHeaderName = "x-api-key";

        public ApiKeyAuthInterceptor(IConfiguration configuration, ILogger<ApiKeyAuthInterceptor> logger)
        {
            _apiKey = configuration["ApiKey"] ?? "AttendanceSystemSecretApiKey123!";
            _logger = logger;
        }

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request,
            ServerCallContext context,
            UnaryServerMethod<TRequest, TResponse> continuation)
        {
            ValidateApiKey(context);
            return await continuation(request, context);
        }

        private void ValidateApiKey(ServerCallContext context)
        {
            var metadata = context.RequestHeaders;
            var apiKeyHeader = metadata.Get(ApiKeyHeaderName);

            if (apiKeyHeader == null)
            {
                _logger.LogWarning("Falta la cabecera x-api-key en la solicitud gRPC.");
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Acceso no autorizado: x-api-key requerida"));
            }

            if (!string.Equals(apiKeyHeader.Value, _apiKey, StringComparison.Ordinal))
            {
                _logger.LogWarning("Intento de acceso no autorizado con clave de API inválida.");
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Acceso no autorizado: x-api-key inválida"));
            }
        }
    }
}
