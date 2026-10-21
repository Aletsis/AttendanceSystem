using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Exceptions;
using AttendanceSystem.Domain.Services;
using MediatR;

namespace AttendanceSystem.Application.Behaviors;

/// <summary>
/// F07-02 / A05: valida el rango de cualquier <see cref="IDateRangeRequest"/> antes de su manejador. Las peticiones
/// que devuelven <see cref="Result"/> reciben un fallo; las demás, <see cref="InvalidDateRangeException"/>.
/// </summary>
public sealed class DateRangeValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IDateRangeRequest range) return next();

        var error = DateRangePolicy.Validate(range.StartDate, range.EndDate, range.MaxRangeDays);
        if (error == null) return next();

        if (typeof(TResponse) == typeof(Result))
        {
            return Task.FromResult((TResponse)(object)Result.Failure(error));
        }
        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failure = typeof(TResponse).GetMethod(nameof(Result.Failure), new[] { typeof(string) })!;
            return Task.FromResult((TResponse)failure.Invoke(null, new object[] { error })!);
        }
        throw new InvalidDateRangeException(error);
    }
}
