using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Roster.Commands.GenerateRotationPattern;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Roster.Commands.ExtendRotationPatterns;

/// <summary>
/// Caso 10 (4a): mantiene proyectado el roster de los esquemas rotativos indefinidos, para que un rotativo nunca
/// llegue a un día sin turno. Devuelve los días generados.
/// </summary>
[RequiresPermission(Permissions.RosterManage)]
public sealed record ExtendRotationPatternsCommand(DateTime? Today = null) : IRequest<int>;

public sealed class ExtendRotationPatternsCommandHandler : IRequestHandler<ExtendRotationPatternsCommand, int>
{
    /// <summary>Días hacia adelante que siempre tiene proyectados un esquema indefinido.</summary>
    public const int HorizonDays = 60;

    private readonly IRotationPatternRepository _patterns;
    private readonly IShiftRosterRepository _rosters;
    private readonly IEmployeeRepository _employees;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceConsolidationRepository? _consolidations;
    private readonly IConsolidationWakeup? _wakeup;

    public ExtendRotationPatternsCommandHandler(
        IRotationPatternRepository patterns,
        IShiftRosterRepository rosters,
        IEmployeeRepository employees,
        IUnitOfWork unitOfWork,
        IAttendanceConsolidationRepository? consolidations = null,
        IConsolidationWakeup? wakeup = null)
    {
        _patterns = patterns;
        _rosters = rosters;
        _employees = employees;
        _unitOfWork = unitOfWork;
        _consolidations = consolidations;
        _wakeup = wakeup;
    }

    public async Task<int> Handle(ExtendRotationPatternsCommand request, CancellationToken cancellationToken)
    {
        var today = (request.Today ?? DateTime.Today).Date;
        var target = today.AddDays(HorizonDays);
        int generated = 0;
        var recalculate = new List<(EmployeeId Employee, DateTime From)>();

        foreach (var pattern in await _patterns.GetIndefiniteAsync(cancellationToken))
        {
            if (pattern.GeneratedThrough >= target) continue;
            var employee = await _employees.GetByIdAsync(pattern.EmployeeId, cancellationToken);
            if (employee == null) continue;

            var command = RotationPatternDefinition.Deserialize(pattern.Definition).ToCommand(pattern.EmployeeId.Value, pattern.StartDate);
            var from = pattern.GeneratedThrough.AddDays(1);
            var existing = (await _rosters.GetByEmployeeAsync(pattern.EmployeeId, from, target, cancellationToken)).ToDictionary(r => r.Date.Date);
            var added = new List<ShiftRoster>();
            var last = pattern.GeneratedThrough;

            for (var date = from; date <= target; date = date.AddDays(1))
            {
                // Dejó de ser rotativo (o de trabajar en la empresa): el patrón termina ahí.
                if (employee.AssignmentOn(date).ShiftType != ShiftType.Rotativo)
                {
                    pattern.EndBefore(date);
                    break;
                }

                var (shiftId, isRestDay, notes) = GenerateRotationPatternCommandHandler.DetermineSlot(
                    command, (date - pattern.StartDate).Days, date, employee);
                if (existing.TryGetValue(date, out var roster))
                {
                    roster.Update(shiftId, isRestDay, notes);
                    await _rosters.UpdateAsync(roster, cancellationToken);
                }
                else
                {
                    added.Add(ShiftRoster.Create(pattern.EmployeeId, date, shiftId, isRestDay, notes));
                }
                last = date;
                generated++;
            }

            if (added.Count > 0) await _rosters.AddRangeAsync(added, cancellationToken);
            pattern.MarkGeneratedThrough(last);
            // Si la extensión llegó tarde (el proceso no corrió), los días ya transcurridos se recalculan.
            if (from <= today && last >= from) recalculate.Add((pattern.EmployeeId, from));
        }

        if (_consolidations != null)
        {
            foreach (var (employee, from) in recalculate)
                await _consolidations.RequestManyAsync(new List<EmployeeId> { employee }, from, today, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (recalculate.Count > 0) _wakeup?.Signal();
        return generated;
    }
}
