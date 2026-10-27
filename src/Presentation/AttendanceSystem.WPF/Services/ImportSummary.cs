namespace AttendanceSystem.WPF.Services
{
    /// <summary>
    /// Resultado de una importación desde Excel: cuántas filas entraron y, de las que no, por qué. Antes sólo se
    /// informaba el número de errores y había que adivinar qué filas fallaron.
    /// </summary>
    public sealed class ImportSummary
    {
        private const int MaxListedErrors = 15;
        private readonly List<string> _errors = new();

        public int Imported { get; private set; }
        public int Failed => _errors.Count;

        public void Success() => Imported++;

        public void Fail(string item, string? reason) => _errors.Add($"• {item}: {reason ?? "error desconocido"}");

        public string ToMessage()
        {
            var message = $"Importación finalizada. Registros importados: {Imported}. Con error: {Failed}.";
            if (Failed == 0) return message;

            var listed = string.Join("\n", _errors.Take(MaxListedErrors));
            var more = Failed > MaxListedErrors ? $"\n… y {Failed - MaxListedErrors} más." : string.Empty;
            return $"{message}\n\n{listed}{more}";
        }
    }
}
