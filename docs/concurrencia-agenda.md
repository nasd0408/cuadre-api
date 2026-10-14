# Concurrencia de agenda

Antes, agendar comprobaba disponibilidad antes de abrir la transacción. Dos
peticiones podían leer un hueco libre y guardarse ambas. Ahora TransaccionAgenda
abre una transacción ReadCommitted y adquiere `pg_advisory_xact_lock` antes de
leer los datos usados para validar y escribir.

La clave tiene un espacio reservado de aplicación y la empresa de la sesión.
Reservar, reprogramar, crear/quitar ausencias y cambiar horarios usan el mismo
bloqueo, conservado hasta commit o rollback. Las empresas distintas no se bloquean
entre sí. Esta primera versión serializa escrituras de agenda dentro de una
empresa, incluso de empleados distintos; limita paralelismo a cambio de evitar
órdenes complejos de bloqueo cuando una cita cambia de profesional.

Las lecturas de disponibilidad no bloquean ni reservan huecos. La escritura
vuelve a validar bajo bloqueo. Si una operación espera más de 10 segundos,
PostgreSQL cancela la espera y la API devuelve 409 con un mensaje para reintentar.
No hay reintentos automáticos de escrituras.

Documentación de referencia:
[bloqueos consultivos de PostgreSQL](https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS).

## Pruebas reales

AgendaPostgresTests usa conexiones y DbContexts independientes. Antes de soltar
un bloqueo de control, consulta pg_locks para comprobar que las peticiones están
realmente esperando. No basa la carrera en un retraso arbitrario.

Casos: dos reservas para un hueco; reserva frente a ausencia; reprogramación
frente a reserva; rollback que revierte datos y libera el bloqueo; independencia
entre empresas. Estas pruebas se ejecutaron localmente contra PostgreSQL 18.
El esquema se crea con EnsureCreated: no son pruebas de aplicación de migraciones.

Para repetirlas, disponer de un servidor PostgreSQL local DE PRUEBAS y un usuario
con permiso para crear bases. Configurar CUADRE_TEST_POSTGRES con una conexión
local cuyo Database comience por `cuadre_test_` y ejecutar:

```powershell
dotnet test BarberShop.Tests --filter FullyQualifiedName~AgendaPostgresTests
```

Cada prueba crea una base `cuadre_test_<guid>`, la inicializa y la elimina al
terminar. Nunca reutiliza el esquema indicado en la conexión. Se rechazan hosts
que no sean localhost/127.0.0.1/::1 y nombres base sin ese prefijo. Sin la variable,
xUnit marca explícitamente estas pruebas como omitidas. CI incorpora un job con
PostgreSQL 18 desechable para que no se omitan en las revisiones.

## Alcance

Esta protección no requiere cambios de esquema. Siguen pendientes de despliegue
las migraciones de perfiles, horarios y ausencias de las entregas anteriores.
Todo nuevo flujo que pueda crear/modificar ocupación debe usar el mismo bloqueo.
El bloqueo es cooperativo: SQL manual que lo ignore no queda protegido.

Las duraciones ya quedan guardadas por cita y las pantallas se actualizan mediante
SignalR; ver [agenda en tiempo real](agenda-tiempo-real.md). La migración de duración
también está pendiente de aplicar a la base de aplicación.
La protección añadida cubre las operaciones listadas, no una restricción de
exclusión global en la base. El cambio de horarios conserva citas existentes por
diseño. No habilitar reservas públicas hasta cerrar esas reglas de producto.
