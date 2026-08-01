using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Positions.Queries.GetPositions;
using MediatR;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Dialogs;

namespace AttendanceSystem.WPF.ViewModels.Departments
{
    public class DepartmentDetailViewModel : BindableBase, IDialogAware
    {
        private readonly IMediator _mediator;
        private Guid? _departmentId;
        private string _name = string.Empty;
        private string? _description;
        private string _title = "Nuevo Departamento";
        private ObservableCollection<SelectablePosition> _positions = new();

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string? Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public ObservableCollection<SelectablePosition> Positions
        {
            get => _positions;
            set => SetProperty(ref _positions, value);
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public DialogCloseListener RequestClose { get; }

        public DepartmentDetailViewModel(IMediator mediator)
        {
            _mediator = mediator;
            SaveCommand = new DelegateCommand(ExecuteSave, CanExecuteSave)
                .ObservesProperty(() => Name);
            
            CancelCommand = new DelegateCommand(ExecuteCancel);
        }

        private bool CanExecuteSave()
        {
            return !string.IsNullOrWhiteSpace(Name);
        }

        private void ExecuteSave()
        {
            var selectedPositionIds = Positions
                .Where(p => p.IsSelected)
                .Select(p => p.Id)
                .ToList();

            var parameters = new DialogParameters
            {
                { "DepartmentId", _departmentId },
                { "Name", Name },
                { "Description", Description },
                { "PositionIds", selectedPositionIds }
            };

            RequestClose.Invoke(parameters, ButtonResult.OK);
        }

        private void ExecuteCancel()
        {
            RequestClose.Invoke(ButtonResult.Cancel);
        }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public async void OnDialogOpened(IDialogParameters parameters)
        {
            await LoadPositionsAsync();

            if (parameters.ContainsKey("DepartmentId"))
            {
                _departmentId = parameters.GetValue<Guid>("DepartmentId");
                Name = parameters.GetValue<string>("Name");
                Description = parameters.GetValue<string>("Description");
                var positionIds = parameters.GetValue<List<Guid>>("PositionIds") ?? new List<Guid>();

                foreach (var pos in Positions)
                {
                    if (positionIds.Contains(pos.Id))
                    {
                        pos.IsSelected = true;
                    }
                }

                Title = "Editar Departamento";
            }
        }

        private async System.Threading.Tasks.Task LoadPositionsAsync()
        {
            var result = await _mediator.Send(new GetPositionsQuery());
            if (result.IsSuccess)
            {
                Positions = new ObservableCollection<SelectablePosition>(
                    result.Value.Select(p => new SelectablePosition { Id = p.Id, Name = p.Name })
                );
            }
        }
    }

    public class SelectablePosition : BindableBase
    {
        private bool _isSelected;
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
