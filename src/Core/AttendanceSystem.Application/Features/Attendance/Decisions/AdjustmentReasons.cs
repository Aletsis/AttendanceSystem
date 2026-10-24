namespace AttendanceSystem.Application.Features.Attendance.Decisions;

/// <summary>
/// Caso 13: en qué ajustes el motivo es obligatorio. Todos los ajustes aceptan motivo y lo guardan en el historial;
/// sólo los de esta lista lo exigen. Es la única lista: para pedirlo en otro ajuste basta con agregarlo aquí (y el
/// campo en la pantalla).
/// </summary>
public static class AdjustmentReasons
{
    public const string ManualPunch = "MarcajeManual";
    public const string VoidManualPunch = "AnularMarcajeManual";
    public const string AssignPunch = "AsignarMarcaje";
    public const string UnassignPunch = "DesasignarMarcaje";

    public const string RequiredMessage = "Indique el motivo.";

    private static readonly HashSet<string> Required = new(StringComparer.Ordinal)
    {
        ManualPunch,
        VoidManualPunch,
        AssignPunch,
        UnassignPunch,
    };

    public static bool IsRequired(string adjustment) => Required.Contains(adjustment);

    /// <summary>Error si el ajuste exige motivo y no lo trae; <c>null</c> si es válido.</summary>
    public static string? Validate(string adjustment, string? reason) =>
        IsRequired(adjustment) && string.IsNullOrWhiteSpace(reason) ? RequiredMessage : null;
}
