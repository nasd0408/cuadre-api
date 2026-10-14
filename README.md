# Cuadre API

Backend de **Cuadre APP**, un SaaS multi-empresa para barberías y negocios de belleza: agenda, clientes, cobros, nómina de profesionales y reservas públicas. Está en producción y da servicio a la app Android publicada en Google Play.

Cliente móvil/web: [cuadre-app](https://github.com/nasd0408/cuadre-app)

## Stack

- **ASP.NET Core (.NET 10)** con controladores REST y **SignalR** para la agenda en tiempo real
- **Entity Framework Core** + **PostgreSQL**
- **ASP.NET Identity** + **JWT** con refresh tokens, e inicio de sesión con **Google**
- **xUnit** para los tests, incluidos tests de concurrencia contra PostgreSQL real
- **GitHub Actions** para el CI

## Lo interesante

**Multi-tenancy con filtro global.** Todas las entidades de negocio heredan de `BaseEntity` y llevan un `EmpresaId`. `AppDbContext` aplica un *global query filter* con la empresa de la sesión, así que ninguna consulta puede devolver datos de otro negocio aunque el controlador olvide filtrar. Los tests `AislamientoTenantTests` y `AislamientoEscrituraTests` verifican el aislamiento en lectura y en escritura.

**Concurrencia en la agenda.** Dos peticiones simultáneas podían ver el mismo hueco libre y reservarlo las dos. `TransaccionAgenda` abre una transacción y toma un `pg_advisory_xact_lock` por empresa antes de validar y escribir. Reservar, reprogramar, crear ausencias y cambiar horarios comparten ese bloqueo. Detalle en [docs/concurrencia-agenda.md](docs/concurrencia-agenda.md).

**Agenda en tiempo real.** Un filtro de acción (`AgendaNotificationFilter`) avisa por SignalR al grupo de la empresa después del commit, para que todos los dispositivos del negocio vean los cambios al momento. Ver [docs/agenda-tiempo-real.md](docs/agenda-tiempo-real.md).

**Suscripciones.** `SubscriptionCheckMiddleware` restringe el acceso según el estado de la cuenta (Activo / Moroso / Suspendido), y `SubscriptionBackgroundService` revisa los vencimientos cada hora y envía avisos por correo. Los pagos son manuales (Pago Móvil, Binance): el negocio registra la referencia y un SuperAdmin la aprueba.

**Reservas públicas.** Cada negocio puede publicar un perfil y un enlace de reserva sin cuenta. Esos endpoints anónimos tienen rate limiting por IP con políticas separadas para lectura, reserva, autenticación y envío de códigos. Ver [docs/reservas-publicas.md](docs/reservas-publicas.md).

**Sincronización offline.** La app puede crear citas sin conexión y luego subirlas en lote a `/api/sync/citas`.

## Estructura

```
Controllers/   Endpoints REST (citas, clientes, empleados, horarios, reservas públicas, suscripción…)
Services/      Lógica de dominio: disponibilidad, horarios, planes, push, correos, tareas en segundo plano
Middleware/    Manejador global de excepciones y control de suscripción
Hubs/          AgendaHub (SignalR)
Models/        Entidades EF Core
Data/          AppDbContext y filtros multi-tenant
Migrations/    Migraciones EF Core
BarberShop.Tests/  Tests xUnit
deploy/        Scripts de despliegue y SQL de migraciones manuales
docs/          Notas de diseño de cada funcionalidad
```

> El código interno usa el nombre histórico `BarberShop` (namespaces, `.csproj`, servicio en el servidor).

## Correr en local

Requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/download) y PostgreSQL.

1. Copia la plantilla de configuración y completa los valores:
   ```bash
   cp appsettings.Template.json appsettings.Development.json
   ```
   Como mínimo necesitas la cadena de conexión y `JWT:Secret`, que debe tener al menos 64 caracteres aleatorios. Este archivo está en `.gitignore` y no se sube al repo.
2. Crea la base de datos:
   ```bash
   dotnet tool install --global dotnet-ef
   dotnet ef database update
   ```
3. Arranca la API:
   ```bash
   dotnet run
   ```
   Escucha en `http://localhost:5002`. `GET /health` comprueba la API y la conexión a la base de datos.

## Tests

```bash
dotnet test BarberShop.Tests/BarberShop.Tests.csproj
```

Los tests de concurrencia (`AgendaPostgresTests`) necesitan un PostgreSQL real y se saltan si no existe la variable `CUADRE_TEST_POSTGRES`:

```bash
CUADRE_TEST_POSTGRES="Host=127.0.0.1;Port=5432;Database=cuadre_test_template;Username=postgres;Password=..." dotnet test BarberShop.Tests/BarberShop.Tests.csproj
```

El CI ejecuta ambos grupos en cada push a `main` y en cada pull request. El proyecto compila con `TreatWarningsAsErrors`.

## Despliegue

Corre en un VPS Ubuntu, con la API como servicio systemd detrás de nginx. Los scripts y el paso a paso están en [deploy/](deploy/). Los secretos de producción viven solo en el servidor.
