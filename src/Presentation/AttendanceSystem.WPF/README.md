# Cliente de escritorio (WPF)

Cliente Windows del sistema de asistencia. Usa **el mismo backend que la versión web**: las capas `Application` e
`Infrastructure`, los mismos comandos y consultas de MediatR y la misma base PostgreSQL. No tiene servidor propio ni
ejecuta tareas programadas (descargas, respaldos y reportes automáticos los hace el sitio web); nunca aplica migraciones.

## Configuración

`appsettings.json` junto al ejecutable (se lee de la carpeta del programa, no de la carpeta de trabajo). Partir de
`appsettings.example.json`:

| Clave | Uso |
|---|---|
| `ConnectionStrings:AttendanceDb` | Base de datos compartida con el sitio web. |
| `BackendUrl` | URL del sitio web; al guardar la configuración se le pide recargar las tareas programadas. |
| `ApiKey` | La API key del sitio web para esa llamada. |
| `Maintenance:PollIntervalSeconds` | Cada cuánto se consulta si hay una restauración en curso (aviso en la ventana). |
| `Serilog` | Registro de la aplicación (errores no controlados incluidos). |

## Sesión, navegación y permisos

- Se arranca en el inicio de sesión. Las cuentas son las de la versión web (misma política de contraseñas y de
  bloqueo por intentos fallidos).
- Menú lateral con las pantallas que el usuario puede abrir; la navegación también valida el permiso de cada pantalla
  (`Services/PagePermissions.cs`, el mismo permiso que la página web equivalente).
- Botones y campos siguen la matriz de permisos del caso 13; los comandos además los valida el backend.
- Las pantallas no se conservan al salir de ellas (`ViewModelBase.KeepAlive = false`): al volver se recargan y al
  cerrar sesión no queda nada de la sesión anterior (pantallas, historial ni contraseña escrita).
- "Cambiar contraseña" en el menú, con el mismo comando que la web.
- Durante una restauración (o tras una fallida) se muestra un aviso arriba y los cambios se rechazan.
- Tras restaurar un respaldo desde aquí se cierra la sesión.

## Pantallas que igualan a la web

- **Inicio**: métricas del día (presentes, retardos, faltas, puntualidad), asistencia de los últimos 7 días, salud de
  los relojes y pendientes de RH (salidas sin clasificar, extras por autorizar, horarios por corregir, checadas que no
  cuentan). Cada bloque sólo para quien puede ver esos datos.
- **Asistencia**: el reporte de asistencia de la web; RH clasifica cada salida temporal y autoriza las extras del día
  (completas o en bloques de 30 min).
- **Bitácora de marcajes**: cada checada del día y cómo se usó; RH reasigna marcajes, cambia el horario del día y
  regresa decisiones al roster (con motivo, `Views/Common/ReasonDialog`).
- **Empleado**: pestañas, varios días de descanso, fecha de vigencia de los cambios y esquema rotativo
  (`RotationSchemeModel`, compartido con la web en Application).
- **Configuración**: además de lo general, comida, toque doble, tareas automáticas, avisos y SMTP. La nube y la
  cuarentena se editan sólo en la web y se conservan al guardar.

## Estructura

- `Themes/Theme.xaml`: paleta y estilos compartidos (tarjetas, botones, pestañas, campos, fichas de días);
  `App.xaml`: estilos basados en MahApps (tablas y campos de formulario).
- `Views/ShellWindow.xaml` + `ViewModels/ShellViewModel.cs`: menú, usuario, avisos y mantenimiento.
- `Services/`: navegación, sesión, mensajes (los guardados correctos se muestran como avisos breves), resumen de
  importaciones.
- `Converters/EnumDisplayConverter.cs`: textos en español de los valores de enumeración.

## Compilar

Se compila en Windows con el SDK de .NET 10. En Linux se puede comprobar que compila (no ejecutar) con:

```
dotnet build src/Presentation/AttendanceSystem.WPF/AttendanceSystem.WPF.csproj -p:EnableWindowsTargeting=true
```
