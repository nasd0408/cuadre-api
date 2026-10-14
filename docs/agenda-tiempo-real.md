# Agenda en tiempo real y duración fija

## Duración de las reservas

Cada CitaServicio guarda DuracionMinutos por unidad al crear la cita, también en
la sincronización offline. La ocupación es la suma de duración por cantidad.
Cambiar la duración de un servicio del catálogo afecta a las nuevas reservas.
Reprogramar conserva la duración guardada de los servicios que siguen en la cita;
añadir un servicio toma su duración actual. Cambiar la cantidad cambia la duración
total. La API devuelve la duración guardada para dibujar el calendario.

Disponibilidad, solapamientos y conflictos con ausencias utilizan ese mismo dato.
Para filas antiguas con duración nula existe un fallback al catálogo, que permite
leer datos escritos por una versión anterior durante una transición de despliegue.

La migración `20261002100527_DuracionFijaCitas` añade la columna y rellena las
reservas existentes con la duración actual del catálogo. No se puede recuperar
su duración original si el servicio ya había cambiado antes de esta migración.
SQL para revisión: `deploy/duracion-fija-citas.sql`; parte de AusenciasProfesionales.
No se ha aplicado a la base de datos de la aplicación.

## SignalR

El hub `/api/hubs/agenda` exige JWT y rol Admin, Barbero o SuperAdmin. Además,
comprueba en base de datos que el usuario pertenezca a la empresa de su token y
que esa empresa no esté suspendida o bloqueada. Asigna el grupo
`agenda:empresa:{EmpresaId}` en el servidor. No ofrece métodos para elegir grupos.
SuperAdmin también necesita una empresa válida en la sesión.

El filtro AgendaNotificationFilter avisa al terminar una escritura exitosa de
Citas, Horarios, Ausencias, Sync, Empleados o Servicios. La acción debe haber
guardado y confirmado su transacción antes de devolver el resultado. Las respuestas
de error no emiten avisos. El evento AgendaChanged contiene fechaUtc, sin clientes,
servicios, motivos de ausencia ni información de reservas.

Es una invalidación: el frontend consulta otra vez la API con sus filtros y
permisos. La lista, el rango mostrado del calendario y el selector de huecos se
actualizan sin refrescar manualmente. Los avisos consecutivos se agrupan durante
200 ms; el calendario descarta respuestas de consultas anteriores.

Cada consumidor mantiene y limpia su propia conexión. Al conectar, reconectar o
volver al primer plano recarga los datos para recuperar cambios perdidos.
SignalR reintenta una desconexión con pausas de 0, 2, 10 y 30 segundos; el arranque
fallido o el agotamiento de esos intentos vuelve a probar cada 15 segundos.
Al desmontar la pantalla o cambiar de usuario/empresa se detienen conexión y timers.

accessTokenFactory consulta `/ajustes/perfil` mediante el cliente HTTP existente:
un 401 activa su renovación de JWT con una única solicitud de refresh en vuelo.
Después lee el token del almacenamiento y comprueba que sigue siendo la misma
sesión y empresa. Esto añade una lectura de perfil a las solicitudes que requieren
token para la conexión. El servidor cierra la conexión al caducar la autenticación.
El token en query se acepta solo en el prefijo del hub, necesario para transportes
del navegador; no autentica otras rutas de la API.

Referencia: [autenticación de SignalR](https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz).

## Despliegue y límites

1. Aplicar las migraciones de perfiles, horarios, ausencias y duración en orden,
   con copia y revisión previas según el procedimiento del entorno.
2. Publicar el backend y el frontend con EXPO_PUBLIC_API_URL apuntando a la base
   de la API, por ejemplo `https://dominio/api`.
3. Comprobar que el proxy admite WebSockets en `/api/hubs/agenda`, incluyendo
   negotiate y sus rutas hijas. Nginx necesita reenviar Upgrade/Connection y
   usar HTTP/1.1; ajustar su tiempo de espera para conexiones persistentes.
4. Autorizar el origen web en CORS. El cliente usa bearer y withCredentials=false.
   Usar HTTPS y evitar que los logs del proxy registren access_token de la query.

Esta entrega admite una instancia de API. Si se distribuyen peticiones entre
varias instancias, los grupos necesitan infraestructura compartida (Azure SignalR
o un backplane) y configuración de afinidad según los transportes utilizados.
El bloqueo PostgreSQL sigue protegiendo las reservas entre instancias, pero por sí
solo no distribuye las notificaciones SignalR.

Los avisos no son durables: si la API cae entre commit y envío, se recupera el
estado al reconectar o volver a consultar. Un fallo de envío se registra y no
revierte una operación ya confirmada. El envío espera como máximo tres segundos.
La pertenencia y suscripción se verifican al conectar; una revocación posterior
no desconecta inmediatamente un socket existente. El canal solo transmite avisos
de recarga y las lecturas HTTP mantienen sus controles de acceso.

RabbitMQ y outbox serán el siguiente paso para confirmaciones y recordatorios
durables. Kafka queda para analítica de eventos cuando exista ese flujo.

## Validación

- 109 pruebas backend pasan, incluidas cinco con PostgreSQL 18 real.
- WebSockets reales contra Kestrel verifican aislamiento de empresas, rechazo
  de empresa incorrecta, autenticación limitada a la ruta del hub y avisos solo
  tras escrituras exitosas.
- Una prueba de duración cambia el catálogo de 30 a 90 minutos, comprueba que
  sigue libre el siguiente hueco y reprograma conservando los 30 minutos originales.
- El SQL de migración se ejecutó sobre tablas mínimas de una base desechable
  PostgreSQL: el backfill queda en 30 minutos tras cambiar el catálogo a 90.
  Esto no sustituye la comprobación del conjunto de migraciones sobre una copia
  de la base de aplicación.
- 95 pruebas frontend pasan; las nuevas cubren recarga, reconexión, primer plano,
  limpieza, reintentos y rechazo de tokens de otra sesión/empresa. TypeScript y
  exportación web pasan. No se han probado dispositivos Android/iOS en esta entrega.
