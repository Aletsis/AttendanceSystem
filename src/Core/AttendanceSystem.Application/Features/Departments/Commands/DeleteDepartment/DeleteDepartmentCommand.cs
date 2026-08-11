using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Departments.Commands.DeleteDepartment;

public sealed record DeleteDepartmentCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteDepartmentCommandHandler : IRequestHandler<DeleteDepartmentCommand, Result>
{
    private readonly IDepartmentRepository _repository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteDepartmentCommandHandler(
        IDepartmentRepository repository,
        IEmployeeRepository employeeRepository,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _employeeRepository = employeeRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteDepartmentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var departmentId = DepartmentId.From(request.Id);
            
            var department = await _repository.GetByIdAsync(departmentId, cancellationToken);
            if (department == null)
            {
                return Result.Failure("Departamento no encontrado.");
            }

            // Revisar si el departamento está en uso por algún empleado
            if (await _employeeRepository.IsDepartmentInUseAsync(departmentId, cancellationToken))
            {
                return Result.Failure("No se puede eliminar el departamento porque hay empleados asignados a él.");
            }

            _repository.Delete(department);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
