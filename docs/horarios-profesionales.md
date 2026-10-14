# Horarios semanales y disponibilidad

En la app: Más → Empleados → detalle del profesional → Horario y disponibilidad.

Cada profesional tiene una zona horaria IANA (por ejemplo Atlantic/Canary), un
interruptor de activación y hasta 28 turnos semanales. Los días sin turnos son
cerrados cuando el horario está activo. Dos turnos separados representan un
descanso. Los turnos no pueden solaparse ni cruzar medianoche; dividir un turno
nocturno entre dos días. Se admite 24:00 únicamente como fin de un turno.

Al desactivar el horario se conservan turnos y zona, y dejan de limitar la agenda.
Los empleados existentes empiezan sin esta restricción. Cambiar un horario no
cancela ni mueve citas existentes. La nueva regla se aplica al agendar y al editar
citas pendientes futuras; los cobros y registros históricos mantienen su flujo.

## API administrativa

- GET / PUT `/api/empleados/{id}/horario`: configuración de la empresa autenticada,
  únicamente para Admin. Campos: activo, zonaHoraria, tramos. Cada tramo contiene
  diaSemana (0 domingo a 6 sábado), inicioMinuto y finMinuto desde medianoche.
- GET `/api/empleados/{id}/horario/disponibilidad?fecha=2030-01-07&duracionMinutos=30`:
  devuelve horarioConfigurado, zonaHoraria e iniciosUtc. La fecha se interpreta
  en la zona del profesional. Duración permitida de 1 a 600 minutos.

Se ofrecen inicios cada 15 minutos a partir del inicio de cada turno, se descartan
instantes pasados y se descuentan las citas pendientes usando duración y cantidad
de sus servicios. Se permite empezar justo al terminar otra cita. Las horas
locales ambiguas o inexistentes por cambio de hora no se ofrecen. Los turnos
con un extremo ambiguo o inexistente tampoco se ofrecen ese día.

La interfaz presenta las horas en la zona del profesional. La API de citas sigue
recibiendo instantes UTC. Una consulta de huecos no los bloquea ni los reserva.

## Migración y validación

HorariosProfesionales añade ZonaHorariaAgenda y HorarioAgendaActivo a Empleados,
y la tabla TramosHorario con clave externa y borrado en cascada al borrar el
empleado. Se requiere aplicar las migraciones anteriores de perfiles públicos.
La migración no ha sido aplicada a una base de datos en esta entrega.

Las pruebas verifican descansos, límites, horario desactivado, aislamiento entre
empresas, zona horaria de Canarias en verano e invierno, descuento de citas y
rechazo al agendar o reprogramar fuera de horario. Los controladores se prueban
con EF InMemory, no con PostgreSQL real.

## Pendientes antes de reservas públicas

- Asociar empleados y agenda a sucursales; actualmente el horario pertenece al profesional.
- Excepciones de horarios parciales por fecha (las ausencias de días completos ya están implementadas).
- Extender las pruebas PostgreSQL a nuevos flujos de escritura cuando se incorporen.

## Ausencias y selector de huecos

En el formulario de agendar, después de elegir profesional y servicios, se ofrece
un selector de huecos para una fecha local del profesional. La duración se calcula
sumando los servicios seleccionados. Al tocar un hueco se conserva su instante UTC
al enviar la cita. El resumen de fecha y hora manual sigue mostrando la zona del
dispositivo, indicada en pantalla. Cambiar fecha, profesional o duración cancela
la consulta anterior. Sin horario activo, se mantiene la selección manual.

La consulta de disponibilidad admite Admin, Barbero y SuperAdmin con empresa en
la sesión. La edición de horarios y ausencias sigue reservada a Admin. La API
de disponibilidad no devuelve motivos de ausencias ni información de clientes.

En Horario y disponibilidad se pueden registrar y quitar ausencias por días
completos, con fechas inclusivas y motivo interno opcional. Se conserva la zona
horaria con la que se registró cada ausencia, así como sus extremos UTC; cambiar
la zona del horario semanal no desplaza una ausencia existente.

- GET `/api/empleados/{id}/ausencias`: ausencias vigentes y próximas.
- POST a la misma ruta: `{ desde: "2030-01-07", hasta: "2030-01-09", motivo: "Vacaciones" }`.
- DELETE `/api/empleados/{id}/ausencias/{ausenciaId}`: quita el bloqueo.

El rango máximo es de 366 días. Antes de registrar una ausencia debe haberse
guardado la zona horaria del profesional. Los rangos terminados, superpuestos o
con límites locales ambiguos se rechazan. Si hay citas pendientes que intersectan
el período, incluso empezadas el día anterior, se responde 409 y deben
reprogramarse antes. No se cancelan citas automáticamente.

Las ausencias bloquean la agenda incluso si se desactiva el horario semanal.
Agendar y reprogramar una cita pendiente futura comprueban también las ausencias.
La migración AusenciasProfesionales añade una tabla e índice por empresa,
profesional y extremos UTC, con borrado en cascada al borrar el profesional.
No se ha aplicado a ninguna base de datos.

La comprobación y escritura de reservas, reprogramaciones, ausencias y horarios
ya comparten un bloqueo transaccional PostgreSQL por empresa. Hay cinco pruebas
reales de concurrencia; ver [concurrencia de agenda](concurrencia-agenda.md).

La duración se guarda por cita y SignalR actualiza calendario, lista y huecos
disponibles. Ver [duración fija y tiempo real](agenda-tiempo-real.md) para el
despliegue y los límites de esta entrega.
