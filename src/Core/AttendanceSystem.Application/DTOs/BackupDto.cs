namespace AttendanceSystem.Application.DTOs;

public class BackupDto
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string BackupType { get; set; } = string.Empty; // "Full", "DatabaseOnly", "ConfigOnly"
    public string Description { get; set; } = string.Empty;
}

public class BackupResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? BackupFilePath { get; set; }
    public long? SizeInBytes { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class RestoreResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime? RestoredAt { get; set; }
}
