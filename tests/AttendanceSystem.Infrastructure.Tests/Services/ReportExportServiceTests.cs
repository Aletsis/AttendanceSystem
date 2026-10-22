using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Infrastructure.Services;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

public class ReportExportServiceTests
{
    private static readonly DateTime Sunday4 = new(2026, 10, 4);

    private static AttendanceReportViewDto Day(DateTime date, bool workedOnRest, bool hasPunches, int workedMinutes = 0, string? note = null) => new()
    {
        EmployeeId = "100",
        EmployeeName = "Empleado Uno",
        Date = date,
        IsRestDay = true,
        WorkedOnRestDay = workedOnRest,
        HasPunches = hasPunches,
        ActualCheckIn = hasPunches ? date.AddHours(9) : null,
        TotalWorkedMinutes = workedMinutes,
        AttendanceNote = note
    };

    private static IXLWorksheet Sheet(byte[] bytes) => new XLWorkbook(new MemoryStream(bytes)).Worksheets.First();

    [Fact]
    public void SummaryExcel_CountsRestDaysWorked_AndSundaysWithAnyPunch()
    {
        var data = new[]
        {
            Day(Sunday4, workedOnRest: true, hasPunches: true, workedMinutes: 480),
            Day(Sunday4.AddDays(7), workedOnRest: true, hasPunches: true),   // sólo entrada
            Day(Sunday4.AddDays(14), workedOnRest: false, hasPunches: false) // descanso sin marcajes
        };

        var sheet = Sheet(new ReportExportService().GenerateExcel(data, Sunday4, Sunday4.AddDays(14), "Empresa", null));

        sheet.Cell(5, 8).GetString().Should().Be("2", "columna Descanso: número de descansos trabajados");
        sheet.Cell(5, 10).GetString().Should().Be("2", "prima dominical: domingos con al menos un marcaje");
    }

    [Fact]
    public void DailyExcel_UsesTheStoredWorkedTime_AndShowsTheNotes()
    {
        var day = Day(Sunday4, workedOnRest: true, hasPunches: true, workedMinutes: 450, note: "Comida de 30 min — 30 min descontados");

        var sheet = Sheet(new ReportExportService().GenerateExcel(new[] { day }, Sunday4, Sunday4, "Empresa", null));

        sheet.Cell(4, 13).GetString().Should().Be("Notas");
        sheet.Cell(5, 7).GetString().Should().Be("07:30");
        sheet.Cell(5, 13).GetString().Should().Be("Comida de 30 min — 30 min descontados");
    }
}
