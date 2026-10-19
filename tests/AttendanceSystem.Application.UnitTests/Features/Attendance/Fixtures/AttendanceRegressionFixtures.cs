using System;
using System.Collections.Generic;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;

/// <summary>
/// Fixture de regresión determinista y anonimizado para la suite de pruebas de asistencia (F00-04).
/// Cubre zona UTC-6, turno nocturno, continuo de 24h, MultiInterval impar, turnos mixtos,
/// bolsa semanal, dos empleados y dos sucursales con reloj controlable.
/// </summary>
public static class AttendanceRegressionFixtures
{
    public const string TimeZoneMexicoCity = "America/Mexico_City";
    public static readonly TimeSpan UtcMinus6Offset = TimeSpan.FromHours(-6);

    // Identificadores deterministas
    public static readonly BranchId BranchNorthId = BranchId.CreateNew();
    public static readonly BranchId BranchSouthId = BranchId.CreateNew();
    public static readonly DepartmentId DeptOpsId = DepartmentId.CreateNew();
    public static readonly PositionId PosOperatorId = PositionId.CreateNew();
    public static readonly DeviceId DeviceNorthId = DeviceId.From("DEV-NORTH-01");
    public static readonly DeviceId DeviceSouthId = DeviceId.From("DEV-SOUTH-02");

    public static readonly EmployeeId EmployeeFixedId = EmployeeId.From("EMP-FIX-001");
    public static readonly EmployeeId EmployeeRotativeId = EmployeeId.From("EMP-ROT-002");

    public static readonly ShiftId RegularShiftId = ShiftId.CreateNew();
    public static readonly ShiftId NightShiftId = ShiftId.CreateNew();
    public static readonly ShiftId Continuous24hShiftId = ShiftId.CreateNew();
    public static readonly ShiftId MultiIntervalShiftId = ShiftId.CreateNew();
    public static readonly ShiftId SplitShiftId = ShiftId.CreateNew();
    public static readonly ShiftId WeeklyOvertimeShiftId = ShiftId.CreateNew();
    public static readonly ShiftId MixedShiftId = ShiftId.CreateNew();

    public static Employee CreateFixedEmployee(DateTime? hireDate = null)
    {
        return Employee.Create(
            id: EmployeeFixedId,
            firstName: "Empleado",
            lastName: "Fijo Uno",
            email: "empleado1@anon.test",
            phoneNumber: null,
            hireDate: hireDate ?? new DateTime(2024, 1, 1),
            gender: Gender.Male,
            branchId: BranchNorthId,
            departmentId: DeptOpsId,
            positionId: PosOperatorId,
            shiftType: ShiftType.Matutino,
            scheduleId: RegularShiftId,
            restDay: WeekDay.Domingo,
            overtimeAuthorized: true,
            overtimeCapType: OvertimeCapType.Daily,
            overtimeCapMinutes: 180);
    }

    public static Employee CreateRotativeEmployee(DateTime? hireDate = null)
    {
        return Employee.Create(
            id: EmployeeRotativeId,
            firstName: "Empleado",
            lastName: "Rotativo Dos",
            email: "empleado2@anon.test",
            phoneNumber: null,
            hireDate: hireDate ?? new DateTime(2024, 1, 1),
            gender: Gender.Female,
            branchId: BranchSouthId,
            departmentId: DeptOpsId,
            positionId: PosOperatorId,
            shiftType: ShiftType.Rotativo,
            scheduleId: null,
            restDays: new List<WeekDay> { WeekDay.Sabado, WeekDay.Domingo },
            overtimeAuthorized: true,
            overtimeCapType: OvertimeCapType.Period,
            overtimeCapMinutes: 540);
    }

    public static Shift CreateRegularShift()
    {
        return Shift.Create(
            name: "Matutino 08-16",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 15,
            workHours: TimeSpan.FromHours(8),
            shiftType: ShiftType.Matutino,
            lunchBreakMinutes: 60);
    }

    public static Shift CreateNightShift()
    {
        // Turno nocturno: 22:00 a 06:00 día siguiente (8 horas)
        return Shift.Create(
            name: "Nocturno 22-06",
            startTime: new TimeSpan(22, 0, 0),
            toleranceMinutes: 15,
            workHours: TimeSpan.FromHours(8),
            shiftType: ShiftType.Nocturno,
            lunchBreakMinutes: 30);
    }

    public static Shift CreateContinuous24hShift()
    {
        // Turno de guardia continua de 24 horas: 08:00 a 08:00
        return Shift.Create(
            name: "Guardia 24 Horas",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 30,
            workHours: TimeSpan.FromHours(24),
            shiftType: ShiftType.Continuo,
            lunchBreakMinutes: 60);
    }

    public static Shift CreateMultiIntervalShift()
    {
        // Turno flexible acumulable por multi-marcaje
        return Shift.Create(
            name: "MultiInterval Libre",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 10,
            workHours: TimeSpan.FromHours(8),
            shiftType: ShiftType.Flexible,
            punchTrackingMode: PunchTrackingMode.MultiInterval);
    }

    public static Shift CreateSplitShift()
    {
        // Turno partido: 09:00 - 13:00 y 15:00 - 19:00
        return Shift.Create(
            name: "Partido 9-13 y 15-19",
            startTime: new TimeSpan(9, 0, 0),
            toleranceMinutes: 10,
            workHours: TimeSpan.FromHours(8),
            shiftType: ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(15, 0, 0),
            secondBlockEndTime: new TimeSpan(19, 0, 0),
            secondBlockToleranceMinutes: 10);
    }

    public static Shift CreateWeeklyOvertimeShift(int weeklyHours = 48)
    {
        // Turno con bolsa de horas semanal
        return Shift.Create(
            name: $"Bolsa Semanal {weeklyHours}h",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 15,
            workHours: TimeSpan.Zero,
            shiftType: ShiftType.Flexible,
            weeklyWorkHours: TimeSpan.FromHours(weeklyHours),
            hasEntryWindow: false);
    }

    public static Shift CreateMixedShift()
    {
        // Turno mixto con configuración variable por día
        var days = new List<ShiftDay>
        {
            new ShiftDay(DayOfWeek.Monday, new TimeSpan(8, 0, 0), TimeSpan.FromHours(8), ShiftType.Matutino),
            new ShiftDay(DayOfWeek.Tuesday, new TimeSpan(8, 0, 0), TimeSpan.FromHours(8), ShiftType.Matutino),
            new ShiftDay(DayOfWeek.Wednesday, new TimeSpan(14, 0, 0), TimeSpan.FromHours(8), ShiftType.Vespertino),
            new ShiftDay(DayOfWeek.Thursday, new TimeSpan(22, 0, 0), TimeSpan.FromHours(8), ShiftType.Nocturno),
            new ShiftDay(DayOfWeek.Friday, new TimeSpan(8, 0, 0), TimeSpan.FromHours(6), ShiftType.Matutino)
            // Sábado y domingo intencionalmente sin configuración (Día libre/descanso)
        };

        return Shift.Create(
            name: "Mixto Semanal",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 10,
            workHours: TimeSpan.FromHours(8),
            shiftType: ShiftType.Mixto,
            days: days);
    }

    /// <summary>
    /// Crea un AttendanceRecord determinista con fecha civil local y su correspondiente UTC (UTC-6).
    /// </summary>
    public static AttendanceRecord CreatePunch(
        EmployeeId employeeId,
        DateTime localCivilTime,
        CheckType checkType,
        DeviceId? deviceId = null,
        VerifyMethod? verifyMethod = null,
        AttendanceStatus? status = null)
    {
        var effectiveStatus = status ?? AttendanceStatus.Pending;
        // En UTC-6, 08:00 local es 14:00 UTC
        var utcTime = DateTime.SpecifyKind(localCivilTime - UtcMinus6Offset, DateTimeKind.Utc);
        var workDate = DateOnly.FromDateTime(localCivilTime);

        var record = AttendanceRecord.Create(
            employeeId: employeeId,
            deviceId: deviceId ?? DeviceNorthId,
            checkTime: utcTime,
            verifyMethod: verifyMethod ?? VerifyMethod.Fingerprint,
            checkType: checkType,
            workDate: workDate,
            originalCheckTime: localCivilTime,
            timeZoneId: TimeZoneMexicoCity,
            source: "Fixture");

        if (effectiveStatus == AttendanceStatus.Processed)
        {
            record.MarkAsProcessed();
        }

        return record;
    }
}
