using AttendanceSystem.Infrastructure.Services;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

public class ImportServiceBranchesTests
{
    private static MemoryStream Workbook(params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Sucursales");
        string[] header = ["Código", "Nombre", "Dirección", "Es Externa (Si/No)", "Host Externo (URL, opcional)"];
        for (var c = 0; c < header.Length; c++) ws.Cell(1, c + 1).Value = header[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                ws.Cell(r + 2, c + 1).Value = rows[r][c];

        var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task ParseBranches_ReadsExternalFlagWithOrWithoutAccent_AndOptionalHost()
    {
        using var stream = Workbook(
            ["A01", "Matriz", "Centro", "No", ""],
            ["B01", "Remota API", "Norte", "Si", "https://api.sucursal.com"],
            ["C01", "Remota Nube", "Sur", "Sí", ""]);

        var result = await new ImportService().ParseBranchesAsync(stream);

        result.Errors.Should().BeEmpty();
        result.ValidEntries.Select(b => (b.Code, b.IsExternal)).Should().Equal(
            ("A01", false), ("B01", true), ("C01", true));
        result.ValidEntries[1].ExternalHost.Should().Be("https://api.sucursal.com");
        result.ValidEntries[2].ExternalHost.Should().BeNullOrEmpty();
    }
}
