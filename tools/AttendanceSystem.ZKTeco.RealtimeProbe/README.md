# Prueba de tiempo real por SDK

Consola x86 que se conecta a un reloj ZKTeco con zkemkeeper y escucha sus marcajes (`RegEvent` +
`ReadRTLog`/`GetRTLog`, evento `OnAttTransactionEx`), para decidir el diseño del listener del servicio ZKTeco. Usa COM
sin registro (`app.manifest`): no necesita `regsvr32` ni el servicio instalado.

```bash
dotnet build -c Release tools/AttendanceSystem.ZKTeco.RealtimeProbe
```

El ejecutable queda en `bin/Release/net10.0-windows/`. Cada corrida imprime los marcajes con el hilo y el apartamento
en que llegaron, y al final un resumen de lo que devolvieron `ReadRTLog`/`GetRTLog`.

| Pregunta | Corrida |
|---|---|
| ¿Llegan los eventos sondeando desde un hilo del pool (MTA), como corre hoy el cliente? | `listen --ip <ip>` |
| ¿Y desde un hilo STA? | `listen --ip <ip> --sta` |
| ¿Llegan sin sondear, sólo con el ciclo de mensajes? | `listen --ip <ip> --sta --no-poll` y `listen --ip <ip> --no-poll` |
| ¿Se intercalan otras operaciones en la misma conexión? | `listen --ip <ip> --op-every 10` |
| ¿Y llamando desde otro hilo? | `listen --ip <ip> --sta --op-every 10 --op-cross-thread` (y sin `--sta`) |
| ¿Acepta el reloj una segunda conexión mientras la primera escucha, y sobrevive la primera? | `second --ip <ip> --second-at 20` |

Opciones: `--port 4370`, `--seconds 120`, `--poll-ms 1000`. Marque en el reloj durante cada corrida.

## Resultados (2026-11-03, reloj 192.168.0.233, ~95 000 marcajes en memoria)

| Corrida | Resultado |
|---|---|
| MTA sin sondeo | Cada marcaje llega **una vez**, a los ~0.1–1 s, en un hilo interno del SDK. Basta con `RegEvent`. |
| MTA con sondeo | Cada marcaje llega **dos veces**: por el hilo interno y por `GetRTLog`. |
| STA con sondeo | Cada marcaje llega dos veces y, al conectar, **repite marcajes de sesiones anteriores** (uno de ellos 5 veces). |
| MTA + lectura de hora cada 10 s desde otro hilo | Las operaciones se intercalan con la escucha sin problemas. |
| Segunda conexión mientras A escucha | El reloj la acepta (686 ms); B lee hora y conteo, se desconecta y A sigue recibiendo marcajes. |

La hora del evento es la hora civil que marca el reloj (iba ~2 min 40 s atrasado), igual que en la descarga.

**Incidente:** en la primera corrida (MTA con sondeo, antes de reiniciar el reloj) `ReadRTLog` se bloqueó ~52 s y
devolvió `false` sin error; después el reloj rechazó toda conexión SDK (`Connect_Net` → -2 a los ~25 s) hasta
apagarlo y encenderlo. No se reprodujo después del reinicio; la causa no está confirmada.

**Reinicio del reloj mientras escucha** (`watch --watch-every 5`):

- `OnDisConnected` **no llega**; la escucha simplemente deja de recibir marcajes, sin error.
- La lectura de hora de vigilancia se queda bloqueada **~46 s** y devuelve `false` con error 0: así se detecta la caída.
- Al volver el reloj, `Connect_Net` + `RegEvent` funcionan al primer intento (7 s después de detectar la caída) y los
  marcajes vuelven a llegar.
- Lo marcado mientras la escucha estaba caída no se entrega por tiempo real: hace falta una descarga de respaldo.
