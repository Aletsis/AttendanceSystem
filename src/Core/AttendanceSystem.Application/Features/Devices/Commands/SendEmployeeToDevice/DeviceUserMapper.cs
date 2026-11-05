using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.Features.Devices.Commands.SendEmployeeToDevice;

/// <summary>Cómo queda registrado un empleado en el reloj; compartido por el envío individual y el de toda una sucursal.</summary>
internal static class DeviceUserMapper
{
    /// <summary>De una sucursal externa el ID lleva el código de la sucursal como prefijo para enrutar sus checadas.</summary>
    public static DeviceUserDto FromEmployee(Employee employee, Branch? branch) => new(
        branch is { IsExternal: true } ? $"{branch.Code}{employee.Id.Value}" : employee.Id.Value,
        employee.FirstName,
        employee.DevicePassword ?? "",
        (int)employee.DevicePrivilege,
        employee.Status != EmployeeStatus.Baja, // caso 16: de vacaciones o incapacidad sigue habilitado
        employee.CardNumber,
        employee.Fingerprints?.Select(f => new DeviceFingerprintDto(f.FingerIndex, f.Template)).ToList(),
        employee.FaceTemplate,
        employee.Photo);

    public static DeviceUserDto FromExternal(ExternalEmployee employee, Branch? branch, int privilege) => new(
        $"{branch?.Code ?? ""}{employee.EmployeeNumber}",
        $"{employee.FirstName} {employee.LastName}".Trim(),
        "", // Sin contraseña PIN en sucursal remota
        privilege,
        employee.Status != EmployeeStatus.Baja,
        employee.CardNumber,
        employee.Fingerprints.Select(f => new DeviceFingerprintDto(f.FingerIndex, f.Template)).ToList(),
        null,
        null);
}
