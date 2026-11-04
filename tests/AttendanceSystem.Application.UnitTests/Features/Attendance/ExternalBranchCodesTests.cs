using AttendanceSystem.Application.Features.Attendance.Services;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

/// <summary>Clasificación de un ID de reloj como empleado de una sucursal externa (descarga SDK, ADMS y tiempo real).</summary>
public class ExternalBranchCodesTests
{
    private readonly ExternalBranchCodes _codes = new([
        Branch.Create("C01", "Cancún", null, isExternal: true),
        Branch.Create("M01", "Matriz", null)
    ]);

    [Theory]
    [InlineData("C01045", "C01", "045")]
    [InlineData("c01045", "c01", "045")]
    [InlineData("C011", "C01", "1")]
    public void IdsStartingWithAnExternalCode_AreSplit(string userId, string branchCode, string employeeId)
    {
        _codes.TrySplit(userId, out var code, out var employee).Should().BeTrue();
        code.Should().Be(branchCode);
        employee.Should().Be(employeeId);
    }

    [Theory]
    [InlineData("C01", "hace falta al menos un carácter después del código")]
    [InlineData("M01045", "la sucursal no es externa")]
    [InlineData("1821", "no empieza con un código")]
    [InlineData("", "ID vacío")]
    public void OtherIds_AreLocal(string userId, string because)
    {
        _codes.TrySplit(userId, out var code, out var employee).Should().BeFalse(because);
        code.Should().BeEmpty();
        employee.Should().BeEmpty();
    }
}
