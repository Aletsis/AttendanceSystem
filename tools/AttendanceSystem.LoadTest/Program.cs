using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DependencyInjection;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Application.Features.Attendance.Decisions;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.DependencyInjection;
using AttendanceSystem.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AttendanceSystem.LoadTest;

/// <summary>
/// F07-01 / F07-09: banco de carga reproducible del escenario representativo (F00-08) sobre PostgreSQL real.
/// </summary>
public static class Program
{
    private const string TimeZone = "America/Mexico_City";

    public static async Task<int> Main(string[] args)
    {
        var options = LoadOptions.Parse(args);
        var admin = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=attendanceuser;Password=AttendancePass;";
        var adminBuilder = new NpgsqlConnectionStringBuilder(admin) { Database = "postgres" };
        var dbName = $"attendance_load_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var connection = new NpgsqlConnectionStringBuilder(admin) { Database = dbName, MaxPoolSize = 100 }.ConnectionString;

        await ExecuteAdminAsync(adminBuilder.ConnectionString, $"CREATE DATABASE \"{dbName}\"");
        Console.Error.WriteLine($"Base de carga: {dbName}");
        try
        {
            await using var provider = BuildProvider(connection);
            using (var scope = provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<AttendanceDbContext>().Database.MigrateAsync();
            }

            var dataset = await Seeder.SeedAsync(provider, options);
            Console.Error.WriteLine($"Sembrado: {dataset.Employees.Count} empleados, {dataset.Punches} marcajes, {dataset.Adjustments} ajustes.");

            var results = new List<ScenarioResult>();
            var start = options.Start;
            var end = options.Start.AddDays(options.Days - 1);

            results.Add(await Measure.RunAsync("Mes completo (primer cálculo)", provider, dataset.Employees.Count * options.Days,
                sp => sp.GetRequiredService<ISender>().Send(new ProcessDailyAttendanceCommand(start, end))));
            var firstFingerprint = await Fingerprint.ComputeAsync(provider);
            results.Add(await Measure.RunAsync("Mes completo (recálculo)", provider, dataset.Employees.Count * options.Days,
                sp => sp.GetRequiredService<ISender>().Send(new ProcessDailyAttendanceCommand(start, end))));
            var secondFingerprint = await Fingerprint.ComputeAsync(provider);
            Console.Error.WriteLine($"Huella primer cálculo: {firstFingerprint}");
            Console.Error.WriteLine($"Huella recálculo:      {secondFingerprint}");
            results.Add(await Measure.RunAsync("Un empleado, mes", provider, options.Days,
                sp => sp.GetRequiredService<ISender>().Send(new ProcessDailyAttendanceCommand(start, end, EmployeeId: dataset.SampleEmployee))));
            var branchEmployees = dataset.Employees.Count(e => e.BranchId == dataset.SampleBranch);
            results.Add(await Measure.RunAsync("Una sucursal, mes", provider, branchEmployees * options.Days,
                sp => sp.GetRequiredService<ISender>().Send(new ProcessDailyAttendanceCommand(start, end, BranchId: dataset.SampleBranch))));

            var import = Seeder.ImportBatch(dataset, end.AddDays(1), options.Seed);
            results.Add(await Measure.RunAsync($"Importación masiva ({import.Count} marcajes)", provider, 0,
                async sp => await ImportAsync(sp, import)));
            results.Add(await Measure.RunAsync("Consolidación de la importación (worker)", provider, dataset.Employees.Count,
                _ => DrainConsolidationAsync(provider)));
            results.Add(await Measure.RunAsync("Reimportación (todo duplicado)", provider, 0,
                async sp => await ImportAsync(sp, import)));

            results.Add(await Measure.RunAsync("10 decisiones concurrentes, mismo empleado", provider, 10,
                _ => ConcurrentDecisionsAsync(provider, dataset.SampleEmployee, start)));

            Console.WriteLine(Report.Markdown(options, dataset, results));
            Console.WriteLine();
            Console.WriteLine($"Huella de resultados (primer cálculo): `{firstFingerprint}`");
            Console.WriteLine($"Huella de resultados (recálculo): `{secondFingerprint}` {(firstFingerprint == secondFingerprint ? "(igual: cálculo determinista)" : "(DISTINTA)")}");
            return results.Any(r => r.Error != null) ? 1 : 0;
        }
        finally
        {
            if (!options.Keep)
            {
                NpgsqlConnection.ClearAllPools();
                await ExecuteAdminAsync(adminBuilder.ConnectionString, $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)");
            }
        }
    }

    /// <summary>El consumidor durable atiende todas las solicitudes pendientes (espera de agrupamiento en cero).</summary>
    private static async Task<object?> DrainConsolidationAsync(IServiceProvider provider)
    {
        var worker = provider.GetRequiredService<AttendanceSystem.Infrastructure.Services.AttendanceConsolidationWorker>();
        int handled, total = 0, cycles = 0;
        do
        {
            handled = await worker.DrainOnceAsync(CancellationToken.None);
            total += handled;
            cycles++;
        } while (handled > 0 && cycles < 1000);
        Console.Error.WriteLine($"  consolidación: {total} solicitudes en {cycles - 1} ciclos");
        return null;
    }

    private static async Task ImportAsync(IServiceProvider sp, List<AttendanceLogDto> logs)
    {
        var sender = sp.GetRequiredService<ISender>();
        foreach (var batch in logs.Chunk(500))
        {
            var result = await sender.Send(new RecordAttendanceBatchCommand("DEV-LOAD-01", batch.ToList(), TimeZone, "LoadTest"));
            if (result.IsFailure) throw new InvalidOperationException(result.Error);
        }
    }

    /// <summary>Diez operadores deciden a la vez sobre días distintos del mismo empleado (A04 / tasa de error).</summary>
    private static async Task<object?> ConcurrentDecisionsAsync(IServiceProvider provider, EmployeeId employee, DateTime start)
    {
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            using var scope = provider.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var result = await sender.Send(new SetDailyOvertimeAuthorizationCommand(
                employee.Value, DateOnly.FromDateTime(start.AddDays(i)), i % 2 == 0, false, "Carga concurrente"));
            return result.IsSuccess;
        }).ToList();
        var outcomes = await Task.WhenAll(tasks);
        if (outcomes.Any(o => !o)) throw new InvalidOperationException($"{outcomes.Count(o => !o)} decisiones rechazadas");
        return null;
    }

    private static ServiceProvider BuildProvider(string connection)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AttendanceDb"] = connection,
                ["AttendanceLocks:TimeoutSeconds"] = "30",
                ["Consolidation:QuietPeriod"] = "00:00:00"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFilter(_ => false));
        services.AddSingleton<ICurrentUserService, LoadTestUser>();
        services.AddApplicationServices();
        services.AddInfrastructureServices(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task ExecuteAdminAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class LoadTestUser : ICurrentUserService
    {
        public string? UserId => "load-test";
        public string? UserName => "load-test";
        public bool IsAuthenticated => true;
    }

    public static DateTime CivilToUtc(DateTime civil) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(civil, DateTimeKind.Unspecified), TimeZoneInfo.FindSystemTimeZoneById(TimeZone));

    public static string Zone => TimeZone;
}

public sealed record LoadOptions(int Employees, int Days, int Seed, bool Keep, DateTime Start)
{
    public static LoadOptions Parse(string[] args)
    {
        int Get(string name, int fallback)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1], CultureInfo.InvariantCulture) : fallback;
        }
        return new LoadOptions(Get("--employees", 250), Get("--days", 30), Get("--seed", 42), args.Contains("--keep"), new DateTime(2026, 6, 1));
    }
}

public sealed record Dataset(List<Employee> Employees, int Punches, int Adjustments, EmployeeId SampleEmployee, BranchId SampleBranch, Dictionary<string, int> ShiftMix);

public sealed record ScenarioResult(
    string Name,
    int EmployeeDays,
    TimeSpan Elapsed,
    double? P50,
    double? P95,
    long SqlCommands,
    TimeSpan DatabaseTime,
    int Units,
    double? LockWaitMax,
    long PeakWorkingSetMb,
    long PeakManagedMb,
    string? Error,
    string StageBreakdown);

/// <summary>Medición de un escenario: métricas de AttendanceTelemetry, comandos SQL de EF Core y memoria.</summary>
public static class Measure
{
    private static long _sqlCommands;
    private static readonly ConcurrentBag<double> EmployeeDayDurations = new();
    private static readonly ConcurrentBag<double> LockWaits = new();
    public static readonly ConcurrentDictionary<string, double> Stages = new();
    private static int _units;

    private static long _dbTicks;

    static Measure()
    {
        // Todo comando que llega a PostgreSQL (EF Core o ADO directo, como reclamar o completar solicitudes).
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                Interlocked.Increment(ref _sqlCommands);
                Interlocked.Add(ref _dbTicks, activity.Duration.Ticks);
            }
        });
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "AttendanceSystem.Attendance") l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "attendance.recalc.employee_day.duration") EmployeeDayDurations.Add(value);
            else if (instrument.Name == "attendance.lock.wait") LockWaits.Add(value);
            else if (instrument.Name == "attendance.recalc.stage.duration")
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "stage" && tag.Value is string stageName) Stages.AddOrUpdate(stageName, value, (_, v) => v + value);
                }
            }
        });
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "attendance.recalc.units") Interlocked.Add(ref _units, (int)value);
        });
        listener.SetMeasurementEventCallback<int>((_, _, _, _) => { });
        listener.Start();
    }

    public static async Task<ScenarioResult> RunAsync(string name, IServiceProvider provider, int employeeDays, Func<IServiceProvider, Task<object?>> work) =>
        await RunCoreAsync(name, provider, employeeDays, async sp => { await work(sp); });

    public static async Task<ScenarioResult> RunAsync(string name, IServiceProvider provider, int employeeDays, Func<IServiceProvider, Task<int>> work) =>
        await RunCoreAsync(name, provider, employeeDays, async sp => { await work(sp); });

    public static async Task<ScenarioResult> RunAsync(string name, IServiceProvider provider, int employeeDays, Func<IServiceProvider, Task> work) =>
        await RunCoreAsync(name, provider, employeeDays, work);

    private static async Task<ScenarioResult> RunCoreAsync(string name, IServiceProvider provider, int employeeDays, Func<IServiceProvider, Task> work)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        EmployeeDayDurations.Clear();
        LockWaits.Clear();
        Stages.Clear();
        Interlocked.Exchange(ref _units, 0);
        Interlocked.Exchange(ref _sqlCommands, 0);
        Interlocked.Exchange(ref _dbTicks, 0);

        long peakWorkingSet = 0, peakManaged = 0;
        using var sampling = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            var process = Process.GetCurrentProcess();
            while (!sampling.IsCancellationRequested)
            {
                process.Refresh();
                peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
                try { await Task.Delay(50, sampling.Token); } catch (OperationCanceledException) { }
            }
        });

        Console.Error.WriteLine($"→ {name}");
        string? error = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var scope = provider.CreateScope();
            await work(scope.ServiceProvider);
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
        }
        stopwatch.Stop();
        sampling.Cancel();
        await sampler;

        var durations = EmployeeDayDurations.OrderBy(d => d).ToArray();
        return new ScenarioResult(
            name,
            employeeDays,
            stopwatch.Elapsed,
            Percentile(durations, 0.50),
            Percentile(durations, 0.95),
            Interlocked.Read(ref _sqlCommands),
            TimeSpan.FromTicks(Interlocked.Read(ref _dbTicks)),
            _units,
            LockWaits.IsEmpty ? null : LockWaits.Max(),
            peakWorkingSet / (1024 * 1024),
            peakManaged / (1024 * 1024),
            error,
            string.Join(", ", Stages.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / 1000:F1} s")));
    }

    private static double? Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return null;
        var index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}

/// <summary>Dataset sintético determinista del escenario F00-08.</summary>
public static class Seeder
{
    public static async Task<Dataset> SeedAsync(IServiceProvider provider, LoadOptions options)
    {
        var random = new Random(options.Seed);
        var branches = Enumerable.Range(1, 3).Select(i => Branch.Create($"S0{i}", $"Sucursal {i}", null, timeZoneId: Program.Zone)).ToList();
        var department = Department.Create("Operación", null);
        var position = Position.Create("Operador", null, 1000m);
        department.AddPosition(position);

        var regular = Shift.Create("Regular 08-17", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(9), ShiftType.Matutino);
        regular.SetPaidLunchMinutes(60);
        var night = Shift.Create("Nocturno 22-06", new TimeSpan(22, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Nocturno);
        var guard = Shift.Create("Guardia 24 h", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(24), ShiftType.Continuo);
        var flexible = Shift.Create("Flexible multi", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Flexible,
            punchTrackingMode: PunchTrackingMode.MultiInterval);
        var split = Shift.Create("Partido 09-13/15-19", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(4), ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(15, 0, 0), secondBlockEndTime: new TimeSpan(19, 0, 0), secondBlockToleranceMinutes: 10);
        var mixed = Shift.Create("Mixto semanal", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Mixto, new[]
        {
            ShiftDay.ForShift(DayOfWeek.Monday, regular),
            ShiftDay.ForShift(DayOfWeek.Tuesday, regular),
            ShiftDay.ForShift(DayOfWeek.Wednesday, split),
            ShiftDay.ForShift(DayOfWeek.Thursday, regular),
            ShiftDay.ForShift(DayOfWeek.Friday, night)
        });

        var employees = new List<Employee>();
        var mix = new Dictionary<string, int>();
        var punches = new List<AttendanceRecord>();
        var rosters = new List<ShiftRoster>();
        var adjustments = new List<AttendanceAdjustment>();

        for (int i = 0; i < options.Employees; i++)
        {
            var share = (double)i / options.Employees;
            var (kind, shift) = share switch
            {
                < 0.60 => ("Regular", regular),
                < 0.75 => ("Nocturno", night),
                < 0.85 => ("Guardia 24 h", guard),
                < 0.95 => ("Flexible multi", flexible),
                _ => ("Mixto/partido", mixed)
            };
            mix[kind] = mix.GetValueOrDefault(kind) + 1;
            var restDays = kind == "Guardia 24 h" ? Array.Empty<WeekDay>() : new[] { WeekDay.Domingo };
            var employee = Employee.Create(EmployeeId.From($"L{i:D4}"), "Empleado", $"Carga {i}", null, null, new DateTime(2024, 1, 1), Gender.Male,
                branches[i % 3].Id, department.Id, position.Id, shift.ShiftType, shift.Id, restDays: restDays, overtimeAuthorized: true);
            employees.Add(employee);

            for (int d = 0; d < options.Days; d++)
            {
                var date = options.Start.AddDays(d);
                if (kind == "Guardia 24 h")
                {
                    var works = d % 2 == 0;
                    rosters.Add(ShiftRoster.Create(employee.Id, date, guard.Id, isRestDay: !works));
                    if (works) AddDay(punches, employee, random, date, (8, 0, 10), (32, 0, 20));
                    continue;
                }
                if (date.DayOfWeek == DayOfWeek.Sunday) continue;
                if (random.NextDouble() < 0.02) continue; // falta

                switch (kind)
                {
                    case "Regular":
                        if (random.NextDouble() < 0.97)
                            AddDay(punches, employee, random, date, (7, 50, 15), (13, 0, 20), (14, 0, 15), (17, 0, 30));
                        else
                            AddDay(punches, employee, random, date, (7, 50, 15), (17, 0, 30));
                        break;
                    case "Nocturno":
                        if (random.NextDouble() < 0.5)
                            AddDay(punches, employee, random, date, (21, 50, 20), (25, 30, 20), (26, 5, 10), (30, 0, 20));
                        else
                            AddDay(punches, employee, random, date, (21, 50, 20), (30, 0, 20));
                        break;
                    case "Flexible multi":
                        AddDay(punches, employee, random, date, (8, 0, 20), (12, 0, 15), (13, 0, 15), (17, 0, 20));
                        if (random.NextDouble() < 0.3) AddDay(punches, employee, random, date, (17, 40, 5), (18, 30, 10));
                        break;
                    default:
                        switch (date.DayOfWeek)
                        {
                            case DayOfWeek.Wednesday: AddDay(punches, employee, random, date, (8, 55, 10), (13, 0, 5), (14, 55, 5), (19, 0, 10)); break;
                            case DayOfWeek.Friday: AddDay(punches, employee, random, date, (21, 50, 15), (30, 0, 15)); break;
                            case DayOfWeek.Saturday: break;
                            default: AddDay(punches, employee, random, date, (7, 55, 10), (13, 0, 10), (14, 0, 10), (17, 0, 20)); break;
                        }
                        break;
                }

                if (random.NextDouble() < 0.05)
                {
                    var adjustment = AttendanceAdjustment.Create(employee.Id, date, "rh");
                    adjustment.SetOvertimeConfiguration(true, false, "rh");
                    adjustments.Add(adjustment);
                }
            }
        }

        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            context.SystemConfigurations.Add(SystemConfiguration.CreateDefault());
            context.Branches.AddRange(branches);
            context.Departments.Add(department);
            context.Positions.Add(position);
            context.Shifts.AddRange(regular, night, guard, flexible, split, mixed);
            context.Employees.AddRange(employees);
            await context.SaveChangesAsync();
        }
        await InsertInBatchesAsync(provider, rosters);
        await InsertInBatchesAsync(provider, adjustments);
        await InsertInBatchesAsync(provider, punches);

        // Los marcajes sembrados generaron solicitudes de consolidación (una por empleado con todo el mes): se
        // descartan para que el escenario del consumidor mida sólo lo que genera la importación.
        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            await context.Database.ExecuteSqlRawAsync("DELETE FROM \"AttendanceConsolidationRequests\"");
        }

        var sample = employees.First();
        return new Dataset(employees, punches.Count, adjustments.Count, sample.Id, branches[0].Id, mix);
    }

    /// <summary>Un día de marcajes; cada tupla es (hora civil desde la medianoche del día, minuto, variación máxima en minutos).</summary>
    private static void AddDay(List<AttendanceRecord> punches, Employee employee, Random random, DateTime date, params (int Hour, int Minute, int Jitter)[] times)
    {
        foreach (var (hour, minute, jitter) in times)
        {
            var civil = date.AddHours(hour).AddMinutes(minute + random.Next(0, jitter + 1)).AddSeconds(random.Next(0, 60));
            punches.Add(AttendanceRecord.Create(employee.Id, DeviceId.From("DEV-LOAD-01"), Program.CivilToUtc(civil), VerifyMethod.Fingerprint,
                CheckType.CheckIn, workDate: DateOnly.FromDateTime(civil), originalCheckTime: civil, timeZoneId: Program.Zone, source: "LoadTest"));
        }
    }

    private static async Task InsertInBatchesAsync<T>(IServiceProvider provider, List<T> items) where T : class
    {
        foreach (var batch in items.Chunk(2000))
        {
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
            context.Set<T>().AddRange(batch);
            await context.SaveChangesAsync();
        }
    }

    /// <summary>Un día nuevo de marcajes de toda la plantilla para el escenario de importación.</summary>
    public static List<AttendanceLogDto> ImportBatch(Dataset dataset, DateTime day, int seed)
    {
        var random = new Random(seed + 1);
        var logs = new List<AttendanceLogDto>();
        foreach (var employee in dataset.Employees)
        {
            foreach (var (hour, minute) in new[] { (7, 50), (13, 0), (14, 0), (17, 0) })
            {
                var civil = day.AddHours(hour).AddMinutes(minute + random.Next(0, 20)).AddSeconds(random.Next(0, 60));
                logs.Add(new AttendanceLogDto(employee.Id.Value, Program.CivilToUtc(civil), 1, 0, civil, DateOnly.FromDateTime(civil), Program.Zone, "LoadTest"));
            }
        }
        return logs;
    }
}

/// <summary>
/// Huella SHA-256 de los resultados del cálculo, independiente de los identificadores generados: valores de cada
/// jornada y pertenencia de cada marcaje (jornada, papel e instante). Permite comparar versiones del código sobre el
/// mismo dataset determinista (F07-05 / F07-09: equivalencia funcional).
/// </summary>
public static class Fingerprint
{
    public static async Task<string> ComputeAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var dailies = await context.DailyAttendances.AsNoTracking()
            .OrderBy(d => d.EmployeeId).ThenBy(d => d.Date)
            .Select(d => new
            {
                Employee = d.EmployeeId, d.Date, d.ActualCheckIn, d.ActualBlock1CheckOut, d.ActualBlock2CheckIn, d.ActualCheckOut,
                d.LateMinutes, d.EarlyDepartureMinutes, d.OvertimeMinutes, d.RawOvertimeMinutes, d.TotalWorkedMinutes,
                d.IsAbsent, d.IsRestDay, d.MissingCheckIn, d.MissingCheckOut, d.LunchMinutesTaken, d.LunchExcessMinutes,
                d.HasTemporaryExits, d.TemporaryExitMinutes, d.CalculationIssue, d.ShiftName
            })
            .ToListAsync();
        var assignments = await (from a in context.PunchAssignments.AsNoTracking()
                                 join r in context.AttendanceRecords.AsNoTracking() on a.Id equals r.Id
                                 orderby a.EmployeeId, r.CheckTime
                                 select new { Employee = a.EmployeeId, a.WorkDate, Role = (int)a.Role, Origin = (int)a.Origin, r.CheckTime })
            .ToListAsync();

        var sb = new StringBuilder();
        foreach (var d in dailies)
        {
            sb.Append(d.Employee.Value).Append('|').Append(d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
              .Append('|').Append(d.ActualCheckIn?.ToString("O")).Append('|').Append(d.ActualBlock1CheckOut?.ToString("O"))
              .Append('|').Append(d.ActualBlock2CheckIn?.ToString("O")).Append('|').Append(d.ActualCheckOut?.ToString("O"))
              .Append('|').Append(d.LateMinutes).Append('|').Append(d.EarlyDepartureMinutes).Append('|').Append(d.OvertimeMinutes)
              .Append('|').Append(d.RawOvertimeMinutes).Append('|').Append(d.TotalWorkedMinutes).Append('|').Append(d.IsAbsent)
              .Append('|').Append(d.IsRestDay).Append('|').Append(d.MissingCheckIn).Append('|').Append(d.MissingCheckOut)
              .Append('|').Append(d.LunchMinutesTaken).Append('|').Append(d.LunchExcessMinutes).Append('|').Append(d.HasTemporaryExits)
              .Append('|').Append(d.TemporaryExitMinutes).Append('|').Append(d.CalculationIssue).Append('|').Append(d.ShiftName).Append('\n');
        }
        foreach (var a in assignments)
        {
            sb.Append(a.Employee.Value).Append('|').Append(a.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
              .Append('|').Append(a.Role).Append('|').Append(a.Origin).Append('|').Append(a.CheckTime.ToString("O")).Append('\n');
        }
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return $"{dailies.Count} jornadas, {assignments.Count} asignaciones, {Convert.ToHexString(hash)[..16]}";
    }
}

public static class Report
{
    public static string Markdown(LoadOptions options, Dataset dataset, IReadOnlyList<ScenarioResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Dataset: {dataset.Employees.Count} empleados ({string.Join(", ", dataset.ShiftMix.Select(kv => $"{kv.Key} {kv.Value}"))}), " +
                      $"{options.Days} días desde {options.Start:yyyy-MM-dd}, {dataset.Punches} marcajes, {dataset.Adjustments} ajustes, semilla {options.Seed}.");
        sb.AppendLine($"Entorno: {Environment.OSVersion}, {Environment.ProcessorCount} CPU lógicas, .NET {Environment.Version}.");
        sb.AppendLine();
        sb.AppendLine("| Escenario | Empleado-días | Total | Tiempo en BD | p50 por empleado-día | p95 por empleado-día | Comandos SQL | Comandos / empleado-mes | Espera máx. de bloqueo | Memoria pico (WS / heap) | Error |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in results)
        {
            var employeeMonths = r.EmployeeDays > 0 ? r.EmployeeDays / (double)options.Days : 0;
            sb.AppendLine(string.Join(" | ", new[]
            {
                $"| {r.Name}",
                r.EmployeeDays.ToString(CultureInfo.InvariantCulture),
                $"{r.Elapsed.TotalSeconds:F1} s",
                $"{r.DatabaseTime.TotalSeconds:F1} s",
                r.P50.HasValue ? $"{r.P50:F1} ms" : "—",
                r.P95.HasValue ? $"{r.P95:F1} ms" : "—",
                r.SqlCommands.ToString(CultureInfo.InvariantCulture),
                employeeMonths > 0 ? $"{r.SqlCommands / employeeMonths:F1}" : "—",
                r.LockWaitMax.HasValue ? $"{r.LockWaitMax:F0} ms" : "—",
                $"{r.PeakWorkingSetMb} / {r.PeakManagedMb} MB",
                (r.Error ?? "—") + " |"
            }));
        }
        sb.AppendLine();
        sb.AppendLine("Etapas del recálculo por escenario (suma de lotes):");
        foreach (var r in results.Where(r => r.StageBreakdown.Length > 0))
        {
            sb.AppendLine($"- {r.Name}: {r.StageBreakdown}");
        }
        return sb.ToString();
    }
}
