using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs.Import;
using ClosedXML.Excel;

namespace AttendanceSystem.Infrastructure.Services;

public class ImportService : IImportService
{
    // --- Attendance Logs ---
    public async Task<ImportResult<ImportedLogEntryDto>> ParseAttendanceLogsAsync(Stream stream, string fileName)
    {
        var result = new ImportResult<ImportedLogEntryDto>();
        try
        {
            if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => ProcessLogsExcel(stream, result));
            }
            else if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                await ProcessLogsCsv(stream, result);
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

    private void ProcessLogsExcel(Stream stream, ImportResult<ImportedLogEntryDto> result)
    {
        try
        {
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet == null)
            {
                result.Errors.Add("Workbook sin hojas.");
                return;
            }

            var range = worksheet.RangeUsed();
            if (range == null) return;
            var rows = range.RowsUsed().Skip(1); 

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
                    
                    result.ValidEntries.Add(new ImportedLogEntryDto(empId, dateTime, type));
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Fila {row.RowNumber()}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error leyendo Excel: {ex.Message}");
        }
    }

    private async Task ProcessLogsCsv(Stream stream, ImportResult<ImportedLogEntryDto> result)
    {
        try
        {
            using var reader = new StreamReader(stream);
            string? line;
            int lineNumber = 0;
            
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNumber++;
                if (lineNumber == 1) continue; 

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
                    
                    result.ValidEntries.Add(new ImportedLogEntryDto(empId, dateTime, type));
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Fila {lineNumber}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error leyendo CSV: {ex.Message}");
        }
    }

    // --- Branches ---
    public async Task<ImportResult<ImportBranchDto>> ParseBranchesAsync(Stream stream)
    {
        return await Task.Run(() => 
        {
            var result = new ImportResult<ImportBranchDto>();
            try
            {
                using var workbook = new XLWorkbook(stream);
                var worksheet = workbook.Worksheets.FirstOrDefault();
                if (worksheet == null) return result;

                var range = worksheet.RangeUsed();
                if (range == null) return result;
                var rows = range.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    try
                    {
                        var name = row.Cell(1).GetValue<string>();
                        var desc = row.Cell(2).GetValue<string>();
                        var addr = row.Cell(3).GetValue<string>();

                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.ValidEntries.Add(new ImportBranchDto(name, desc, addr));
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Fila {row.RowNumber()}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error leyendo Excel: {ex.Message}");
            }
            return result;
        });
    }

    // --- Departments ---
    public async Task<ImportResult<ImportDepartmentDto>> ParseDepartmentsAsync(Stream stream)
    {
        return await Task.Run(() => 
        {
            var result = new ImportResult<ImportDepartmentDto>();
            try
            {
                using var workbook = new XLWorkbook(stream);
                var worksheet = workbook.Worksheets.FirstOrDefault();
                if (worksheet == null) return result;

                var range = worksheet.RangeUsed();
                if (range == null) return result;
                var rows = range.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    try
                    {
                        var name = row.Cell(1).GetValue<string>();
                        var desc = row.Cell(2).GetValue<string>();

                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.ValidEntries.Add(new ImportDepartmentDto(name, desc));
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Fila {row.RowNumber()}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error leyendo Excel: {ex.Message}");
            }
            return result;
        });
    }

    // --- Positions ---
    public async Task<ImportResult<ImportPositionDto>> ParsePositionsAsync(Stream stream)
    {
        return await Task.Run(() => 
        {
            var result = new ImportResult<ImportPositionDto>();
            try
            {
                using var workbook = new XLWorkbook(stream);
                var worksheet = workbook.Worksheets.FirstOrDefault();
                if (worksheet == null) return result;

                var range = worksheet.RangeUsed();
                if (range == null) return result;
                var rows = range.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    try
                    {
                        var name = row.Cell(1).GetValue<string>();
                        var desc = row.Cell(2).GetValue<string>();
                        
                        decimal baseSalary = 0;
                        if (!row.Cell(3).TryGetValue<decimal>(out baseSalary))
                            baseSalary = 0;

                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.ValidEntries.Add(new ImportPositionDto(name, desc, baseSalary));
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Fila {row.RowNumber()}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error leyendo Excel: {ex.Message}");
            }
            return result;
        });
    }

    // --- Employees ---
    public async Task<ImportResult<ImportEmployeeDto>> ParseEmployeesAsync(Stream stream)
    {
        return await Task.Run(() => 
        {
            var result = new ImportResult<ImportEmployeeDto>();
            try
            {
                using var workbook = new XLWorkbook(stream);
                var worksheet = workbook.Worksheets.FirstOrDefault();
                if (worksheet == null) return result;

                var range = worksheet.RangeUsed();
                if (range == null) return result;
                var rows = range.RowsUsed().Skip(1);

                foreach (var row in rows)
                {
                    try
                    {
                        var id = row.Cell(1).GetValue<string>();
                        if (string.IsNullOrWhiteSpace(id)) continue;

                        var firstName = row.Cell(2).GetValue<string>();
                        var lastName = row.Cell(3).GetValue<string>();
                        var email = row.Cell(4).GetValue<string>();
                        var branchName = row.Cell(5).GetValue<string>();
                        var deptName = row.Cell(6).GetValue<string>();
                        var posName = row.Cell(7).GetValue<string>();

                        DateTime hireDate;
                        if (!row.Cell(8).TryGetValue(out hireDate))
                        {
                            var hireDateStr = row.Cell(8).GetValue<string>();
                            if (!DateTime.TryParse(hireDateStr, out hireDate))
                                hireDate = DateTime.Today;
                        }

                        var gender = row.Cell(9).GetValue<string>();

                        result.ValidEntries.Add(new ImportEmployeeDto(
                            id, firstName, lastName, email, 
                            branchName, deptName, posName, 
                            gender, hireDate
                        ));
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Fila {row.RowNumber()}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error leyendo Excel: {ex.Message}");
            }
            return result;
        });
    }
}
