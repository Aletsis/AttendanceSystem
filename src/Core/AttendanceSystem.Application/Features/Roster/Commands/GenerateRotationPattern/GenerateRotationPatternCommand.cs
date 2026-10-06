using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Roster.Commands.GenerateRotationPattern;

public record CustomRotationSlotDto(Guid? ShiftId, bool IsRestDay, string? Notes = null);

public record GenerateRotationPatternCommand(
    List<string> EmployeeIds,
    DateTime StartDate,
    DateTime? EndDate = null,
    RotationSchemeType SchemeType = RotationSchemeType.Rotativo3x8,
    List<Guid>? ShiftIds = null,
    int DaysPerShift = 7,
    int RestDaysAfterRotation = 2,
    List<CustomRotationSlotDto>? CustomSlots = null,
    bool IsIndefinite = false) : IRequest<int>;

public class GenerateRotationPatternCommandHandler : IRequestHandler<GenerateRotationPatternCommand, int>
{
    private readonly IShiftRosterRepository _rosterRepository;
    private readonly IEmployeeRepository? _employeeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public GenerateRotationPatternCommandHandler(
        IShiftRosterRepository rosterRepository,
        IUnitOfWork unitOfWork)
        : this(rosterRepository, null, unitOfWork)
    {
    }

    public GenerateRotationPatternCommandHandler(
        IShiftRosterRepository rosterRepository,
        IEmployeeRepository? employeeRepository,
        IUnitOfWork unitOfWork)
    {
        _rosterRepository = rosterRepository;
        _employeeRepository = employeeRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<int> Handle(GenerateRotationPatternCommand request, CancellationToken cancellationToken)
    {
        if (request.EmployeeIds == null || !request.EmployeeIds.Any())
            return 0;

        var effectiveEndDate = (request.IsIndefinite || !request.EndDate.HasValue)
            ? (request.EndDate ?? request.StartDate.Date.AddYears(1)).Date
            : request.EndDate.Value.Date;

        if (effectiveEndDate < request.StartDate.Date)
            throw new ArgumentException("La fecha final no puede ser anterior a la fecha inicial.");

        int totalGenerated = 0;
        var totalDays = (effectiveEndDate - request.StartDate.Date).Days + 1;

        foreach (var empIdStr in request.EmployeeIds)
        {
            var empId = EmployeeId.From(empIdStr);

            if (_employeeRepository != null)
            {
                var employee = await _employeeRepository.GetByIdAsync(empId, cancellationToken);
                if (employee != null && employee.ShiftType != ShiftType.Rotativo)
                {
                    employee.SetShiftType(ShiftType.Rotativo);
                    _employeeRepository.Update(employee);
                }
            }

            // Cargar los existentes en el rango para actualizar o insertar
            var existingRosters = (await _rosterRepository.GetByEmployeeAsync(
                empId,
                request.StartDate.Date,
                effectiveEndDate,
                cancellationToken))
                .ToDictionary(r => r.Date.Date);

            var newRosters = new List<ShiftRoster>();

            for (int dayOffset = 0; dayOffset < totalDays; dayOffset++)
            {
                var currentDate = request.StartDate.Date.AddDays(dayOffset);
                (ShiftId? shiftId, bool isRestDay, string notes) = DetermineSlot(request, dayOffset);

                if (existingRosters.TryGetValue(currentDate, out var existing))
                {
                    existing.Update(shiftId, isRestDay, notes);
                    await _rosterRepository.UpdateAsync(existing, cancellationToken);
                }
                else
                {
                    var newRoster = ShiftRoster.Create(empId, currentDate, shiftId, isRestDay, notes);
                    newRosters.Add(newRoster);
                }

                totalGenerated++;
            }

            if (newRosters.Any())
            {
                await _rosterRepository.AddRangeAsync(newRosters, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return totalGenerated;
    }

    private static (ShiftId? shiftId, bool isRestDay, string notes) DetermineSlot(
        GenerateRotationPatternCommand request,
        int dayOffset)
    {
        var shiftIds = request.ShiftIds ?? new List<Guid>();

        switch (request.SchemeType)
        {
            case RotationSchemeType.Esquema4x3:
                {
                    // Ciclo de 7 días: 4 días trabajo, 3 días descanso
                    int dayInCycle = dayOffset % 7;
                    var baseShift = shiftIds.FirstOrDefault();
                    if (dayInCycle < 4 && baseShift != Guid.Empty)
                    {
                        return (ShiftId.From(baseShift), false, "Esquema 4x3 (Jornada)");
                    }
                    else
                    {
                        return (null, true, "Esquema 4x3 (Descanso)");
                    }
                }

            case RotationSchemeType.Esquema24x48:
                {
                    // Ciclo de 3 días: 1 día de guardia 24h, 2 días de descanso (48h)
                    int dayInCycle = dayOffset % 3;
                    var baseShift = shiftIds.FirstOrDefault();
                    if (dayInCycle == 0 && baseShift != Guid.Empty)
                    {
                        return (ShiftId.From(baseShift), false, "Esquema 24x48 (Guardia 24h)");
                    }
                    else
                    {
                        return (null, true, "Esquema 24x48 (Descanso)");
                    }
                }

            case RotationSchemeType.Rotativo3x8:
                {
                    // 3 turnos rotativos: Mañana, Tarde, Noche
                    // Rota cada DaysPerShift días (ej. 7 días Mañana, 7 días Tarde, 7 días Noche)
                    // O ciclo de trabajo + descanso
                    if (!shiftIds.Any())
                        return (null, true, "Rotativo 3x8");

                    int daysPerShift = Math.Max(1, request.DaysPerShift);
                    int shiftIndex = (dayOffset / daysPerShift) % shiftIds.Count;
                    var currentShiftGuid = shiftIds[shiftIndex];

                    // Si hay días de descanso tras completar un bloque de rotación
                    int dayInShiftBlock = dayOffset % daysPerShift;
                    int workDaysInBlock = Math.Max(1, daysPerShift - request.RestDaysAfterRotation);

                    if (request.RestDaysAfterRotation > 0 && dayInShiftBlock >= workDaysInBlock)
                    {
                        return (null, true, "Rotativo 3x8 (Descanso)");
                    }

                    return (ShiftId.From(currentShiftGuid), false, $"Rotativo 3x8 (Turno {shiftIndex + 1})");
                }

            case RotationSchemeType.Rotativo2x8:
                {
                    // 2 turnos rotativos: ej. Turno 1 y Turno 2
                    // Rota cada DaysPerShift días
                    // O ciclo de trabajo + descanso
                    if (!shiftIds.Any())
                        return (null, true, "Rotativo 2x8");

                    int daysPerShift = Math.Max(1, request.DaysPerShift);
                    int shiftIndex = (dayOffset / daysPerShift) % shiftIds.Count;
                    var currentShiftGuid = shiftIds[shiftIndex];

                    // Si hay días de descanso tras completar un bloque de rotación
                    int dayInShiftBlock = dayOffset % daysPerShift;
                    int workDaysInBlock = Math.Max(1, daysPerShift - request.RestDaysAfterRotation);

                    if (request.RestDaysAfterRotation > 0 && dayInShiftBlock >= workDaysInBlock)
                    {
                        return (null, true, "Rotativo 2x8 (Descanso)");
                    }

                    return (ShiftId.From(currentShiftGuid), false, $"Rotativo 2x8 (Turno {shiftIndex + 1})");
                }

            case RotationSchemeType.Personalizado:
                {
                    if (request.CustomSlots != null && request.CustomSlots.Any())
                    {
                        int slotIndex = dayOffset % request.CustomSlots.Count;
                        var slot = request.CustomSlots[slotIndex];
                        var shiftId = slot.ShiftId.HasValue ? ShiftId.From(slot.ShiftId.Value) : null;
                        return (shiftId, slot.IsRestDay, slot.Notes ?? "Patrón Personalizado");
                    }
                    return (null, true, "Descanso");
                }

            default:
                return (null, true, "Descanso");
        }
    }
}
