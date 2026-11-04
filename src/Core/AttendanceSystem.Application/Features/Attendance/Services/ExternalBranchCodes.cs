using AttendanceSystem.Domain.Aggregates.BranchAggregate;

namespace AttendanceSystem.Application.Features.Attendance.Services;

/// <summary>
/// Códigos de las sucursales externas. Un ID de reloj que empieza con uno de ellos (sin distinguir mayúsculas) es de un
/// empleado de esa sucursal: su marcaje no es local, se guarda pendiente de subir a la nube. Lo comparten la descarga
/// SDK, ADMS y el tiempo real, para que un mismo marcaje se clasifique igual llegue por donde llegue.
/// </summary>
public sealed class ExternalBranchCodes
{
    private const int CodeLength = 3;
    private readonly HashSet<string> _codes;

    public ExternalBranchCodes(IEnumerable<Branch> branches)
    {
        _codes = new HashSet<string>(branches.Where(b => b.IsExternal).Select(b => b.Code), StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<ExternalBranchCodes> LoadAsync(IBranchRepository branches, CancellationToken cancellationToken = default) =>
        new(await branches.GetAllAsync(cancellationToken));

    /// <summary>
    /// Si el ID es de un empleado de una sucursal externa: el código tal como lo marcó el reloj y el número del empleado
    /// en esa sucursal. Hace falta al menos un carácter después del código.
    /// </summary>
    public bool TrySplit(string userId, out string branchCode, out string employeeId)
    {
        if (userId.Length > CodeLength && _codes.Contains(userId[..CodeLength]))
        {
            branchCode = userId[..CodeLength];
            employeeId = userId[CodeLength..];
            return true;
        }

        branchCode = employeeId = string.Empty;
        return false;
    }
}
