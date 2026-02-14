using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Employees;
using AttendanceSystem.Domain.Enumerations;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using Colors = QuestPDF.Helpers.Colors;

namespace AttendanceSystem.Infrastructure.Services;

public class ReportExportService : IReportExportService
{
    public byte[] GenerateExcel(IEnumerable<AttendanceReportViewDto> attendanceData, DateTime startDate, DateTime endDate, string companyName, byte[]? companyLogo)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Reporte de Asistencia");

        // 1. Title and Info
        var titleRange = worksheet.Range("A1:L1");
        titleRange.Merge().Value = companyName;
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

        if (companyLogo != null && companyLogo.Length > 0)
        {
            try 
            {
               using var ms = new MemoryStream(companyLogo);
               var pic = worksheet.AddPicture(ms).MoveTo(worksheet.Cell("A1"));
               pic.Width = 60; // Adjust as needed
               pic.Height = 60;
            }
            catch { /* Ignore image errors in excel */ }
        }

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

    public byte[] GeneratePdf(IEnumerable<AttendanceReportViewDto> attendanceData, DateTime startDate, DateTime endDate, string companyName, byte[]? companyLogo)
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
                        if (companyLogo != null && companyLogo.Length > 0)
                        {
                            row.ConstantItem(60).PaddingRight(10).Image(companyLogo).FitArea();
                        }

                        row.RelativeItem().Column(col => 
                        {
                            col.Item().AlignCenter().Text(companyName).SemiBold().FontSize(18).FontColor(Colors.Blue.Darken2);
                            
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
        DateTime endDate,
        string companyName,
        byte[]? companyLogo)
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
                            col.Item().Height(12.5f, Unit.Centimetre).Element(c => ComposeAttendanceCard(c, item1, startDate, endDate, companyName, companyLogo));
                            
                            if (item2.HasValue)
                            {
                                // Cut Line
                                col.Item().PaddingVertical(0.2f, Unit.Centimetre)
                                   .BorderBottom(1).BorderColor(Colors.Grey.Medium);
                                
                                // Card 2
                                col.Item().PaddingTop(0.2f, Unit.Centimetre).Height(12.5f, Unit.Centimetre).Element(c => ComposeAttendanceCard(c, item2.Value, startDate, endDate, companyName, companyLogo));
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

                        page.Content().Element(c => ComposeAttendanceCard(c, group, startDate, endDate, companyName, companyLogo));
                    });
                }
            }
        }).GeneratePdf();
    }

    private void ComposeAttendanceCard(IContainer container, KeyValuePair<(string EmployeeId, string EmployeeName), List<AttendanceReportViewDto>> group, DateTime startDate, DateTime endDate, string companyName, byte[]? companyLogo)
    {
         var employeeInfo = group.Key;
         var records = group.Value.OrderBy(x => x.Date).ToList();
         var totalOvertimeMinutes = records.Sum(x => x.RoundedOvertimeMinutes);

         container.Column(col => 
         {
            // Header
            col.Item().Column(header => 
            {
                header.Item().Row(r => 
                {
                    if (companyLogo != null && companyLogo.Length > 0)
                    {
                        r.ConstantItem(50).PaddingRight(5).Image(companyLogo).FitArea();
                    }
                    
                    r.RelativeItem().Column(c => {
                         c.Item().AlignCenter().Text(companyName).Bold().FontSize(14);
                         c.Item().AlignCenter().Text("TARJETA DE ASISTENCIA").Bold().FontSize(12);
                         c.Item().AlignCenter().Text($"Del {startDate:dd/MM/yyyy} al {endDate:dd/MM/yyyy}");
                    });
                });

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
            col.Item().PaddingTop(15).Row(row => 
            {
                 // Legal Text
                row.RelativeItem(2).PaddingRight(10).Text("Previamente de asentar mi firma y mi huella digital en el presente documento, manifiesto que he revisado la relacion de entradas y salidas que el mismo contiene, por lo que acepto de conformidad dicha relacion de fechas y horas que se plasman en esta tarjeta, pues reflejan Fielmente los registros que hice de mis ingresos y egresos en el presente centro de trabajo.")
                   .FontSize(6).Justify();

                 // Signature
                row.RelativeItem(1).Column(c =>
                {
                    c.Item().AlignCenter().Container().Width(120).Height(35).BorderBottom(1).BorderColor(Colors.Black); 
                    c.Item().AlignCenter().PaddingTop(2).Text("Firma").FontSize(8);
                });

                // Fingerprint
                row.RelativeItem(1).Column(c =>
                {
                    c.Item().AlignCenter().Container().Width(55).Height(55).Border(1).BorderColor(Colors.Black); 
                    c.Item().AlignCenter().PaddingTop(2).Text("Huella").FontSize(8);
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

    public byte[] GenerateAdvancedAbsenceExcel(IEnumerable<AdvancedReportSummaryDto> data, DateTime start, DateTime end, bool detailed, bool specificDate, bool showBranch)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Faltas");

        // Styling
        var headerStyle = workbook.Style;
        headerStyle.Font.Bold = true;
        headerStyle.Fill.BackgroundColor = XLColor.LightGray;
        headerStyle.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; // Default for header

        int currentRow = 1;

        if (specificDate)
        {
            // --- Specific Date Format ---
            // Columns: Fecha | ID | Nombre | Dept | Puesto | [Sucursal] | Falta
            int col = 1;
            worksheet.Cell(currentRow, col++).Value = "Fecha";
            worksheet.Cell(currentRow, col++).Value = "ID de Empleado";
            worksheet.Cell(currentRow, col++).Value = "Nombre";
            worksheet.Cell(currentRow, col++).Value = "Departamento";
            worksheet.Cell(currentRow, col++).Value = "Puesto";
            if (showBranch) worksheet.Cell(currentRow, col++).Value = "Sucursal";
            worksheet.Cell(currentRow, col++).Value = "Falta";
            
            // Apply header style explicitly to the range
            var range = worksheet.Range(currentRow, 1, currentRow, col - 1);
            range.Style.Font.Bold = true;
            range.Style.Fill.BackgroundColor = XLColor.LightGray;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            currentRow++;

            foreach (var summary in data)
            {
                foreach (var detail in summary.Details)
                {
                    if (detail.IsAbsent)
                    {
                        col = 1;
                        worksheet.Cell(currentRow, col++).Value = detail.Date.ToShortDateString();
                        worksheet.Cell(currentRow, col++).Value = summary.EmployeeId; 
                        worksheet.Cell(currentRow, col++).Value = summary.EmployeeName;
                        worksheet.Cell(currentRow, col++).Value = summary.DepartmentName;
                        worksheet.Cell(currentRow, col++).Value = summary.PositionName;
                        if (showBranch) worksheet.Cell(currentRow, col++).Value = summary.BranchName;
                        worksheet.Cell(currentRow, col++).Value = "1FINJ";
                        currentRow++;
                    }
                }
            }
        }
        else
        {
            // --- Range / Period Format ---
            if (!detailed)
            {
                // --- Summary View ---
                // Columns: ID de empleado | Nombre | Total de faltas
                worksheet.Cell(currentRow, 1).Value = "ID de Empleado";
                worksheet.Cell(currentRow, 2).Value = "Nombre";
                worksheet.Cell(currentRow, 3).Value = "Total de Faltas";

                var range = worksheet.Range(currentRow, 1, currentRow, 3);
                range.Style.Font.Bold = true;
                range.Style.Fill.BackgroundColor = XLColor.LightGray;
                range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                currentRow++;

                foreach (var summary in data)
                {
                    if (summary.Count > 0)
                    {
                        worksheet.Cell(currentRow, 1).Value = summary.EmployeeId;
                        worksheet.Cell(currentRow, 2).Value = summary.EmployeeName;
                        worksheet.Cell(currentRow, 3).Value = summary.Count;
                        currentRow++;
                    }
                }
            }
            else
            {
                // --- Detailed View (Pivot) ---
                // Columns: ID | Nombre | Dept | Puesto | [Sucursal] | [Date 1] | [Date 2] ... | Total
                int col = 1;
                worksheet.Cell(currentRow, col++).Value = "ID de Empleado";
                worksheet.Cell(currentRow, col++).Value = "Nombre";
                worksheet.Cell(currentRow, col++).Value = "Departamento";
                worksheet.Cell(currentRow, col++).Value = "Puesto";
                if (showBranch) worksheet.Cell(currentRow, col++).Value = "Sucursal";
                
                // Save start column for dates
                int dateColStart = col;

                // Using start/end from arguments to build columns
                var dates = new List<DateTime>();
                var currentDt = start.Date;
                var endDt = end.Date;

                while (currentDt <= endDt)
                {
                    dates.Add(currentDt);
                    worksheet.Cell(currentRow, col).Value = currentDt.ToString("dd/MM");
                    col++;
                    currentDt = currentDt.AddDays(1);
                }
                
                int totalColIndex = col;
                worksheet.Cell(currentRow, totalColIndex).Value = "Total";

                var range = worksheet.Range(currentRow, 1, currentRow, totalColIndex);
                range.Style.Font.Bold = true;
                range.Style.Fill.BackgroundColor = XLColor.LightGray;
                range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                currentRow++;

                foreach (var summary in data)
                {
                    if (summary.Count == 0) continue;

                    col = 1;
                    worksheet.Cell(currentRow, col++).Value = summary.EmployeeId;
                    worksheet.Cell(currentRow, col++).Value = summary.EmployeeName;
                    worksheet.Cell(currentRow, col++).Value = summary.DepartmentName;
                    worksheet.Cell(currentRow, col++).Value = summary.PositionName;
                    if (showBranch) worksheet.Cell(currentRow, col++).Value = summary.BranchName;

                    var absenceDates = summary.Details
                        .Where(d => d.IsAbsent)
                        .Select(d => d.Date.Date)
                        .ToHashSet();

                    int dateCol = dateColStart;
                    foreach (var dt in dates)
                    {
                        if (absenceDates.Contains(dt))
                        {
                            worksheet.Cell(currentRow, dateCol).Value = "1FINJ";
                            worksheet.Cell(currentRow, dateCol).Style.Font.FontColor = XLColor.Red;
                            worksheet.Cell(currentRow, dateCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }
                        dateCol++;
                    }

                    worksheet.Cell(currentRow, totalColIndex).Value = summary.Count;
                    currentRow++;
                }
            }
        }
        
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerateWorkedRestDayExcel(IEnumerable<AdvancedReportSummaryDto> data, DateTime start, DateTime end, bool detailed, bool showBranch)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Descanso Trabajado");

        // Styling
        var headerStyle = workbook.Style;
        headerStyle.Font.Bold = true;
        headerStyle.Fill.BackgroundColor = XLColor.LightGray;
        headerStyle.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int currentRow = 1;

        if (detailed)
        {
            // --- Detailed View (Pivot/Expanded) ---
            // Requested: ID | Nombre | Dept | Puesto | Sucursal (Optional) | [Date 1] | [Date 2] ... | Total
            // Note: User asked for "Detailed" to have Date Columns with 1DFT.
            
            int col = 1;
            worksheet.Cell(currentRow, col++).Value = "ID de Empleado";
            worksheet.Cell(currentRow, col++).Value = "Nombre";
            worksheet.Cell(currentRow, col++).Value = "Departamento";
            worksheet.Cell(currentRow, col++).Value = "Puesto";
            if (showBranch) worksheet.Cell(currentRow, col++).Value = "Sucursal";
            
            int dateColStart = col;

            var dates = new List<DateTime>();
            var currentDt = start.Date;
            var endDt = end.Date;

            while (currentDt <= endDt)
            {
                dates.Add(currentDt);
                worksheet.Cell(currentRow, col).Value = currentDt.ToString("dd/MM");
                col++;
                currentDt = currentDt.AddDays(1);
            }

            int totalColIndex = col;
            worksheet.Cell(currentRow, totalColIndex).Value = "Total";

            var range = worksheet.Range(currentRow, 1, currentRow, totalColIndex);
            range.Style.Font.Bold = true;
            range.Style.Fill.BackgroundColor = XLColor.LightGray;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            currentRow++;

            foreach (var summary in data)
            {
                if (summary.Count == 0) continue;

                col = 1;
                worksheet.Cell(currentRow, col++).Value = summary.EmployeeId;
                worksheet.Cell(currentRow, col++).Value = summary.EmployeeName;
                worksheet.Cell(currentRow, col++).Value = summary.DepartmentName;
                worksheet.Cell(currentRow, col++).Value = summary.PositionName;
                if (showBranch) worksheet.Cell(currentRow, col++).Value = summary.BranchName;

                var workedRestDays = summary.Details
                    .Where(d => d.WorkedOnRestDay)
                    .Select(d => d.Date.Date)
                    .ToHashSet();

                int dateCol = dateColStart;
                foreach (var dt in dates)
                {
                    if (workedRestDays.Contains(dt))
                    {
                        worksheet.Cell(currentRow, dateCol).Value = "1DFT";
                        worksheet.Cell(currentRow, dateCol).Style.Font.FontColor = XLColor.Green;
                        worksheet.Cell(currentRow, dateCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }
                    dateCol++;
                }

                worksheet.Cell(currentRow, totalColIndex).Value = summary.Count;
                currentRow++;
            }
        }
        else
        {
            // --- Summary View ---
            // ID | Nombre | Dept | Puesto | Sucursal (Optional) | Total
            int col = 1;
            worksheet.Cell(currentRow, col++).Value = "ID de Empleado";
            worksheet.Cell(currentRow, col++).Value = "Nombre";
            worksheet.Cell(currentRow, col++).Value = "Departamento";
            worksheet.Cell(currentRow, col++).Value = "Puesto";
            if (showBranch) worksheet.Cell(currentRow, col++).Value = "Sucursal";
            worksheet.Cell(currentRow, col++).Value = "Total Descansos Trabajados";

            var range = worksheet.Range(currentRow, 1, currentRow, col - 1);
            range.Style.Font.Bold = true;
            range.Style.Fill.BackgroundColor = XLColor.LightGray;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            currentRow++;

            foreach (var summary in data)
            {
                if (summary.Count > 0)
                {
                    col = 1;
                    worksheet.Cell(currentRow, col++).Value = summary.EmployeeId;
                    worksheet.Cell(currentRow, col++).Value = summary.EmployeeName;
                    worksheet.Cell(currentRow, col++).Value = summary.DepartmentName;
                    worksheet.Cell(currentRow, col++).Value = summary.PositionName;
                    if (showBranch) worksheet.Cell(currentRow, col++).Value = summary.BranchName;
                    worksheet.Cell(currentRow, col++).Value = summary.Count;
                    currentRow++;
                }
            }
        }
        
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
    public byte[] GenerateLateArrivalExcel(IEnumerable<AdvancedReportSummaryDto> data, DateTime start, DateTime end, bool detailed, bool specificDate, bool showBranch)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Retardos");

        // Styling
        var headerStyle = workbook.Style;
        headerStyle.Font.Bold = true;
        headerStyle.Fill.BackgroundColor = XLColor.LightGray;
        headerStyle.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int currentRow = 1;

        if (specificDate)
        {
            // --- Specific Date Format ---
            int col = 1;
            worksheet.Cell(currentRow, col++).Value = "Fecha";
            worksheet.Cell(currentRow, col++).Value = "ID de Empleado";
            worksheet.Cell(currentRow, col++).Value = "Nombre";
            worksheet.Cell(currentRow, col++).Value = "Departamento";
            worksheet.Cell(currentRow, col++).Value = "Puesto";
            if (showBranch) worksheet.Cell(currentRow, col++).Value = "Sucursal";
            worksheet.Cell(currentRow, col++).Value = "Retardo";
            
            var range = worksheet.Range(currentRow, 1, currentRow, col - 1);
            range.Style.Font.Bold = true;
            range.Style.Fill.BackgroundColor = XLColor.LightGray;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            currentRow++;

            foreach (var summary in data)
            {
                foreach (var detail in summary.Details)
                {
                    if (detail.LateMinutes > 0)
                    {
                        col = 1;
                        worksheet.Cell(currentRow, col++).Value = detail.Date.ToShortDateString();
                        worksheet.Cell(currentRow, col++).Value = summary.EmployeeId; 
                        worksheet.Cell(currentRow, col++).Value = summary.EmployeeName;
                        worksheet.Cell(currentRow, col++).Value = summary.DepartmentName;
                        worksheet.Cell(currentRow, col++).Value = summary.PositionName;
                        if (showBranch) worksheet.Cell(currentRow, col++).Value = summary.BranchName;
                        worksheet.Cell(currentRow, col++).Value = "1RET";
                        worksheet.Cell(currentRow, col-1).Style.Font.FontColor = XLColor.Orange;
                        currentRow++;
                    }
                }
            }
        }
        else
        {
            if (!detailed)
            {
                // --- Summary View ---
                worksheet.Cell(currentRow, 1).Value = "ID de Empleado";
                worksheet.Cell(currentRow, 2).Value = "Nombre";
                worksheet.Cell(currentRow, 3).Value = "Total Retardos";

                var range = worksheet.Range(currentRow, 1, currentRow, 3);
                range.Style.Font.Bold = true;
                range.Style.Fill.BackgroundColor = XLColor.LightGray;
                range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                currentRow++;

                foreach (var summary in data)
                {
                    if (summary.Count > 0)
                    {
                        worksheet.Cell(currentRow, 1).Value = summary.EmployeeId;
                        worksheet.Cell(currentRow, 2).Value = summary.EmployeeName;
                        worksheet.Cell(currentRow, 3).Value = summary.Count;
                        currentRow++;
                    }
                }
            }
            else
            {
                // --- Detailed View (Pivot) ---
                int col = 1;
                worksheet.Cell(currentRow, col++).Value = "ID de Empleado";
                worksheet.Cell(currentRow, col++).Value = "Nombre";
                worksheet.Cell(currentRow, col++).Value = "Departamento";
                worksheet.Cell(currentRow, col++).Value = "Puesto";
                if (showBranch) worksheet.Cell(currentRow, col++).Value = "Sucursal";
                
                int dateColStart = col;

                var dates = new List<DateTime>();
                var currentDt = start.Date;
                var endDt = end.Date;

                while (currentDt <= endDt)
                {
                    dates.Add(currentDt);
                    worksheet.Cell(currentRow, col).Value = currentDt.ToString("dd/MM");
                    col++;
                    currentDt = currentDt.AddDays(1);
                }
                
                int totalColIndex = col;
                worksheet.Cell(currentRow, totalColIndex).Value = "Total";

                var range = worksheet.Range(currentRow, 1, currentRow, totalColIndex);
                range.Style.Font.Bold = true;
                range.Style.Fill.BackgroundColor = XLColor.LightGray;
                range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                currentRow++;

                foreach (var summary in data)
                {
                    if (summary.Count == 0) continue;

                    col = 1;
                    worksheet.Cell(currentRow, col++).Value = summary.EmployeeId;
                    worksheet.Cell(currentRow, col++).Value = summary.EmployeeName;
                    worksheet.Cell(currentRow, col++).Value = summary.DepartmentName;
                    worksheet.Cell(currentRow, col++).Value = summary.PositionName;
                    if (showBranch) worksheet.Cell(currentRow, col++).Value = summary.BranchName;

                    var lateDates = summary.Details
                        .Where(d => d.LateMinutes > 0)
                        .Select(d => d.Date.Date)
                        .ToHashSet();

                    int dateCol = dateColStart;
                    foreach (var dt in dates)
                    {
                        if (lateDates.Contains(dt))
                        {
                            worksheet.Cell(currentRow, dateCol).Value = "1RET";
                            worksheet.Cell(currentRow, dateCol).Style.Font.FontColor = XLColor.Orange;
                            worksheet.Cell(currentRow, dateCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }
                        dateCol++;
                    }

                    worksheet.Cell(currentRow, totalColIndex).Value = summary.Count;
                    currentRow++;
                }
            }
        }
        
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private string FormatMinuteString(double minutes)
    {
        if (minutes == 0) return "--:--";
        var ts = TimeSpan.FromMinutes(minutes);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";
    }

    public byte[] GenerateOvertimeExcel(IEnumerable<AdvancedReportSummaryDto> data, DateTime start, DateTime end, bool detailed, bool specificDate, bool showBranch)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Horas Extra");

        // Styling
        var headerStyle = workbook.Style;
        headerStyle.Font.Bold = true;
        headerStyle.Fill.BackgroundColor = XLColor.LightGray;
        headerStyle.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        int currentRow = 1;

        if (specificDate)
        {
            // --- Specific Date Format ---
            int col = 1;
            worksheet.Cell(currentRow, col++).Value = "Fecha";
            worksheet.Cell(currentRow, col++).Value = "ID de Empleado";
            worksheet.Cell(currentRow, col++).Value = "Nombre";
            worksheet.Cell(currentRow, col++).Value = "Departamento";
            worksheet.Cell(currentRow, col++).Value = "Puesto";
            if (showBranch) worksheet.Cell(currentRow, col++).Value = "Sucursal";
            worksheet.Cell(currentRow, col++).Value = "Horas Extra";

            var range = worksheet.Range(currentRow, 1, currentRow, col - 1);
            range.Style.Font.Bold = true;
            range.Style.Fill.BackgroundColor = XLColor.LightGray;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            currentRow++;

            foreach (var summary in data)
            {
                foreach (var detail in summary.Details)
                {
                    if (detail.OvertimeMinutes > 0)
                    {
                        col = 1;
                        worksheet.Cell(currentRow, col++).Value = detail.Date.ToShortDateString();
                        worksheet.Cell(currentRow, col++).Value = summary.EmployeeId;
                        worksheet.Cell(currentRow, col++).Value = summary.EmployeeName;
                        worksheet.Cell(currentRow, col++).Value = summary.DepartmentName;
                        worksheet.Cell(currentRow, col++).Value = summary.PositionName;
                        if (showBranch) worksheet.Cell(currentRow, col++).Value = summary.BranchName;
                        
                        var otStr = FormatMinuteString(detail.OvertimeMinutes);
                        worksheet.Cell(currentRow, col++).Value = otStr;
                        worksheet.Cell(currentRow, col-1).Style.Font.FontColor = XLColor.Blue;
                        
                        currentRow++;
                    }
                }
            }
        }
        else
        {
            if (!detailed)
            {
                // --- Summary View ---
                worksheet.Cell(currentRow, 1).Value = "ID de Empleado";
                worksheet.Cell(currentRow, 2).Value = "Nombre";
                worksheet.Cell(currentRow, 3).Value = "Total Horas Extra";

                var range = worksheet.Range(currentRow, 1, currentRow, 3);
                range.Style.Font.Bold = true;
                range.Style.Fill.BackgroundColor = XLColor.LightGray;
                range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                currentRow++;

                foreach (var summary in data)
                {
                    var totalMins = summary.Details.Sum(d => d.OvertimeMinutes);
                    if (totalMins > 0)
                    {
                        worksheet.Cell(currentRow, 1).Value = summary.EmployeeId;
                        worksheet.Cell(currentRow, 2).Value = summary.EmployeeName;
                        worksheet.Cell(currentRow, 3).Value = FormatMinuteString(totalMins);
                        currentRow++;
                    }
                }
            }
            else
            {
                // --- Detailed View (Pivot) ---
                int col = 1;
                worksheet.Cell(currentRow, col++).Value = "ID de Empleado";
                worksheet.Cell(currentRow, col++).Value = "Nombre";
                worksheet.Cell(currentRow, col++).Value = "Departamento";
                worksheet.Cell(currentRow, col++).Value = "Puesto";
                if (showBranch) worksheet.Cell(currentRow, col++).Value = "Sucursal";
                
                int dateColStart = col;

                var dates = new List<DateTime>();
                var currentDt = start.Date;
                var endDt = end.Date;

                while (currentDt <= endDt)
                {
                    dates.Add(currentDt);
                    worksheet.Cell(currentRow, col).Value = currentDt.ToString("dd/MM");
                    col++;
                    currentDt = currentDt.AddDays(1);
                }
                
                int totalColIndex = col;
                worksheet.Cell(currentRow, totalColIndex).Value = "Total";

                var range = worksheet.Range(currentRow, 1, currentRow, totalColIndex);
                range.Style.Font.Bold = true;
                range.Style.Fill.BackgroundColor = XLColor.LightGray;
                range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                currentRow++;

                foreach (var summary in data)
                {
                    if (summary.Details.Sum(d => d.OvertimeMinutes) == 0) continue;

                    col = 1;
                    worksheet.Cell(currentRow, col++).Value = summary.EmployeeId;
                    worksheet.Cell(currentRow, col++).Value = summary.EmployeeName;
                    worksheet.Cell(currentRow, col++).Value = summary.DepartmentName;
                    worksheet.Cell(currentRow, col++).Value = summary.PositionName;
                    if (showBranch) worksheet.Cell(currentRow, col++).Value = summary.BranchName;

                    var otDict = summary.Details
                        .Where(d => d.OvertimeMinutes > 0)
                        .ToDictionary(d => d.Date.Date, d => d.OvertimeMinutes);

                    int dateCol = dateColStart;
                    foreach (var dt in dates)
                    {
                        if (otDict.ContainsKey(dt))
                        {
                            var mins = otDict[dt];
                            worksheet.Cell(currentRow, dateCol).Value = FormatMinuteString(mins);
                            worksheet.Cell(currentRow, dateCol).Style.Font.FontColor = XLColor.Blue;
                            worksheet.Cell(currentRow, dateCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }
                        dateCol++;
                    }

                    var totalMins = summary.Details.Sum(d => d.OvertimeMinutes);
                    worksheet.Cell(currentRow, totalColIndex).Value = FormatMinuteString(totalMins);
                    currentRow++;
                }
            }
        }
        
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
