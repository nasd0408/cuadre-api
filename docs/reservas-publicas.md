# Reservas públicas en DuckDNS

La web y la API usan el mismo dominio:

- Página: `https://cuadreapp.duckdns.org/app/#/reservar/{token}`.
- Profesional seleccionado: la misma ruta con `/{empleadoId}` al final.
- Comprobante privado: `/app/#/cita/{solicitudToken}`.
- API: `https://cuadreapp.duckdns.org/api/publico/reservas`.

Los enlaces públicos se activan desde Administración → Perfil público → Reservas sin cuenta. Hay que guardar y publicar el perfil, publicar un local, tener servicios activos y configurar el horario semanal de al menos un profesional. Las empresas existentes tienen las reservas **desactivadas** por defecto.

## Flujo

El visitante elige profesional, un servicio, fecha y uno de los horarios libres. Introduce nombre y teléfono, con correo opcional. La cita se guarda como `Pendiente`, con el precio y la duración del servicio en ese momento, sin movimientos de dinero. Aparece en la agenda del negocio; SignalR avisa al grupo de esa empresa después del commit.

El comprobante muestra los datos actuales de la cita y permite cancelar mientras sea futura y esté pendiente. Sigue funcionando aunque el negocio cierre sus reservas públicas o quite la publicación. Una cita atendida o pasada debe gestionarse con el negocio. Cancelar no borra su historial ni crea movimientos de dinero.

La confirmación de esta versión está en la página. Se conservan teléfono y correo para futuras notificaciones; todavía no se envían SMS, WhatsApp ni correos automáticamente.

## Aislamiento y concurrencia

El token público contiene 256 bits aleatorios y no es un JWT. Solo permite consultar el catálogo publicado y solicitar citas. La empresa se resuelve en el servidor desde el enlace: no se acepta `EmpresaId` ni `ClienteId` del navegador. Después de resolverlo, se abre un `AppDbContext` independiente con el filtro de esa empresa, incluso si el visitante tiene una sesión de otro negocio.

La disponibilidad pública y la administrativa usan `DisponibilidadService`: zona IANA, turnos, ausencias, citas pendientes y duraciones guardadas. La creación vuelve a consultarla después de adquirir `TransaccionAgenda`; no se confía en los horarios mostrados anteriormente. El mismo bloqueo protege cambios de horario, ausencias, reservas internas, reprogramaciones y la configuración/publicación de la reserva. Los conflictos devuelven 409 y el navegador recarga los horarios.

El navegador genera un segundo token criptográfico de 256 bits antes de enviar la solicitud. Lo reutiliza al reintentar los mismos datos y lo conserva en `sessionStorage` para poder comprobar el resultado si se pierde la respuesta. La base guarda su SHA-256 y una huella de la solicitud normalizada. Repetirla devuelve la misma cita; reutilizarla con datos diferentes devuelve 409. Este token da acceso exclusivamente al comprobante y cancelación de esa cita. Las respuestas no se cachean.

Una reserva anónima crea una ficha de cliente propia; un teléfono no verificado no permite modificar ni apropiarse de una ficha existente. Por eso pueden aparecer contactos repetidos cuando una persona hace varias reservas. Consolidar clientes requiere un flujo posterior de verificación o una herramienta administrativa explícita.

Se aplican límites por IP: 60 lecturas/minuto y 10 escrituras/15 minutos, además del límite global. El middleware de suscripción ignora la sesión del visitante en estas rutas; crear citas comprueba la suscripción del negocio del enlace.

## Alcance actual

Cada negocio configura un local para sus reservas públicas. Todos sus profesionales con horario activo se ofrecen en ese local. Aún no existe una asignación de empleados a sucursales ni un calendario independiente por local. Cada reserva pública incluye un servicio; la agenda interna sigue admitiendo varios. El enlace de profesional lo preselecciona y permite elegir otro profesional del mismo negocio.

## Endpoints

| Método y ruta bajo `/api` | Uso |
| --- | --- |
| `GET /empresa/reservas-publicas` | Configuración y profesionales; requiere Admin |
| `PUT /empresa/reservas-publicas` | Activar/desactivar y elegir `sucursalId`; requiere Admin |
| `GET /publico/reservas/{token}` | Perfil mínimo, local, profesionales y servicios |
| `GET /publico/reservas/{token}/disponibilidad` | Query: `empleadoId`, `servicioId`, `fecha=YYYY-MM-DD` |
| `POST /publico/reservas/{token}` | `solicitudToken`, `empleadoId`, `servicioId`, `inicioUtc`, `nombre`, `telefono`, `email` |
| `GET /publico/reservas/cita/{token}` | Consultar comprobante privado |
| `POST /publico/reservas/cita/{token}/cancelar` | Cancelación idempotente |

## Despliegue

Esta entrega no aplica cambios a producción. Preparar el release con el proceso de `deploy/manual/README.md`, respaldar la base y aplicar las migraciones pendientes antes de cambiar la API. La nueva es `20261002112038_ReservasPublicas`. `deploy/reservas-publicas.sql` contiene únicamente el salto desde `DuracionFijaCitas`; el preparador manual genera el SQL completo del release si aún faltan las migraciones anteriores.

Construir el frontend con `EXPO_PUBLIC_API_URL=https://cuadreapp.duckdns.org/api` y, opcionalmente, `EXPO_PUBLIC_WEB_URL=https://cuadreapp.duckdns.org/app/`. Ejecutar `build:web` para ajustar las rutas de assets a `/app/`; el export directo usado en CI no hace ese ajuste. No reemplazar el `.env` de desarrollo con la configuración de producción. Las rutas usan fragmentos `#`, de modo que nginx sigue sirviendo el mismo `index.html`.

Después del despliegue, activar un negocio y comprobar su catálogo y disponibilidad sin sesión. Verificar que reservar/cancelar actualiza su calendario, que otro negocio no recibe los avisos y que apagar la opción impide reservas nuevas sin ocultar los comprobantes existentes. Evitar registrar cuerpos o rutas privadas de estas peticiones en sistemas de analítica.

## Validación

123 pruebas de backend pasan, incluidas ocho con PostgreSQL 18 real y una reserva/cancelación por HTTP con WebSockets reales. La migración se aplicó dos veces a un esquema mínimo anterior de prueba: conservó el perfil y dejó la opción desactivada. Ninguna de estas comprobaciones usa la base de producción.

El siguiente bloque de mensajería puede registrar un evento de reserva en una outbox dentro de esta misma transacción, publicar mediante RabbitMQ y procesar la confirmación en un worker. SignalR sigue actualizando el calendario; los mensajes de contacto necesitarán reintentos y deduplicación separados de la reserva.
