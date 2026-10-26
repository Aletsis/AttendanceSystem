using AttendanceSystem.Application.Features.Attendance.Consolidation;
using Prism.Dialogs;

namespace AttendanceSystem.WPF.Services
{
    /// <summary>Caso 15: pregunta desde qué fecha aplica un cambio que afecta el cálculo.</summary>
    public static class EffectiveDatePrompt
    {
        /// <returns>La fecha elegida, o null si se canceló.</returns>
        public static Task<DateTime?> AskEffectiveDateAsync(this IDialogService dialogs, string title, string message, Func<DateTime, Task<RecalculationImpactDto>> impact)
        {
            var completion = new TaskCompletionSource<DateTime?>();
            var parameters = new DialogParameters
            {
                { "Title", title },
                { "Message", message },
                { "Impact", impact }
            };
            dialogs.ShowDialog("EffectiveDateDialog", parameters, result =>
                completion.TrySetResult(result.Result == ButtonResult.OK ? result.Parameters.GetValue<DateTime>("EffectiveDate") : null));
            return completion.Task;
        }
    }
}
