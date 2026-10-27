using Prism.Dialogs;

namespace AttendanceSystem.WPF.Services
{
    /// <summary>Caso 13: pide el motivo de un ajuste manual (asignar o desasignar un marcaje, cambiar el horario del día).</summary>
    public static class ReasonPrompt
    {
        /// <returns>El motivo escrito, o null si se canceló.</returns>
        public static Task<string?> AskReasonAsync(this IDialogService dialogs, string title, string message)
        {
            var completion = new TaskCompletionSource<string?>();
            var parameters = new DialogParameters
            {
                { "Title", title },
                { "Message", message }
            };
            dialogs.ShowDialog("ReasonDialog", parameters, result =>
                completion.TrySetResult(result.Result == ButtonResult.OK ? result.Parameters.GetValue<string>("Reason") : null));
            return completion.Task;
        }
    }
}
