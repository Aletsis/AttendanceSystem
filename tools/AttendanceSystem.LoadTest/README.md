# Banco de carga de asistencia

Reproduce el escenario representativo de F00-08 (250 empleados, 3 sucursales, 30 días, ~3.5 marcajes por
empleado-día, 5 % de ajustes) sobre una base PostgreSQL propia y mide, por escenario: duración total, p50/p95 por
empleado-día, consultas SQL, memoria pico y espera de bloqueo. Usa la composición real de dependencias
(`AddApplicationServices` + `AddInfrastructureServices`).

```bash
dotnet run -c Release --project tools/AttendanceSystem.LoadTest -- [--employees 250] [--days 30] [--seed 42] [--keep]
```

Conexión de administración: variable `TEST_POSTGRES_CONNECTION` (misma que la suite relacional) o
`Host=localhost;Port=5432;Database=postgres;Username=attendanceuser;Password=AttendancePass`. Crea y elimina la base
`attendance_load_<marca>` (con `--keep` la conserva). Imprime una tabla Markdown con los resultados.
