using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Features.Attendance.Decisions;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Documentation;

/// <summary>
/// Ejemplos numéricos de Docs/tecnico/03-motor-de-calculo.md. Cada prueba es un ejemplo del documento (mismo
/// identificador, p. ej. <c>R03</c>) y ejecuta el orquestador real (<see cref="AttendanceRecalculationHarness"/>):
/// si el cálculo cambia, la prueba falla y el documento debe actualizarse.
/// Fechas: semana del lunes 2026-08-03 en America/Mexico_City (UTC-6 todo el año). Sin configuración del sistema
/// se usan los valores por omisión: ventana de comida 12:30–15:00, 30 min con goce, ventana nocturna 23:00–04:00,
/// doble toque de 15 min, semana desde el lunes.
/// </summary>
public class MotorCalculoEjemplosTests
{
    private static readonly DateTime Lun = new(2026, 8, 3);
    private static readonly DateTime Mar = Lun.AddDays(1);
    private static readonly DateTime Mie = Lun.AddDays(2);
    private static readonly DateTime Dom = Lun.AddDays(6);

    // ---------------------------------------------------------------- escenario

    private sealed class Escenario
    {
        public AttendanceRecalculationHarness H { get; } = new();
        public EmployeeId Emp { get; } = EmployeeId.From("DOC-001");

        public Escenario(
            Shift? horario,
            bool extrasAutorizadas = true,
            OvertimeCalculationMethod metodo = OvertimeCalculationMethod.NoRounding,
            OvertimeCapType tope = OvertimeCapType.None,
            double? minutosTope = null,
            bool extrasAntesDeEntrada = false,
            IEnumerable<WeekDay>? descansos = null,
            ShiftType? tipo = null,
            IEnumerable<Shift>? otrosTurnos = null)
        {
            if (horario != null) H.Store.Shifts.Add(horario);
            if (otrosTurnos != null) H.Store.Shifts.AddRange(otrosTurnos);
            H.Store.Employees.Add(Employee.Create(Emp, "Empleado", "Ejemplo", null, null, new DateTime(2024, 1, 1), Gender.Male,
                AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
                tipo ?? horario?.ShiftType, horario?.Id,
                overtimeAuthorized: extrasAutorizadas, overtimeCalculationMethod: metodo, overtimeCapType: tope,
                overtimeCapMinutes: minutosTope, calculateOvertimeBeforeEntry: extrasAntesDeEntrada,
                restDays: descansos ?? Array.Empty<WeekDay>()));
        }

        /// <summary>Marcajes del día en hora civil; "+1 06:00" es del día siguiente y "-1 23:50" del anterior.</summary>
        public Escenario Marcas(DateTime dia, params string[] horas)
        {
            foreach (var texto in horas)
            {
                var offset = 0;
                var hora = texto;
                if (texto.StartsWith("+1 ")) { offset = 1; hora = texto[3..]; }
                else if (texto.StartsWith("-1 ")) { offset = -1; hora = texto[3..]; }
                var t = TimeSpan.ParseExact(hora, @"hh\:mm", CultureInfo.InvariantCulture);
                H.Store.Records.Add(AttendanceRegressionFixtures.CreatePunch(Emp, dia.AddDays(offset).Add(t), CheckType.CheckIn));
            }
            return this;
        }

        public async Task<DailyAttendance> Calcular(DateTime dia)
        {
            await H.RecalculateAsync(dia, dia);
            return Jornada(dia);
        }

        public Task Calcular(DateTime desde, DateTime hasta) => H.RecalculateAsync(desde, hasta);

        public DailyAttendance Jornada(DateTime dia) => H.Store.Dailies.Single(d => d.EmployeeId == Emp && d.Date == dia.Date);
    }

    private static string Hora(DateTime? civil) => civil?.ToString("ddd HH:mm", CultureInfo.InvariantCulture) ?? "-";

    // ---------------------------------------------------------------- turnos

    /// <summary>Matutino 08:00, 8 h (salida 16:00), 15 min de tolerancia.</summary>
    private static Shift Matutino() => Shift.Create("Matutino 08-16", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Matutino);

    private static Shift Vespertino() => Shift.Create("Vespertino 14-22", new TimeSpan(14, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Vespertino);

    private static Shift Nocturno(TimeSpan? inicio = null) =>
        Shift.Create("Nocturno", inicio ?? new TimeSpan(22, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Nocturno);

    /// <summary>Partido 09:00–13:00 y 15:00–19:00, 10 min de tolerancia en cada bloque.</summary>
    private static Shift Partido() => Shift.Create("Partido 9-13 15-19", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(4), ShiftType.Partido,
        secondBlockStartTime: new TimeSpan(15, 0, 0), secondBlockEndTime: new TimeSpan(19, 0, 0), secondBlockToleranceMinutes: 10);

    // ================================================================ R: matutino / vespertino (procesador regular)

    [Fact]
    public async Task R01_Puntual()
    {
        var e = new Escenario(Matutino()).Marcas(Lun, "07:55", "16:00");
        var d = await e.Calcular(Lun);
        (d.LateMinutes, d.EarlyDepartureMinutes, d.TotalWorkedMinutes, d.RawOvertimeMinutes, d.OvertimeMinutes).Should().Be((0, 0, 480, 0, 0));
    }

    [Fact]
    public async Task R02_RetardoDentroDeTolerancia()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "08:15", "16:00").Calcular(Lun);
        (d.LateMinutes, d.TotalWorkedMinutes).Should().Be((0, 480));
    }

    [Fact]
    public async Task R03_RetardoFueraDeTolerancia_CuentaDesdeElSiguienteBloqueDe30()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "08:20", "16:00").Calcular(Lun);
        (d.LateMinutes, d.TotalWorkedMinutes, d.EarlyDepartureMinutes, d.RawOvertimeMinutes).Should().Be((20, 450, 0, 0));
        d.GetReferenceEntry().Should().Be(Lun.AddHours(8.5));
    }

    [Fact]
    public async Task R04_RetardoLargo_DosBloques()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "08:50", "17:00").Calcular(Lun);
        (d.LateMinutes, d.TotalWorkedMinutes, d.RawOvertimeMinutes).Should().Be((50, 480, 0));
        d.GetReferenceEntry().Should().Be(Lun.AddHours(9));
    }

    [Fact]
    public async Task R05_SalidaAnticipada()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "08:00", "15:30").Calcular(Lun);
        (d.EarlyDepartureMinutes, d.TotalWorkedMinutes, d.RawOvertimeMinutes).Should().Be((30, 450, 0));
    }

    [Fact]
    public async Task R06_TiempoExtra_SinRedondeo()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "08:00", "18:10").Calcular(Lun);
        (d.TotalWorkedMinutes, d.RawOvertimeMinutes, d.OvertimeMinutes).Should().Be((610, 130, 130));
    }

    [Theory]
    [InlineData(OvertimeCalculationMethod.NoRounding, "18:10", 130, 130)]
    [InlineData(OvertimeCalculationMethod.RoundByHalfHour, "18:10", 130, 120)]
    [InlineData(OvertimeCalculationMethod.RoundByHour, "18:10", 130, 120)]
    [InlineData(OvertimeCalculationMethod.RoundByHalfHour, "18:50", 170, 150)]
    [InlineData(OvertimeCalculationMethod.RoundByHour, "18:50", 170, 120)]
    public async Task R07_TiempoExtra_Redondeo(OvertimeCalculationMethod metodo, string salida, int bruto, int efectivo)
    {
        var d = await new Escenario(Matutino(), metodo: metodo).Marcas(Lun, "08:00", salida).Calcular(Lun);
        (d.RawOvertimeMinutes, d.OvertimeMinutes).Should().Be((bruto, efectivo));
    }

    [Fact]
    public async Task R08_TiempoExtra_NoAutorizado()
    {
        var d = await new Escenario(Matutino(), extrasAutorizadas: false).Marcas(Lun, "08:00", "18:00").Calcular(Lun);
        (d.RawOvertimeMinutes, d.OvertimeMinutes).Should().Be((120, 0));
        d.Notes.Should().Contain("Tiempo extra no autorizado: 120 min");
    }

    [Fact]
    public async Task R09_TopeDiario()
    {
        var d = await new Escenario(Matutino(), tope: OvertimeCapType.Daily, minutosTope: 60).Marcas(Lun, "08:00", "18:10").Calcular(Lun);
        (d.RawOvertimeMinutes, d.OvertimeMinutes).Should().Be((130, 60));
    }

    [Fact]
    public async Task R10_TopePorPeriodo_SemanaDesdeLunes()
    {
        var e = new Escenario(Matutino(), tope: OvertimeCapType.Period, minutosTope: 120)
            .Marcas(Lun, "08:00", "17:30").Marcas(Mar, "08:00", "17:30");
        await e.Calcular(Lun, Mar);
        (e.Jornada(Lun).RawOvertimeMinutes, e.Jornada(Lun).OvertimeMinutes).Should().Be((90, 90));
        (e.Jornada(Mar).RawOvertimeMinutes, e.Jornada(Mar).OvertimeMinutes).Should().Be((90, 30));
    }

    [Fact]
    public async Task R11_ExtrasAntesDeLaEntrada()
    {
        var con = await new Escenario(Matutino(), extrasAntesDeEntrada: true).Marcas(Lun, "07:00", "16:00").Calcular(Lun);
        (con.TotalWorkedMinutes, con.RawOvertimeMinutes).Should().Be((540, 60));

        var sin = await new Escenario(Matutino()).Marcas(Lun, "07:00", "16:00").Calcular(Lun);
        (sin.TotalWorkedMinutes, sin.RawOvertimeMinutes).Should().Be((480, 0));
    }

    [Fact]
    public async Task R12_ExtrasAntesDeLaEntrada_LlegadaTardeDentroDeTolerancia()
    {
        // Con la opción activa, la referencia es la llegada real también cuando llega tarde dentro de la tolerancia.
        var d = await new Escenario(Matutino(), extrasAntesDeEntrada: true).Marcas(Lun, "08:10", "16:00").Calcular(Lun);
        (d.LateMinutes, d.TotalWorkedMinutes).Should().Be((0, 470));
    }

    [Fact]
    public async Task R13_SoloEntrada()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "08:02").Calcular(Lun);
        (d.MissingCheckIn, d.MissingCheckOut, d.TotalWorkedMinutes, d.IsAbsent).Should().Be((false, true, 0, false));
    }

    [Fact]
    public async Task R14_UnSoloMarcajeCercaDeLaSalida_EsSalida()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "15:50").Calcular(Lun);
        (d.MissingCheckIn, d.MissingCheckOut, d.EarlyDepartureMinutes).Should().Be((true, false, 10));
    }

    [Fact]
    public async Task R15_DobleToqueEnLaEntrada()
    {
        var e = new Escenario(Matutino()).Marcas(Lun, "08:00", "08:05", "16:00");
        var d = await e.Calcular(Lun);
        (Hora(d.CivilCheckIn), Hora(d.CivilCheckOut), d.TotalWorkedMinutes).Should().Be(("Mon 08:00", "Mon 16:00", 480));
        e.H.Store.Assignments.Single(a => a.AttendanceRecordId == e.H.Punch(e.Emp, Lun.AddHours(8).AddMinutes(5)).Id)
            .Role.Should().Be(PunchAssignmentRole.DiscardedDoubleTap);
    }

    [Fact]
    public async Task R16_DosMarcajesA10Minutos_SonUnoSolo()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "08:00", "08:10").Calcular(Lun);
        (Hora(d.CivilCheckIn), d.MissingCheckOut).Should().Be(("Mon 08:00", true));
    }

    [Fact]
    public async Task R17_Falta()
    {
        var d = await new Escenario(Matutino()).Calcular(Lun);
        d.IsAbsent.Should().BeTrue();
    }

    [Fact]
    public async Task R18_DescansoSinMarcajes_NoEsFalta()
    {
        var d = await new Escenario(Matutino(), descansos: new[] { WeekDay.Domingo }).Calcular(Dom);
        (d.IsRestDay, d.IsAbsent, d.WorkedOnRestDay).Should().Be((true, false, false));
    }

    [Fact]
    public async Task R19_DescansoTrabajado()
    {
        var d = await new Escenario(Matutino(), descansos: new[] { WeekDay.Domingo }).Marcas(Dom, "08:00", "14:00").Calcular(Dom);
        (d.IsRestDay, d.WorkedOnRestDay, d.TotalWorkedMinutes, d.EarlyDepartureMinutes, d.RawOvertimeMinutes)
            .Should().Be((true, true, 360, 120, 0));
    }

    [Fact]
    public async Task R20_Vespertino_MismasReglas()
    {
        var d = await new Escenario(Vespertino()).Marcas(Lun, "14:20", "22:00").Calcular(Lun);
        (d.LateMinutes, d.TotalWorkedMinutes).Should().Be((20, 450));
    }

    [Fact]
    public async Task R21_VespertinoQueCruzaMedianoche_UsaLaReglaNocturna()
    {
        var turno = Shift.Create("Vespertino 18-02", new TimeSpan(18, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Vespertino);
        var d = await new Escenario(turno).Marcas(Lun, "18:00", "+1 02:00").Calcular(Lun);
        (Hora(d.CivilCheckOut), d.TotalWorkedMinutes).Should().Be(("Tue 02:00", 480));
    }

    // ================================================================ C: comida y salidas temporales

    [Fact]
    public async Task C01_ComidaDentroDeLosMinutosConGoce()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "08:00", "13:00", "13:30", "16:00").Calcular(Lun);
        (d.LunchMinutesTaken, d.LunchExcessMinutes, d.TotalWorkedMinutes, d.EarlyDepartureMinutes).Should().Be((30, 0, 480, 0));
    }

    [Fact]
    public async Task C02_ComidaConExcedente_SinReponer()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "08:00", "13:00", "13:50", "16:00").Calcular(Lun);
        (d.LunchMinutesTaken, d.LunchExcessMinutes, d.TotalWorkedMinutes, d.EarlyDepartureMinutes).Should().Be((50, 20, 460, 20));
    }

    [Fact]
    public async Task C03_ComidaConExcedente_Repuesto()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "08:00", "13:00", "13:50", "16:20").Calcular(Lun);
        (d.LunchExcessMinutes, d.TotalWorkedMinutes, d.EarlyDepartureMinutes, d.RawOvertimeMinutes).Should().Be((20, 480, 0, 0));
    }

    [Fact]
    public async Task C04_MinutosConGoceDelTurno()
    {
        var turno = Matutino();
        turno.SetPaidLunchMinutes(60);
        var d = await new Escenario(turno).Marcas(Lun, "08:00", "13:00", "13:50", "16:00").Calcular(Lun);
        (d.PaidLunchMinutes, d.LunchExcessMinutes, d.TotalWorkedMinutes).Should().Be((60, 0, 480));
    }

    [Fact]
    public async Task C05_SalidaTemporal_PendienteSeDescuenta()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "08:00", "10:00", "10:40", "16:00").Calcular(Lun);
        (d.HasTemporaryExits, d.TemporaryExitMinutes, d.DeductedExitMinutes, d.TotalWorkedMinutes, d.EarlyDepartureMinutes)
            .Should().Be((true, 40, 40, 440, 40));
        d.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public async Task C06_SalidaTemporal_PermisoConGoce()
    {
        var e = new Escenario(Matutino()).Marcas(Lun, "08:00", "10:00", "10:40", "16:00");
        var d = await e.Calcular(Lun);
        var salida = d.TemporaryExits.Single();
        var r = await new ClassifyTemporaryExitCommandHandler(e.H.Decisions()).Handle(
            new ClassifyTemporaryExitCommand(e.Emp.Value, DateOnly.FromDateTime(Lun), salida.ExitRecordId, TemporaryExitStatus.ApprovedPaid, "doc"),
            CancellationToken.None);
        r.IsSuccess.Should().BeTrue(r.IsSuccess ? "" : r.Error);

        d = e.Jornada(Lun);
        (d.DeductedExitMinutes, d.TotalWorkedMinutes, d.EarlyDepartureMinutes, d.RequiresReview).Should().Be((0, 480, 0, false));
    }

    [Fact]
    public async Task C07_SegundaAusenciaEnLaVentana_EsSalidaTemporal()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "08:00", "13:00", "13:20", "13:40", "14:20", "16:40").Calcular(Lun);
        (d.LunchMinutesTaken, d.TemporaryExitMinutes, d.TotalWorkedMinutes, d.EarlyDepartureMinutes).Should().Be((20, 40, 480, 0));
    }

    [Fact]
    public async Task C08_VentanaDeComidaApagada()
    {
        var e = new Escenario(Matutino()).Marcas(Lun, "08:00", "13:00", "13:50", "16:00");
        var config = SystemConfiguration.CreateDefault();
        config.SetLunchWindowEnabled(false);
        e.H.Configuration = config;
        var d = await e.Calcular(Lun);
        (d.LunchMinutesTaken, d.TemporaryExitMinutes, d.DeductedExitMinutes, d.TotalWorkedMinutes).Should().Be((0, 50, 50, 430));
    }

    [Fact]
    public async Task C09_MarcajeIntermedioSinPareja()
    {
        var d = await new Escenario(Matutino()).Marcas(Lun, "08:00", "11:00", "16:00").Calcular(Lun);
        (d.TotalWorkedMinutes, d.HasTemporaryExits).Should().Be((480, false));
        d.CalculationIssue.Should().Contain("Marcaje intermedio sin pareja a las 11:00");
    }

    // ================================================================ N: nocturno

    [Fact]
    public async Task N01_Nocturno_Puntual()
    {
        var d = await new Escenario(Nocturno()).Marcas(Lun, "22:00", "+1 06:00").Calcular(Lun);
        (Hora(d.CivilCheckIn), Hora(d.CivilCheckOut), d.TotalWorkedMinutes, d.LateMinutes).Should().Be(("Mon 22:00", "Tue 06:00", 480, 0));
    }

    [Fact]
    public async Task N02_Nocturno_Retardo()
    {
        var d = await new Escenario(Nocturno()).Marcas(Lun, "22:30", "+1 06:00").Calcular(Lun);
        (d.LateMinutes, d.TotalWorkedMinutes).Should().Be((30, 450));
    }

    [Fact]
    public async Task N03_Nocturno_TiempoExtra()
    {
        var d = await new Escenario(Nocturno()).Marcas(Lun, "22:00", "+1 08:00").Calcular(Lun);
        (d.TotalWorkedMinutes, d.RawOvertimeMinutes).Should().Be((600, 120));
    }

    [Fact]
    public async Task N04_Nocturno_ComidaEnVentanaNocturna()
    {
        var d = await new Escenario(Nocturno()).Marcas(Lun, "22:00", "+1 02:00", "+1 02:45", "+1 06:15").Calcular(Lun);
        (d.LunchMinutesTaken, d.LunchExcessMinutes, d.TotalWorkedMinutes, d.EarlyDepartureMinutes).Should().Be((45, 15, 480, 0));
    }

    [Fact]
    public async Task N05_NocturnoQueEmpiezaAMedianoche_EntradaDelDiaAnterior()
    {
        var d = await new Escenario(Nocturno(TimeSpan.Zero)).Marcas(Lun, "-1 23:50", "08:00").Calcular(Lun);
        (Hora(d.CivilCheckIn), Hora(d.CivilCheckOut), d.TotalWorkedMinutes, d.LateMinutes).Should().Be(("Sun 23:50", "Mon 08:00", 480, 0));
    }

    // ================================================================ M: mixto y rotativo

    [Fact]
    public async Task M01_Mixto_UsaElHorarioDeCadaDia()
    {
        var mat = Matutino();
        var ves = Vespertino();
        var dias = new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }
            .Select(d => ShiftDay.ForShift(d, mat))
            .Concat(new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday }.Select(d => ShiftDay.ForShift(d, ves)));
        var mixto = Shift.Create("Mixto", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Mixto, days: dias);
        var e = new Escenario(mixto, otrosTurnos: new[] { mat, ves })
            .Marcas(Lun, "08:00", "16:00").Marcas(Mar, "14:20", "22:00");
        await e.Calcular(Lun, Mar);
        (e.Jornada(Lun).ShiftName, e.Jornada(Lun).TotalWorkedMinutes).Should().Be(("Matutino 08-16", 480));
        (e.Jornada(Mar).ShiftName, e.Jornada(Mar).LateMinutes, e.Jornada(Mar).TotalWorkedMinutes).Should().Be(("Vespertino 14-22", 20, 450));
    }

    [Fact]
    public async Task M02_Mixto_DiaSinHorario_SeCalculaSinTurnoYSeReporta()
    {
        var mat = Matutino();
        var dias = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday }.Select(d => ShiftDay.ForShift(d, mat));
        var mixto = Shift.Create("Mixto incompleto", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(8), ShiftType.Mixto, days: dias);
        var d = await new Escenario(mixto, otrosTurnos: new[] { mat }).Marcas(Mie, "08:00", "16:00").Calcular(Mie);
        (d.ShiftId, d.TotalWorkedMinutes).Should().Be((null, 480));
        d.CalculationIssue.Should().Contain("no tiene horario para el miércoles");
    }

    [Fact]
    public async Task M03_Rotativo_TurnoYDescansoDelRol()
    {
        var mat = Matutino();
        var ves = Vespertino();
        var e = new Escenario(null, tipo: ShiftType.Rotativo, otrosTurnos: new[] { mat, ves }).Marcas(Lun, "14:00", "22:00");
        e.H.Store.Rosters.Add(ShiftRoster.Create(e.Emp, Lun, ves.Id, false));
        e.H.Store.Rosters.Add(ShiftRoster.Create(e.Emp, Mar, null, true));
        await e.Calcular(Lun, Mar);
        (e.Jornada(Lun).ShiftName, e.Jornada(Lun).TotalWorkedMinutes, e.Jornada(Lun).IsAutoDetectedShift).Should().Be(("Vespertino 14-22", 480, false));
        (e.Jornada(Mar).IsRestDay, e.Jornada(Mar).IsAbsent).Should().Be((true, false));
    }

    [Fact]
    public async Task M04_Rotativo_SinRol_Autodetecta()
    {
        var e = new Escenario(null, tipo: ShiftType.Rotativo, otrosTurnos: new[] { Matutino(), Vespertino() }).Marcas(Mie, "07:50", "16:00");
        var d = await e.Calcular(Mie);
        (d.ShiftName, d.IsAutoDetectedShift, d.TotalWorkedMinutes).Should().Be(("Matutino 08-16", true, 480));
    }

    // ================================================================ S: sin horario y autodetección

    [Fact]
    public async Task S01_SinHorario_AutodetectaElTurnoMasCercano()
    {
        var d = await new Escenario(null, otrosTurnos: new[] { Matutino(), Vespertino() }).Marcas(Lun, "13:40", "22:05").Calcular(Lun);
        (d.ShiftName, d.IsAutoDetectedShift, d.TotalWorkedMinutes, d.RawOvertimeMinutes).Should().Be(("Vespertino 14-22", true, 485, 5));
    }

    [Fact]
    public async Task S02_SinHorario_NingunTurnoAMenosDe5Horas()
    {
        var d = await new Escenario(null, otrosTurnos: new[] { Matutino(), Vespertino() }).Marcas(Lun, "19:30", "+1 03:00").Calcular(Lun);
        (d.ShiftId, d.IsWithoutShift, d.RawOvertimeMinutes).Should().Be((null, true, 0));
    }

    [Fact]
    public async Task S03_SinTurno_SoloPrimerYUltimoMarcajeDelDia()
    {
        var d = await new Escenario(null).Marcas(Lun, "10:00", "14:00", "18:30").Calcular(Lun);
        (Hora(d.CivilCheckIn), Hora(d.CivilCheckOut), d.TotalWorkedMinutes, d.RawOvertimeMinutes, d.LateMinutes)
            .Should().Be(("Mon 10:00", "Mon 18:30", 510, 0, 0));
        d.Notes.Should().Contain("Sin turno asignado: asígnele un horario");
    }

    // ================================================================ P: partido

    [Fact]
    public async Task P01_Partido_Puntual()
    {
        var d = await new Escenario(Partido()).Marcas(Lun, "09:00", "13:00", "15:00", "19:00").Calcular(Lun);
        (d.TotalWorkedMinutes, d.LateMinutes, d.EarlyDepartureMinutes, d.RawOvertimeMinutes).Should().Be((480, 0, 0, 0));
    }

    [Fact]
    public async Task P02_Partido_RetardoEnElSegundoBloque()
    {
        var d = await new Escenario(Partido()).Marcas(Lun, "09:00", "13:00", "15:20", "19:00").Calcular(Lun);
        (d.LateMinutes, d.TotalWorkedMinutes, d.RawOvertimeMinutes).Should().Be((20, 450, 0));
    }

    [Fact]
    public async Task P03_Partido_ExcedenteDeUnBloqueCompensaElOtro()
    {
        var d = await new Escenario(Partido()).Marcas(Lun, "09:00", "12:30", "15:00", "19:30").Calcular(Lun);
        (d.EarlyDepartureMinutes, d.TotalWorkedMinutes, d.RawOvertimeMinutes).Should().Be((30, 480, 0));
    }

    [Fact]
    public async Task P04_Partido_TiempoExtra()
    {
        var d = await new Escenario(Partido()).Marcas(Lun, "09:00", "13:00", "15:00", "20:00").Calcular(Lun);
        (d.TotalWorkedMinutes, d.RawOvertimeMinutes).Should().Be((540, 60));
    }

    [Fact]
    public async Task P05_Partido_TresMarcajes_BloqueIncompletoCuentaCero()
    {
        var d = await new Escenario(Partido()).Marcas(Lun, "09:00", "13:00", "19:00").Calcular(Lun);
        (d.MissingBlock2CheckIn, d.TotalWorkedMinutes, d.RawOvertimeMinutes).Should().Be((true, 240, 0));
    }

    // ================================================================ F: flexible

    /// <summary>Flexible con ventana de llegada 07:00–09:00, 8 h diarias, sin tolerancia.</summary>
    private static Shift FlexibleConVentana(bool redondeo = false) => Shift.Create("Flexible 07-09", new TimeSpan(7, 0, 0),
        redondeo ? 5 : 0, TimeSpan.FromHours(8), ShiftType.Flexible, flexWindowEndTime: new TimeSpan(9, 0, 0),
        roundingsEnabled: redondeo, roundingInterval: redondeo ? 15 : 0);

    [Fact]
    public async Task F01_Flexible_LlegaDentroDeLaVentana()
    {
        var d = await new Escenario(FlexibleConVentana()).Marcas(Lun, "08:30", "16:30").Calcular(Lun);
        (d.LateMinutes, d.TotalWorkedMinutes, d.EarlyDepartureMinutes, d.RawOvertimeMinutes).Should().Be((0, 480, 0, 0));
        d.DynamicScheduledCheckOut.Should().Be(Lun.AddHours(16.5));
    }

    [Fact]
    public async Task F02_Flexible_LlegaDespuesDeLaVentana()
    {
        var d = await new Escenario(FlexibleConVentana()).Marcas(Lun, "09:20", "17:20").Calcular(Lun);
        (d.LateMinutes, d.TotalWorkedMinutes).Should().Be((20, 480));
    }

    [Fact]
    public async Task F03_Flexible_JornadaCorta()
    {
        var d = await new Escenario(FlexibleConVentana()).Marcas(Lun, "08:00", "15:00").Calcular(Lun);
        (d.EarlyDepartureMinutes, d.TotalWorkedMinutes).Should().Be((60, 420));
    }

    [Theory]
    [InlineData("08:04", "16:00", 480)] // 08:04 → 08:00 (dentro de los 5 min)
    [InlineData("08:07", "16:15", 480)] // 08:07 → 08:15
    public async Task F04_Flexible_RedondeoDeEntrada(string entrada, string salida, int trabajado)
    {
        var d = await new Escenario(FlexibleConVentana(redondeo: true)).Marcas(Lun, entrada, salida).Calcular(Lun);
        d.TotalWorkedMinutes.Should().Be(trabajado);
    }

    [Fact]
    public async Task F05_Flexible_SinVentana_EntradaLibre()
    {
        var turno = Shift.Create("Flexible libre 8h", new TimeSpan(8, 0, 0), 0, TimeSpan.FromHours(8), ShiftType.Flexible, hasEntryWindow: false);
        var d = await new Escenario(turno).Marcas(Lun, "13:00", "21:30").Calcular(Lun);
        (d.LateMinutes, d.TotalWorkedMinutes, d.RawOvertimeMinutes).Should().Be((0, 510, 30));
    }

    [Fact]
    public async Task F06_Flexible_MultiMarcaje()
    {
        var d = await new Escenario(AttendanceRegressionFixtures.CreateMultiIntervalShift()).Marcas(Lun, "08:00", "12:00", "13:00", "17:30").Calcular(Lun);
        (d.TotalWorkedMinutes, d.RawOvertimeMinutes, d.HasOpenInterval).Should().Be((510, 30, false));
    }

    [Fact]
    public async Task F07_Flexible_MultiMarcaje_IntervaloAbierto()
    {
        var d = await new Escenario(AttendanceRegressionFixtures.CreateMultiIntervalShift()).Marcas(Lun, "08:00", "12:00", "13:00").Calcular(Lun);
        (d.TotalWorkedMinutes, d.HasOpenInterval, d.MissingCheckOut, d.EarlyDepartureMinutes).Should().Be((240, true, true, 240));
        d.CalculationIssue.Should().Contain("Intervalo abierto sin salida registrada desde las 13:00");
    }

    [Fact]
    public async Task F08_Guardia24h_DiaSiguienteDeDescanso()
    {
        var d = await new Escenario(AttendanceRegressionFixtures.CreateGuard24hShift(), descansos: new[] { WeekDay.Martes })
            .Marcas(Lun, "08:00", "+1 08:30").Calcular(Lun);
        (Hora(d.CivilCheckOut), d.TotalWorkedMinutes, d.RawOvertimeMinutes).Should().Be(("Tue 08:30", 1470, 30));
    }

    [Fact]
    public async Task F09_Guardia24h_DiaSiguienteLaborable_LaVentanaTerminaALas24h()
    {
        var e = new Escenario(AttendanceRegressionFixtures.CreateGuard24hShift()).Marcas(Lun, "08:00", "+1 08:30");
        var d = await e.Calcular(Lun);
        (d.MissingCheckOut, d.TotalWorkedMinutes).Should().Be((true, 0));
    }

    // ================================================================ B: bolsa semanal

    private static Shift BolsaSemanal40() => Shift.Create("Bolsa 40h", new TimeSpan(8, 0, 0), 0, TimeSpan.Zero, ShiftType.Flexible,
        weeklyWorkHours: TimeSpan.FromHours(40), hasEntryWindow: false);

    [Fact]
    public async Task B01_BolsaSemanal_ExtraDesdeQueSeCruzaLaMeta()
    {
        var e = new Escenario(BolsaSemanal40(), descansos: new[] { WeekDay.Sabado, WeekDay.Domingo });
        for (var i = 0; i < 5; i++) e.Marcas(Lun.AddDays(i), "08:00", "17:00"); // 9 h diarias
        await e.Calcular(Lun, Dom);
        Enumerable.Range(0, 4).Select(i => e.Jornada(Lun.AddDays(i)).RawOvertimeMinutes).Should().AllBeEquivalentTo(0);
        (e.Jornada(Lun.AddDays(4)).TotalWorkedMinutes, e.Jornada(Lun.AddDays(4)).RawOvertimeMinutes, e.Jornada(Lun.AddDays(4)).OvertimeMinutes)
            .Should().Be((540, 300, 300));
        e.Jornada(Lun).EarlyDepartureMinutes.Should().Be(0);
    }

    [Fact]
    public async Task B02_BolsaSemanal_Faltante()
    {
        var e = new Escenario(BolsaSemanal40(), descansos: new[] { WeekDay.Sabado, WeekDay.Domingo });
        for (var i = 0; i < 5; i++) e.Marcas(Lun.AddDays(i), "08:00", "15:36"); // 456 min diarios = 38 h
        await e.Calcular(Lun, Dom);
        // El faltante se reporta en el último día de la semana con bolsa: el domingo (descanso) también la lleva.
        (e.Jornada(Dom).IsRestDay, e.Jornada(Dom).WeeklyShortfallMinutes).Should().Be((true, 120));
        e.Jornada(Lun.AddDays(4)).WeeklyShortfallMinutes.Should().Be(0);
    }

    // ================================================================ A: decisiones de RH

    [Fact]
    public async Task A01_AutorizacionParcialEnBloquesDe30()
    {
        var e = new Escenario(Matutino(), extrasAutorizadas: false).Marcas(Lun, "08:00", "18:10");
        await e.Calcular(Lun);
        var r = await new SetDailyOvertimeAuthorizationCommandHandler(e.H.Decisions()).Handle(
            new SetDailyOvertimeAuthorizationCommand(e.Emp.Value, DateOnly.FromDateTime(Lun), true, false, "doc", null, 60, 130),
            CancellationToken.None);
        r.IsSuccess.Should().BeTrue(r.IsSuccess ? "" : r.Error);
        (e.Jornada(Lun).RawOvertimeMinutes, e.Jornada(Lun).OvertimeMinutes).Should().Be((130, 60));
    }

    [Theory]
    [InlineData(130, 130, true)]
    [InlineData(60, 130, true)]
    [InlineData(45, 130, false)]
    [InlineData(150, 130, false)]
    public void A02_ReglaDeAutorizacionParcial(int autorizados, int bruto, bool valida) =>
        DailyAttendance.IsValidOvertimeAuthorization(autorizados, bruto).Should().Be(valida);
}
