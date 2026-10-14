# Administración y perfiles públicos: primera entrega

## Situación encontrada

Empresa agrupa usuarios y empleados. Los servicios ya tienen precio y duración.
La agenda comprueba solapamientos, pero eso no demuestra protección frente a
dos reservas concurrentes: requiere una prueba real contra PostgreSQL y una
estrategia transaccional antes de abrir reservas a clientes.

## Implementado

PerfilPublicoEmpresa es independiente de los datos internos de Empresa. No se
copia automáticamente ningún teléfono ni dirección. Cada empresa tiene como
máximo un perfil. Las sucursales pertenecen a una empresa y contienen dirección
pública y coordenadas opcionales. Ambas coordenadas deben indicarse juntas.
La publicación del perfil y de cada sucursal se controla por separado; el valor
inicial es privado. Despublicar el perfil oculta también sus sucursales.

| Método | Ruta | Acceso |
| --- | --- | --- |
| GET / PUT | `/api/empresa/perfil-publico` | Admin de la empresa de la sesión |
| GET / POST | `/api/empresa/sucursales` | Admin de la empresa de la sesión |
| PUT | `/api/empresa/sucursales/{id}` | Admin propietario de la sucursal |
| GET | `/api/publico/empresas/{empresaId}` | Anónimo, solo publicado |

Ejemplo de PUT del perfil:

```json
{
  "nombre": "Estudio Aurora",
  "descripcion": "Peluquería y estética",
  "categoria": "Belleza",
  "telefonoPublico": "+34600111222",
  "instagram": "aurora",
  "facebook": "aurora.estudio",
  "tikTok": "aurora",
  "publicado": false
}
```

Ejemplo de POST de sucursal:

```json
{
  "nombre": "Local principal",
  "direccionPublica": "Dirección que el negocio desea mostrar",
  "latitud": null,
  "longitud": null,
  "publicada": false
}
```

GET del perfil devuelve 404 hasta que se cree. PUT reemplaza todos sus campos.
La API pública devuelve 404 tanto para perfiles inexistentes como privados.
Los usuarios de redes aceptan un @ inicial, eliminado al guardar. No se recopilan
datos de las redes ni se realizan integraciones con ellas.

## Base de datos y validación

La migración PerfilesPublicosYSucursales añade dos tablas, claves externas con
borrado en cascada e índice único por empresa para el perfil. No modifica las
citas existentes ni publica datos de empresas existentes.

AppDbContextFactory permite generar migraciones sin iniciar la API ni sus servicios.
Para aplicar migraciones con `dotnet ef database update`, proporcionar explícitamente
la conexión del entorno mediante `--connection`. La conexión por defecto de la
factoría es deliberadamente inutilizable. No se ha aplicado la migración a ninguna BD.

Las pruebas usan EF InMemory: cubren publicación, despublicación, aislamiento de
empresas, ausencia de copia de contacto privado y validación de coordenadas.
No sustituyen una prueba de migración y restricciones contra PostgreSQL.

## Próximas entregas

1. Verificar en el entorno de pruebas el recorrido completo entre administración
   y ficha pública con la migración aplicada.
2. Vincular profesionales, servicios y citas con sucursales, con migración de los
   negocios actuales a un local inicial. Definir zona horaria por sucursal.
3. Ampliar excepciones parciales: horarios, descansos, ausencias de días completos
   y reservas concurrentes verificadas en PostgreSQL ya están implementados.
4. Validar SignalR en el entorno desplegado: hub, aislamiento por empresa y
   reconexión con recarga desde la API ya están implementados.
5. RabbitMQ: confirmaciones y recordatorios con outbox, reintentos e idempotencia.
6. Kafka: analítica reproducible de eventos cuando exista ese flujo.

La administración del frontend permite crear y editar el perfil y los locales,
publicarlos o despublicarlos, y guardar o quitar sus coordenadas. Requiere aplicar
la migración y desplegar los endpoints antes de usarla. Incluye estados de carga,
reintento, validación y aviso de cambios pendientes al navegar atrás.

La ficha pública está disponible en `/app/#/empresa/{id}` sin iniciar sesión.
Consume la API con un cliente HTTP independiente de los tokens e interceptores
privados. Incluye teléfono, redes y locales publicados, enlaces al mapa cuando
hay GPS, estado no disponible para 404 y reintento para fallos de conexión.
Administración muestra el enlace del perfil guardado como público y permite
abrirlo; en móvil también permite compartirlo mediante el sistema operativo.

Para un dominio web distinto al de la API, configurar `EXPO_PUBLIC_WEB_URL`
con la URL base de la web, incluyendo `/app/` cuando corresponda. En navegador
se usa por defecto la dirección actual; en móvil, el origen de la API y `/app/`.
Estos enlaces abren la ficha web; no registran enlaces universales nativos.

No incluye todavía búsqueda geográfica, fotografías ni
reservas públicas. SignalR ya actualiza la agenda administrativa; ver
[agenda en tiempo real](agenda-tiempo-real.md). El GPS se introduce manualmente; no solicita permisos
de ubicación ni obtiene automáticamente la posición del dispositivo.
