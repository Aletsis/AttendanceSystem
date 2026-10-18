using AttendanceSystem.Application.Abstractions;

namespace AttendanceSystem.ZKTeco.Adapters;

public interface IDeviceSessionLease : IDisposable, IAsyncDisposable
{
    string SessionId { get; }
    IDeviceClient Client { get; }
}
