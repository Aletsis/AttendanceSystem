using AttendanceSystem.Application.Features.Attendance.Services;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.ExternalEmployees.Services;

/// <summary>
/// Encuentra al empleado externo que corresponde a un ID del reloj (código de la sucursal externa + número de empleado,
/// como lo registra <c>DeviceUserMapper.FromExternal</c>). Separa el ID con <see cref="ExternalBranchCodes"/>, igual que la
/// descarga, ADMS y el tiempo real, para que un mismo ID se interprete igual en todos lados.
/// </summary>
public sealed class ExternalEmployeeDeviceIds
{
    private readonly ExternalBranchCodes _codes;
    private readonly Dictionary<string, BranchId> _branchByCode;
    private readonly Dictionary<BranchId, string> _codeByBranch;

    public ExternalEmployeeDeviceIds(IEnumerable<Branch> branches)
    {
        var external = branches.Where(b => b.IsExternal).ToList();
        _codes = new ExternalBranchCodes(external);
        _branchByCode = external
            .GroupBy(b => b.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        _codeByBranch = external.ToDictionary(b => b.Id, b => b.Code);
    }

    /// <summary>
    /// El ID con que el empleado quedó registrado en el reloj (el código tal como está en la sucursal), aunque el ID
    /// recibido traiga el código con otras mayúsculas.
    /// </summary>
    public string DeviceUserIdOf(ExternalEmployee employee) =>
        (_codeByBranch.TryGetValue(employee.BranchId, out var code) ? code : string.Empty) + employee.EmployeeNumber;

    public static async Task<ExternalEmployeeDeviceIds> LoadAsync(IBranchRepository branches, CancellationToken cancellationToken = default) =>
        new(await branches.GetAllAsync(cancellationToken));

    /// <summary>Si el ID tiene el prefijo de una sucursal externa: esa sucursal y el número del empleado en ella.</summary>
    public bool TryParse(string deviceUserId, out BranchId branchId, out string employeeNumber)
    {
        branchId = null!;
        employeeNumber = string.Empty;

        if (string.IsNullOrWhiteSpace(deviceUserId)) return false;
        if (!_codes.TrySplit(deviceUserId.Trim(), out var code, out var number)) return false;
        if (!_branchByCode.TryGetValue(code, out var id)) return false;

        branchId = id;
        employeeNumber = number;
        return true;
    }

    public async Task<ExternalEmployee?> FindAsync(
        IExternalEmployeeRepository repository, string deviceUserId, CancellationToken cancellationToken = default) =>
        TryParse(deviceUserId, out var branchId, out var number)
            ? await repository.GetByBranchAndEmployeeNumberAsync(branchId, number, cancellationToken)
            : null;
}
