using System.Text;
using System.Text.RegularExpressions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Infrastructure.Services;
using ClosedXML.Excel;
using FluentAssertions;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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

    // Peor caso de una tarjeta: turno partido, logo y varias observaciones todos los días.
    private static List<AttendanceReportViewDto> CrowdedSplitDays(string employeeId, int count) => Enumerable.Range(0, count).Select(i =>
    {
        var date = Sunday4.AddDays(i);
        return new AttendanceReportViewDto
        {
            EmployeeId = employeeId,
            Date = date,
            ShiftType = ShiftType.Partido,
            ScheduledCheckIn = TimeSpan.FromHours(8),
            ScheduledCheckOut = TimeSpan.FromHours(13),
            ScheduledBlock2CheckIn = TimeSpan.FromHours(15),
            ScheduledBlock2CheckOut = TimeSpan.FromHours(19),
            ActualCheckIn = date.AddHours(8),
            ActualBlock1CheckOut = date.AddHours(13),
            ActualBlock2CheckIn = date.AddHours(15),
            MissingCheckOut = true,
            EarlyDepartureMinutes = 20,
            OvertimeMinutes = 60,
            TemporaryExits = new[]
            {
                new TemporaryExitDto(Guid.NewGuid(), date.AddHours(10), date.AddHours(10.5), 30, TemporaryExitStatus.ApprovedPaid, false),
                new TemporaryExitDto(Guid.NewGuid(), date.AddHours(16), date.AddHours(16.5), 30, TemporaryExitStatus.Pending, false)
            }
        };
    }).ToList();

    private static int PageCount(byte[] pdf) =>
        int.Parse(Regex.Match(Encoding.Latin1.GetString(pdf), @"/Count (\d+)").Groups[1].Value);

    [Theory]
    [InlineData(7, 1)]   // media hoja: dos tarjetas por hoja
    [InlineData(31, 2)]  // hoja completa: una tarjeta por hoja
    public void CardsPdf_CrowdedCards_KeepTheirSpace(int days, int expectedPages)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var data = new Dictionary<(string EmployeeId, string EmployeeName), List<AttendanceReportViewDto>>
        {
            [("100", "Empleado Uno")] = CrowdedSplitDays("100", days),
            [("101", "Empleado Dos")] = CrowdedSplitDays("101", days)
        };

        var pdf = new ReportExportService().GenerateAttendanceCardsPdf(
            data, Sunday4, Sunday4.AddDays(days - 1), "Empresa", Placeholders.Image(200, 200));

        PageCount(pdf).Should().Be(expectedPages, "una tarjeta que no cabe se parte en otra hoja");
    }
}
