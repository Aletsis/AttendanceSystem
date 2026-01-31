namespace AttendanceSystem.Blazor.Server.Models;

public class PredefinedPeriod
{
    public string Name { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    
    public override string ToString() => Name;
    
    public override bool Equals(object? obj)
    {
        if (obj is PredefinedPeriod other)
        {
            return Start == other.Start && End == other.End && Name == other.Name;
        }
        return false;
    }
    
    public override int GetHashCode() => HashCode.Combine(Start, End, Name);
}
