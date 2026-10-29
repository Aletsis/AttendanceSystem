using System.Globalization;
using System.Text;

namespace AttendanceSystem.Application.Features.Devices;

/// <summary>
/// Nombre de usuario tal como lo acepta un reloj ZKTeco, igual por SDK y por ADMS. Muchos equipos muestran
/// basura con acentos o eñes y descartan el nombre entero si excede su límite (24 caracteres).
/// </summary>
public static class DeviceUserName
{
    public const int MaxLength = 24;

    public static string ForZkDevice(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var builder = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            // Tabuladores y saltos de línea romperían el formato de los comandos ADMS.
            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        var clean = builder.ToString().Normalize(NormalizationForm.FormC)
            .Replace("ñ", "n").Replace("Ñ", "N")
            .Trim();

        return clean.Length > MaxLength ? clean[..MaxLength].TrimEnd() : clean;
    }
}
