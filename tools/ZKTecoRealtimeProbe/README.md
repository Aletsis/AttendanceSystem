# ZKTecoRealtimeProbe — prueba técnica de checadas en tiempo real por SDK (fase 1)

Ejecutable independiente: **no** forma parte de `AttendanceSystem.sln`, no usa la base de datos y no envía nada al
sistema. Solo se conecta a uno o más relojes, se suscribe a sus eventos y registra lo que pasa.

Responde a estas preguntas antes de rediseñar el servicio ZKTeco:

1. ¿Los relojes reales emiten eventos de checada por SDK? ¿Cuál variante (`OnAttTransactionEx`, `OnAttTransactionEx_New`
   u `OnAttTransaction`)?
2. ¿Con qué latencia llegan?
3. ¿Cuánto tarda en detectarse una conexión caída (cable, reinicio del reloj, VPN)? ¿Llega `OnDisConnected` o solo
   lo detecta el latido?
4. ¿Se recuperan con `ReadTimeGLogData` las checadas hechas mientras no había conexión?
5. ¿Acepta el reloj una segunda conexión SDK mientras esta está abierta?
6. ¿Cuánto tiempo queda bloqueado el reloj en una lectura completa (`ReadAllGLogData`)? Eso decide si sondear cada
   pocos minutos es una alternativa viable.

## Cómo funciona

Cada reloj tiene su propio hilo **STA** con bucle de mensajes de Windows (`Application.Run`). El objeto
`zkemkeeper.CZKEMClass` se crea, se usa y recibe sus eventos solo en ese hilo.

> **Corregido por la prueba en un reloj real (2026-11-03):** el STA no es necesario. En un hilo MTA, con sólo
> `RegEvent`, cada checada llega una vez y en menos de 1 s por un hilo interno del SDK; en STA con sondeo llegan
> duplicadas y se repiten checadas de sesiones anteriores. `OnDisConnected` no llega al reiniciar el reloj. Ver
> [`AttendanceSystem.ZKTeco.RealtimeProbe`](../AttendanceSystem.ZKTeco.RealtimeProbe/README.md).

- `Connect_Net` → `RegEvent(máquina, máscara)` → espera eventos.
- Latido `GetDeviceTime` cada 30 s: mide el desfase del reloj y detecta caídas (2 fallos seguidos).
- Al caer: `Disconnect` y reintentos a los 5, 10, 30 y 60 s (luego cada 60 s).
- Al reconectar: lee con `ReadTimeGLogData` el hueco desde el último latido correcto (−1 min).
- Las lecturas deshabilitan el reloj (`EnableDevice(false)`) igual que la descarga de producción y registran cuánto
  tiempo quedó bloqueado.

## Requisitos

- Windows. El proceso es de **32 bits** (x86).
- `zkemkeeper.dll` registrado con el `regsvr32` de 32 bits. En el servidor donde ya corre el servicio ZKTeco ya lo
  está. En otro equipo, como administrador, desde la carpeta publicada:
  ```
  C:\Windows\SysWOW64\regsvr32.exe zkemkeeper.dll
  ```
- Acceso de red al reloj (TCP 4370 por omisión).

## Compilar

Desde la raíz del repositorio (funciona también en Linux gracias a `EnableWindowsTargeting`):

```
dotnet publish tools/ZKTecoRealtimeProbe -c Release -r win-x86 --self-contained true -o publish/ZKTecoRealtimeProbe
```

`--self-contained` evita instalar .NET en el equipo de prueba. Copie la carpeta completa: incluye las DLL del SDK.

## Uso

```
ZKTecoRealtimeProbe.exe --device 192.168.1.201
ZKTecoRealtimeProbe.exe --device 192.168.1.201 --device 192.168.1.202:4370 --comm-key 1234
```

| Opción | Por omisión | Descripción |
|---|---|---|
| `--device <ip[:puerto]>` | — | Reloj a vigilar; repetible (un hilo por reloj). |
| `--comm-key <n>` | — | Contraseña de comunicación del reloj. |
| `--machine <n>` | 1 | Número de máquina del SDK. |
| `--heartbeat <seg>` | 30 | Intervalo del latido. |
| `--heartbeat-failures <n>` | 2 | Fallos seguidos para dar por caída la conexión. |
| `--event-mask <n>` | 65535 | Máscara de `RegEvent` (1 = solo checadas). |
| `--no-catch-up` | — | No leer el hueco al reconectar. |
| `--window <min>` | 60 | Ventana del comando `t`. |
| `--verbose` | — | Registra cada latido. |
| `--log-dir <ruta>` | `probe-logs` | Carpeta de bitácoras. |

Teclas mientras corre: `s` estado · `t` leer la última ventana · `n` `ReadNewGLogData` · `a` `ReadAllGLogData`
(solo cuenta y tiempo) · `h` ayuda · `q` o Ctrl+C salir.

Salida en `probe-logs/`:
- `probe-<fecha>.log`: todo lo que se ve en consola.
- `probe-punches-<fecha>.csv`: una fila por checada (evento o lectura), con atraso vs PC, desfase del reloj y
  latencia estimada (`atraso + desfase`).

## Precauciones

- **Mientras el probe está conectado, el servicio ZKTeco o ZKAccess pueden no poder conectarse a ese reloj** (o
  desconectar al probe). Úselo con un reloj de pruebas o fuera del horario de la descarga automática.
- `n` (`ReadNewGLogData`) puede mover en el reloj una marca interna de "registros nuevos". Producción no la usa, pero
  pruébelo primero en un reloj de pruebas.
- `a` deshabilita el reloj durante toda la lectura completa: nadie puede checar mientras tanto.

## Plan de pruebas

Anote los resultados por modelo y firmware (se ven con `GetDeviceInfo` o en el menú del reloj).

| # | Prueba | Qué observar |
|---|---|---|
| 1 | Arrancar y checar 3–5 veces con huella, tarjeta y contraseña. | Llega una línea `CHK` por checada. Variante del evento. Latencia estimada (esperado < 2 s). Si no llega nada, revise si llegan `OnVerify`/`OnFinger` (eventos activos pero sin checada) y pruebe `--event-mask 1`. |
| 2 | Checar con una huella no registrada. | ¿Llega un evento con `invalida = 1` o no llega nada? |
| 3 | Desconectar el cable de red del reloj 2–3 min, checar durante el corte, reconectar. | Tiempo hasta `Conexión caída`. ¿Llegó `OnDisConnected`? ¿Reconecta solo? ¿La recuperación lista las checadas del corte? |
| 4 | Reiniciar el reloj. | Igual que la 3. |
| 5 | Con el probe conectado, lanzar una descarga desde el sistema (o abrir ZKAccess). | ¿La segunda conexión falla, funciona, o tumba al probe? Define si la fase 2 debe ser obligatoriamente "una conexión por reloj". |
| 6 | Pulsar `a` con un reloj con muchos registros. | Milisegundos de bloqueo. Más de unos segundos descarta el sondeo frecuente con lectura completa. |
| 7 | Pulsar `t` y `n`. | ¿Soporta el firmware `ReadTimeGLogData` y `ReadNewGLogData`? (código −4 o similar = no). |
| 8 | Si hay sucursal por VPN, repetir 1 y 3 contra un reloj remoto. | Caídas espontáneas y latencia. |
| 9 | Dejarlo 24 h con 2+ relojes. | Caídas espontáneas (`s` al final), checadas perdidas: compare el CSV con una descarga normal del día. |

Con estos resultados se decide la fase 2: un hilo por reloj en el servicio ZKTeco con todas las operaciones encoladas en él.
