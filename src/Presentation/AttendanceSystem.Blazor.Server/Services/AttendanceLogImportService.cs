using ClosedXML.Excel;
using System.Globalization;

namespace AttendanceSystem.Blazor.Server.Services;

public record ImportedLogEntry(string EmployeeId, DateTime DateTime, string Type);
public record ImportResult(List<ImportedLogEntry> ValidEntries, List<string> Errors);

public class AttendanceLogImportService
{
    public async Task<ImportResult> ProcessFileAsync(Stream stream, string fileName)
    {
        var result = new ImportResult(new List<ImportedLogEntry>(), new List<string>());
        
        try 
        {
            if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                ProcessExcel(stream, result);
            }
            else if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                await ProcessCsv(stream, result);
            }
            else
            {
                result.Errors.Add("Formato de archivo no soportado. Use .xlsx o .csv");
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error crítico procesando archivo: {ex.Message}");
        }
        
        return result;
    }

    private void ProcessExcel(Stream stream, ImportResult result)
    {
        try
        {
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet == null)
            {
                result.Errors.Add("El archivo Excel está vacío o no tiene hojas.");
                return;
            }

            var rows = worksheet.RangeUsed().RowsUsed().Skip(1); // Skip header

            foreach (var row in rows)
            {
                try
                {
                    var empId = row.Cell(1).GetValue<string>();
                    
                    DateTime date;
                    if (!row.Cell(2).TryGetValue(out date))
                    {
                         var dateStr = row.Cell(2).GetValue<string>();
                         if (!DateTime.TryParse(dateStr, out date))
                         {
                             result.Errors.Add($"Fila {row.RowNumber()}: Fecha inválida.");
                             continue;
                         }
                    }

                    TimeSpan time;
                    if (row.Cell(3).DataType == XLDataType.TimeSpan)
                    {
                        time = row.Cell(3).GetValue<TimeSpan>();
                    }
                    else if (row.Cell(3).DataType == XLDataType.DateTime)
                    {
                        time = row.Cell(3).GetValue<DateTime>().TimeOfDay;
                    }
                    else
                    {
                         var timeStr = row.Cell(3).GetValue<string>();
                         if (!TimeSpan.TryParse(timeStr, out time))
                         {
                             result.Errors.Add($"Fila {row.RowNumber()}: Hora inválida.");
                             continue;
                         }
                    }

                    var type = row.Cell(4).GetValue<string>(); 
                    var dateTime = date.Date + time;

                    if (string.IsNullOrWhiteSpace(empId)) 
                    {
                        result.Errors.Add($"Fila {row.RowNumber()}: EmployeeId vacío.");
                        continue;
                    }
                    
                    result.ValidEntries.Add(new ImportedLogEntry(empId, dateTime, type));
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Fila {row.RowNumber()}: Error procesando fila: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error leyendo Excel: {ex.Message}");
        }
    }

    private async Task ProcessCsv(Stream stream, ImportResult result)
    {
        try
        {
            using var reader = new StreamReader(stream);
            string? line;
            int lineNumber = 0;
            
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNumber++;
                if (lineNumber == 1) continue; // Skip header

                var parts = line.Split(',');
                if (parts.Length < 4) continue;

                try
                {
                    var empId = parts[0].Trim();
                    if (!DateTime.TryParse(parts[1].Trim(), out var date)) 
                    {
                        result.Errors.Add($"Fila {lineNumber}: Fecha inválida.");
                        continue;
                    }
                    if (!TimeSpan.TryParse(parts[2].Trim(), out var time)) 
                    {
                        result.Errors.Add($"Fila {lineNumber}: Hora inválida.");
                        continue;
                    }
                    var type = parts[3].Trim();

                    var dateTime = date.Date + time;

                    if (string.IsNullOrWhiteSpace(empId)) 
                    {
                        result.Errors.Add($"Fila {lineNumber}: EmployeeId vacío.");
                        continue;
                    }
                    
                    result.ValidEntries.Add(new ImportedLogEntry(empId, dateTime, type));
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Fila {lineNumber}: Error procesando fila: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error leyendo CSV: {ex.Message}");
        }
    }
}
