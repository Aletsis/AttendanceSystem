namespace AttendanceSystem.Domain.Enumerations;

public enum ShiftType
{
    Matutino = 0,
    Vespertino = 1,
    Nocturno = 2,
    Mixto = 3,
    // 4 = Continuo, retirado: lo reemplaza Flexible (migración RetireContinuousShiftType).
    Flexible = 5,
    Partido = 6,
    Rotativo = 7
}
