using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Employees;
using AttendanceSystem.Domain.Enumerations;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using Colors = QuestPDF.Helpers.Colors;

namespace AttendanceSystem.Blazor.Server.Services;

public class ReportExportService
{
    public byte[] GenerateExcel(IEnumerable<AttendanceReportViewDto> attendanceData, DateTime startDate, DateTime endDate)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Reporte de Asistencia");

        // 1. Title and Info
        var titleRange = worksheet.Range("A1:L1");
        titleRange.Merge().Value = "Carnicerias La Blanquita";
        titleRange.Style.Font.FontSize = 16;
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontColor = XLColor.White;
        titleRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1976D2"); // Primary Blue
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        
        var culture = new CultureInfo("es-ES");
        var dateRangeStr = startDate.Date == endDate.Date 
            ? startDate.ToString("dd 'DE' MMMM 'DE' yyyy", culture).ToUpper()
            : $"{startDate.ToString("dd 'DE' MMMM 'DE' yyyy", culture).ToUpper()} - {endDate.ToString("dd 'DE' MMMM 'DE' yyyy", culture).ToUpper()}";

        worksheet.Cell("A2").Value = $"INCIDENCIAS RELOJ CHECADOR: {dateRangeStr}";
        worksheet.Range("A2:L2").Merge().Style.Font.Italic = true;
        worksheet.Range("A2:L2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // 2. Headers
        int headerRow = 4;
        string[] headers = {
            "ID", "Nombre Completo", "Fecha", "Horario Entrada", "Horario Salida",
            "Entrada Real", "Salida Real", "Tiempo Trabajado", "Tiempo Extra",
            "Sucursal", "Falta", "Descanso", "Retardo"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = worksheet.Cell(headerRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#424242"); // Dark Grey
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.White;
        }

        // 3. Data
        var currentRow = headerRow + 1;
        foreach (var item in attendanceData)
        {
            // Calculate values
            TimeSpan worked = TimeSpan.Zero;
            if (item.ActualCheckIn.HasValue && item.ActualCheckOut.HasValue)
            {
                worked = item.ActualCheckOut.Value - item.ActualCheckIn.Value;
            }
            var workedStr = worked == TimeSpan.Zero ? "" : $"{(int)worked.TotalHours:00}:{worked.Minutes:00}";
            var overtimeStr = CalculateOvertimeString(item);

            // Fill Cells
            SetCell(worksheet, currentRow, 1, item.EmployeeId);
            SetCell(worksheet, currentRow, 2, item.EmployeeName);
            SetCell(worksheet, currentRow, 3, item.Date.ToString("dd/MM/yyyy"));
            SetCell(worksheet, currentRow, 4, item.ScheduledCheckIn?.ToString(@"hh\:mm") ?? "--:--");
            SetCell(worksheet, currentRow, 5, item.ScheduledCheckOut?.ToString(@"hh\:mm") ?? "--:--");
            SetCell(worksheet, currentRow, 6, FormatDateTime(item.ActualCheckIn, item.Date, "--:--"));
            SetCell(worksheet, currentRow, 7, FormatDateTime(item.ActualCheckOut, item.Date, "--:--"));
            SetCell(worksheet, currentRow, 8, workedStr);
            SetCell(worksheet, currentRow, 9, overtimeStr);
            SetCell(worksheet, currentRow, 10, item.BranchName);
            SetCell(worksheet, currentRow, 11, item.IsAbsent ? "1FINJ" : "0", true);
            SetCell(worksheet, currentRow, 12, item.WorkedOnRestDay ? "1DFT" : "0", true);
            SetCell(worksheet, currentRow, 13, item.LateMinutes > 0 ? "1RET" : "0", true);

            // Row Styling
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; // ID
            for (int k = 3; k <= 9; k++) worksheet.Cell(currentRow, k).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; // Date & Times
            for (int k = 11; k <= 13; k++) worksheet.Cell(currentRow, k).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; // Flags

            currentRow++;
        }

        // Adjust column widths
        worksheet.Columns().AdjustToContents();
        // Set specific width for Name
        worksheet.Column(2).Width = 30;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private void SetCell(IXLWorksheet ws, int row, int col, object value, bool isIndicator = false)
    {
        var cell = ws.Cell(row, col);
        if (value is string s) cell.Value = s;
        else cell.Value = value?.ToString() ?? "";
        
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.OutsideBorderColor = XLColor.LightGray;
        
        if (isIndicator && value?.ToString() != "0")
        {
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.Red;
        }
        else if (isIndicator)
        {
            cell.Style.Font.FontColor = XLColor.LightGray;
        }
    }

    public byte[] GeneratePdf(IEnumerable<AttendanceReportViewDto> attendanceData, DateTime startDate, DateTime endDate)
    {
         return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily(Fonts.SegoeUI));

                page.Header()
                    .Row(row => 
                    {
                        row.RelativeItem().Column(col => 
                        {
                            col.Item().AlignCenter().Text("CARNICERIAS LA BLANQUITA").SemiBold().FontSize(18).FontColor(Colors.Blue.Darken2);
                            
                            var culture = new CultureInfo("es-ES");
                            var dateRangeStr = startDate.Date == endDate.Date 
                                ? startDate.ToString("dd 'DE' MMMM 'DE' yyyy", culture).ToUpper()
                                : $"{startDate.ToString("dd 'DE' MMMM 'DE' yyyy", culture).ToUpper()} - {endDate.ToString("dd 'DE' MMMM 'DE' yyyy", culture).ToUpper()}";
                                
                            col.Item().AlignCenter().Text($"INCIDENCIAS RELOJ CHECADOR: {dateRangeStr}").FontSize(10).FontColor(Colors.Grey.Medium);
                        });
                    });

                page.Content()
                    .PaddingVertical(0.5f, Unit.Centimetre)
                    .Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(35); // Id
                            columns.RelativeColumn(3); // Name
                            columns.ConstantColumn(50); // Date
                            columns.RelativeColumn(); // Sch In
                            columns.RelativeColumn(); // Sch Out
                            columns.RelativeColumn(); // In
                            columns.RelativeColumn(); // Out
                            columns.RelativeColumn(); // Worked
                            columns.RelativeColumn(); // Overtime
                            columns.RelativeColumn(2); // Branch
                            columns.ConstantColumn(30); // Abs
                            columns.ConstantColumn(30); // Rest
                            columns.ConstantColumn(30); // Late
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderStyle).Text("ID");
                            header.Cell().Element(HeaderStyle).Text("Nombre");
                            header.Cell().Element(HeaderStyle).Text("Fecha");
                            header.Cell().Element(HeaderStyle).Text("H. Ent");
                            header.Cell().Element(HeaderStyle).Text("H. Sal");
                            header.Cell().Element(HeaderStyle).Text("Entrada");
                            header.Cell().Element(HeaderStyle).Text("Salida");
                            header.Cell().Element(HeaderStyle).Text("T. Trab");
                            header.Cell().Element(HeaderStyle).Text("T. Ext");
                            header.Cell().Element(HeaderStyle).Text("Sucursal");
                            header.Cell().Element(HeaderStyle).Text("Falta");
                            header.Cell().Element(HeaderStyle).Text("Desc.");
                            header.Cell().Element(HeaderStyle).Text("Ret.");

                            static IContainer HeaderStyle(IContainer container)
                            {
                                return container
                                    .Background(Colors.Blue.Darken2)
                                    .PaddingVertical(5)
                                    .PaddingHorizontal(2)
                                    .AlignMiddle()
                                    .AlignCenter()
                                    .DefaultTextStyle(x => x.SemiBold().FontColor(Colors.White));
                            }
                        });

                        uint rowIndex = 0;
                        foreach (var item in attendanceData)
                        {
                            var overtimeStr = CalculateOvertimeString(item);
                            
                            TimeSpan worked = TimeSpan.Zero;
                            if (item.ActualCheckIn.HasValue && item.ActualCheckOut.HasValue)
                            {
                                worked = item.ActualCheckOut.Value - item.ActualCheckIn.Value;
                            }
                            
                            var workedStr = worked == TimeSpan.Zero ? "" : $"{(int)worked.TotalHours:00}:{worked.Minutes:00}";
                            var bgColor = rowIndex % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                            table.Cell().Element(c => BodyStyle(c, bgColor)).Text(item.EmployeeId);
                            table.Cell().Element(c => BodyStyle(c, bgColor).AlignLeft()).Text(item.EmployeeName);
                            table.Cell().Element(c => BodyStyle(c, bgColor)).Text(item.Date.ToString("dd/MM/yyyy"));
                            table.Cell().Element(c => BodyStyle(c, bgColor)).Text(item.ScheduledCheckIn?.ToString(@"hh\:mm") ?? "--");
                            table.Cell().Element(c => BodyStyle(c, bgColor)).Text(item.ScheduledCheckOut?.ToString(@"hh\:mm") ?? "--");
                            table.Cell().Element(c => BodyStyle(c, bgColor)).Text(FormatDateTime(item.ActualCheckIn, item.Date, "--"));
                            table.Cell().Element(c => BodyStyle(c, bgColor)).Text(FormatDateTime(item.ActualCheckOut, item.Date, "--"));
                            table.Cell().Element(c => BodyStyle(c, bgColor)).Text(workedStr);
                            table.Cell().Element(c => BodyStyle(c, bgColor)).Text(overtimeStr);
                            table.Cell().Element(c => BodyStyle(c, bgColor).AlignLeft()).Text(item.BranchName);
                            
                            // Indicators with colors
                            table.Cell().Element(c => BodyStyle(c, bgColor)).Text(item.IsAbsent ? "1FINJ" : "").FontColor(Colors.Red.Medium).Bold();
                            table.Cell().Element(c => BodyStyle(c, bgColor)).Text(item.WorkedOnRestDay ? "1DFT" : "").FontColor(Colors.Green.Darken1).Bold();
                            table.Cell().Element(c => BodyStyle(c, bgColor)).Text(item.LateMinutes > 0 ? "1RET" : "").FontColor(Colors.Orange.Darken2).Bold();

                            rowIndex++;

                            static IContainer BodyStyle(IContainer container, string backgroundColor)
                            {
                                return container
                                    .Background(backgroundColor)
                                    .BorderBottom(1)
                                    .BorderColor(Colors.Grey.Lighten3)
                                    .PaddingVertical(4)
                                    .PaddingHorizontal(2)
                                    .AlignMiddle()
                                    .AlignCenter();
                            }
                        }
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(x =>
                    {
                        x.Span("Página ");
                        x.CurrentPageNumber();
                        x.Span(" de ");
                        x.TotalPages();
                    });
            });
        })
        .GeneratePdf();
    }

    private string CalculateOvertimeString(AttendanceReportViewDto item)
    {
        if (item.RoundedOvertimeMinutes <= 0) return "";
        
        var ts = TimeSpan.FromMinutes(item.RoundedOvertimeMinutes);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";
    }

    public byte[] GenerateBranchesExcel(IEnumerable<BranchDto> branches)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sucursales");

        // Headers
        worksheet.Cell(1, 1).Value = "Nombre";
        worksheet.Cell(1, 2).Value = "Descripción";
        worksheet.Cell(1, 3).Value = "Dirección";
        
        var header = worksheet.Range("A1:C1");
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;

        int row = 2;
        foreach (var item in branches)
        {
            worksheet.Cell(row, 1).Value = item.Name;
            worksheet.Cell(row, 2).Value = item.Description;
            worksheet.Cell(row, 3).Value = item.Address;
            row++;
        }
        
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerateDepartmentsExcel(IEnumerable<DepartmentDto> departments)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Departamentos");

        // Headers
        worksheet.Cell(1, 1).Value = "Nombre";
        worksheet.Cell(1, 2).Value = "Descripción";
        
        var header = worksheet.Range("A1:B1");
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;

        int row = 2;
        foreach (var item in departments)
        {
            worksheet.Cell(row, 1).Value = item.Name;
            worksheet.Cell(row, 2).Value = item.Description;
            row++;
        }
        
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerateEmployeesExcel(IEnumerable<EmployeeDto> employees)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Empleados");

        // Headers
        worksheet.Cell(1, 1).Value = "ID";
        worksheet.Cell(1, 2).Value = "Nombre";
        worksheet.Cell(1, 3).Value = "Apellido";
        worksheet.Cell(1, 4).Value = "Email";
        worksheet.Cell(1, 5).Value = "Telefono";
        worksheet.Cell(1, 6).Value = "Fecha Contratación";
        worksheet.Cell(1, 7).Value = "Genero";
        worksheet.Cell(1, 8).Value = "Sucursal";
        worksheet.Cell(1, 9).Value = "Departamento";
        worksheet.Cell(1, 10).Value = "Puesto";
        worksheet.Cell(1, 11).Value = "Horario";
        worksheet.Cell(1, 12).Value = "Dia Descanso";

        var header = worksheet.Range("A1:L1");
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;

        int row = 2;
        foreach (var emp in employees)
        {
            worksheet.Cell(row, 1).Value = emp.Id;
            worksheet.Cell(row, 2).Value = emp.FirstName;
            worksheet.Cell(row, 3).Value = emp.LastName;
            worksheet.Cell(row, 4).Value = emp.Email;
            worksheet.Cell(row, 5).Value = emp.PhoneNumber;
            worksheet.Cell(row, 6).Value = emp.HireDate.ToString("dd/MM/yyyy");
            worksheet.Cell(row, 7).Value = emp.Gender.ToString();
            worksheet.Cell(row, 8).Value = emp.BranchName;
            worksheet.Cell(row, 9).Value = emp.DepartmentName;
            worksheet.Cell(row, 10).Value = emp.PositionName;
            worksheet.Cell(row, 11).Value = emp.ScheduleName;
            worksheet.Cell(row, 12).Value = emp.RestDayName;
            row++;
        }
        
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GeneratePositionsExcel(IEnumerable<PositionDto> positions)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Puestos");

        // Headers
        worksheet.Cell(1, 1).Value = "Nombre";
        worksheet.Cell(1, 2).Value = "Descripción";
        worksheet.Cell(1, 3).Value = "Salario Base";

        var header = worksheet.Range("A1:C1");
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;

        int row = 2;
        foreach (var item in positions)
        {
            worksheet.Cell(row, 1).Value = item.Name;
            worksheet.Cell(row, 2).Value = item.Description;
            worksheet.Cell(row, 3).Value = item.BaseSalary;
            row++;
        }
        
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerateAttendanceCardsPdf(
        Dictionary<(string EmployeeId, string EmployeeName), List<AttendanceReportViewDto>> groupedData, 
        DateTime startDate, 
        DateTime endDate)
    {
        var durationDays = (endDate.Date - startDate.Date).TotalDays + 1;
        bool useHalfPage = durationDays <= 7;
        
        return Document.Create(container =>
        {
            if (useHalfPage)
            {
                var groupsList = groupedData.ToList();
                for (int i = 0; i < groupsList.Count; i += 2) 
                {
                    var item1 = groupsList[i];
                    var item2 = (i + 1 < groupsList.Count) ? (KeyValuePair<(string, string), List<AttendanceReportViewDto>>?)groupsList[i + 1] : null;

                    container.Page(page =>
                    {
                        page.Size(PageSizes.Letter);
                        page.Margin(1, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                        page.Content().Column(col => 
                        {
                            // Card 1
                            // Use Height to constrain it to half page (Letter height ~28cm, margin 2cm = 26cm. Half ~13cm).
                            // We use 12.5cm to be safe.
                            col.Item().Height(12.5f, Unit.Centimetre).Element(c => ComposeAttendanceCard(c, item1, startDate, endDate));
                            
                            if (item2.HasValue)
                            {
                                // Cut Line
                                col.Item().PaddingVertical(0.2f, Unit.Centimetre)
                                   .BorderBottom(1).BorderColor(Colors.Grey.Medium);
                                
                                // Card 2
                                col.Item().PaddingTop(0.2f, Unit.Centimetre).Height(12.5f, Unit.Centimetre).Element(c => ComposeAttendanceCard(c, item2.Value, startDate, endDate));
                            }
                        });
                    });
                }
            }
            else 
            {
                // Normal behavior: One card per page
                foreach (var group in groupedData)
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.Letter);
                        page.Margin(1, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                        page.Content().Element(c => ComposeAttendanceCard(c, group, startDate, endDate));
                    });
                }
            }
        }).GeneratePdf();
    }

    private void ComposeAttendanceCard(IContainer container, KeyValuePair<(string EmployeeId, string EmployeeName), List<AttendanceReportViewDto>> group, DateTime startDate, DateTime endDate)
    {
         var employeeInfo = group.Key;
         var records = group.Value.OrderBy(x => x.Date).ToList();
         var totalOvertimeMinutes = records.Sum(x => x.RoundedOvertimeMinutes);

         container.Column(col => 
         {
            // Header
            col.Item().Column(header => 
            {
                header.Item().AlignCenter().Text("CARNICERIAS LA BLANQUITA").Bold().FontSize(14);
                header.Item().AlignCenter().Text("TARJETA DE ASISTENCIA").Bold().FontSize(12);
                header.Item().AlignCenter().Text($"Del {startDate:dd/MM/yyyy} al {endDate:dd/MM/yyyy}");
                header.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Text($"No. {employeeInfo.EmployeeId}").Bold();
                    row.RelativeItem().AlignRight().Text(employeeInfo.EmployeeName).Bold();
                });
                header.Item().PaddingTop(5).LineHorizontal(1);
            });

            // Table Section
             col.Item().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(); // Date
                    columns.RelativeColumn(); // Day
                    columns.RelativeColumn(); // In
                    columns.RelativeColumn(); // Out
                    columns.RelativeColumn(); // Overtime
                });

                // Table Header
                table.Header(header =>
                {
                    header.Cell().Border(1).Background(Colors.Grey.Lighten3).AlignCenter().Padding(2).Text("Fecha").Bold();
                    header.Cell().Border(1).Background(Colors.Grey.Lighten3).AlignCenter().Padding(2).Text("Día").Bold();
                    header.Cell().Border(1).Background(Colors.Grey.Lighten3).AlignCenter().Padding(2).Text("Entrada").Bold();
                    header.Cell().Border(1).Background(Colors.Grey.Lighten3).AlignCenter().Padding(2).Text("Salida").Bold();
                    header.Cell().Border(1).Background(Colors.Grey.Lighten3).AlignCenter().Padding(2).Text("Hrs. Extra").Bold();
                });

                // Table Body
                foreach (var record in records)
                {
                    table.Cell().Border(1).AlignCenter().Padding(2).Text(record.Date.ToString("dd/MM/yyyy"));
                    table.Cell().Border(1).AlignCenter().Padding(2).Text(record.Date.ToString("ddd"));
                    var inStr = FormatDateTime(record.ActualCheckIn, record.Date);
                    if (string.IsNullOrEmpty(inStr) && record.MissingCheckIn) inStr = "--";

                    var outStr = FormatDateTime(record.ActualCheckOut, record.Date);
                    if (string.IsNullOrEmpty(outStr) && record.MissingCheckOut) outStr = "--";

                    table.Cell().Border(1).AlignCenter().Padding(2).Text(inStr);
                    table.Cell().Border(1).AlignCenter().Padding(2).Text(outStr);
                    table.Cell().Border(1).AlignCenter().Padding(2).Text(FormatMinuteString(record.RoundedOvertimeMinutes));
                }

                // Table Footer (Total)
                table.Footer(footer =>
                {
                    footer.Cell().ColumnSpan(4).Border(1).AlignRight().Padding(2).Text("Total Horas Extra:").Bold();
                    footer.Cell().Border(1).AlignCenter().Padding(2).Text(FormatMinuteString(totalOvertimeMinutes)).Bold();
                });
            });
            
            // Signature Section 
            col.Item().PaddingTop(20).Row(row => 
            {
                 // Signature
                row.RelativeItem().Column(c =>
                {
                    c.Item().AlignCenter().Container().Width(150).Height(40).BorderBottom(1).BorderColor(Colors.Black); 
                    c.Item().AlignCenter().PaddingTop(5).Text("Firma").FontSize(9);
                });

                // Fingerprint
                row.RelativeItem().Column(c =>
                {
                    c.Item().AlignCenter().Container().Width(80).Height(60).Border(1).BorderColor(Colors.Black); 
                    c.Item().AlignCenter().PaddingTop(5).Text("Huella").FontSize(9);
                });
            });
         });
    }


    
    private string FormatMinuteString(int minutes)
    {
        if (minutes == 0) return "00:00";
        var ts = TimeSpan.FromMinutes(minutes);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";
    }

    private string FormatDateTime(DateTime? dt, DateTime referenceDate, string nullPlaceholder = "")
    {
        if (!dt.HasValue) return nullPlaceholder;
        // If it's the same date, show only time. Otherwise show Date + Time
        return (dt.Value.Date == referenceDate.Date) 
            ? dt.Value.ToString("HH:mm") 
            : dt.Value.ToString("dd/MM/yyyy HH:mm");
    }
}
