# Deploy manual del backend, paso a paso

Este runbook existe para que **tú** despliegues entendiendo cada paso. `deploy.py` hace lo mismo de una sola vez; este es el mismo proceso desarmado.

## El mapa mental

Un deploy siempre tiene la misma anatomía, sea con systemd, Docker o Kubernetes:

```
build → artefacto → subir → backup → migrar BD → respaldar versión actual → cambiar binarios → reiniciar → health check
                                                                                                      ↓ si falla
                                                                                                   rollback
```

| Qué | Dónde vive en el droplet |
|---|---|
| Binarios de la API | `/var/www/barbershop/` |
| Configuración (cadena de conexión, JWT, Brevo) | `/var/www/barbershop/appsettings*.json`. **Nunca se sube desde tu PC.** |
| Servicio | systemd, unidad `barbershop` (`/etc/systemd/system/barbershop.service`) |
| Puerto | La API escucha en `localhost:5000`. nginx la publica en `https://cuadreapp.duckdns.org/api/` |
| Base de datos | PostgreSQL 16 local, base `BarberShopDB` |
| Backups de BD | `/var/backups/postgres/` (cron diario 3 AM, 7 días de retención) |
| Respaldos de binarios | `/var/backups/cuadre/backend_antes_de_<ID>/` |

## Antes de empezar (una sola vez)

- `deploy/config.json` existe con `host` y `user` (ya lo tienes).
- En PowerShell, `ssh` y `scp` funcionan (vienen con Windows 10+). Te pedirán la contraseña del droplet en cada comando.
- `dotnet ef` instalado: `dotnet tool install --global dotnet-ef`.

---

## Deploy completo

### Paso 1 — Preparar (en tu PC)

Desde `BarberShopBackend`:

```powershell
powershell -ExecutionPolicy Bypass -File deploy\manual\1-preparar.ps1
```

**Qué esperar:** 4 bloques `== 1/4 ... ==` hasta `4/4`, los tests en verde y al final `Listo. Release preparado desde <rama>@<commit>`.

Si hay cambios sin commit, se detiene. Es a propósito: lo que despliegas tiene que existir en git para poder volver a ello.

### Paso 2 — Subir (en tu PC)

```powershell
powershell -ExecutionPolicy Bypass -File deploy\manual\2-subir.ps1
```

**Qué esperar:** el tamaño del paquete (~5 MB) y 4 pedidos de contraseña. Al final: `ID del release: 20260925_1830`. **Anota ese ID.**

Esto solo deja archivos en `/tmp` del droplet. Producción no cambió.

### Paso 3 — En el droplet, un comando a la vez

```powershell
ssh root@TU_IP_DEL_SERVIDOR
```

A partir de aquí estás en el servidor. Cambia `ID` por el tuyo:

```bash
ID=20260925_1830
S=/root/cuadre-deploy/3-servidor.sh
```

**3.1 Foto del estado actual.** Solo lee, no cambia nada:
```bash
bash $S estado
```
Mira que el servicio esté `active` y el health en `HTTP 200`. Si ya estaba caído antes de empezar, detente y averigua por qué.

**3.2 Desempaquetar en /tmp.** Producción sigue sin cambios:
```bash
bash $S desempaquetar $ID
```

**3.3 Backup de la BD.** Tu red de seguridad para el paso siguiente:
```bash
bash $S backup-bd
```

**3.4 Migrar la BD.** Es el primer paso que **cambia producción**:
```bash
bash $S migrar $ID
```
Compara las listas "ANTES" y "DESPUÉS". Si el release no trae migraciones nuevas, las dos listas son iguales y está bien: el script es idempotente.

> ¿Por qué migrar ANTES de cambiar binarios? Durante unos segundos, la versión vieja de la API corre sobre la BD ya migrada. Por eso las migraciones deben ser **compatibles hacia atrás**: agregar columnas o tablas sí; renombrar o borrar, no, al menos no en el mismo release. A esto se le llama *expand / contract*.

**3.5 Respaldar la versión que corre hoy:**
```bash
bash $S respaldar-release $ID
```

**3.6 Cambiar los binarios:**
```bash
bash $S activar $ID
```
Los archivos cambiaron, pero el proceso en memoria sigue siendo el viejo.

**3.7 Reiniciar.** Unos segundos de corte:
```bash
bash $S reiniciar
```
Debe decir `Active: active (running)`.

**3.8 Verificar:**
```bash
bash $S verificar
```
Espera a `API sana (HTTP 200)`. Luego ábrelo desde tu navegador: https://cuadreapp.duckdns.org/health

**3.9 Limpiar /tmp:**
```bash
bash $S limpiar $ID
```

Listo. `bash $S estado` debería mostrar el commit nuevo en "Versión desplegada".

---

## Si algo sale mal

### La API no arranca después de activar/reiniciar

1. Mira el error real:
   ```bash
   journalctl -u barbershop -n 100 --no-pager
   ```
   Causas típicas: falta una clave en `appsettings.Production.json`, Postgres caído, o una excepción al arrancar.
2. Si no lo arreglas en un par de minutos, vuelve atrás:
   ```bash
   bash $S rollback $ID
   ```
   Restaura los binarios respaldados en 3.5, reinicia y verifica.

### La migración falló (paso 3.4)

La app vieja sigue corriendo; no cambiaste binarios. Lee el log que indica el script (`/tmp/cuadre_migrations_<ID>.log`). Cada migración del script corre en su propia transacción, así que normalmente la que falló no dejó nada a medias.

### Restaurar la base de datos (último recurso)

Solo si una migración dejó datos mal y el rollback de binarios no basta. **Pierdes lo que se haya escrito después del backup.**

```bash
systemctl stop barbershop
ls -lh /var/backups/postgres/BarberShopDB_*.sql.gz     # elige el backup del paso 3.3
sudo -u postgres dropdb BarberShopDB
sudo -u postgres createdb BarberShopDB
gunzip -c /var/backups/postgres/BarberShopDB_<fecha>.sql.gz | sudo -u postgres psql -d BarberShopDB
systemctl start barbershop
```

Antes de hacerlo de verdad en producción, practícalo en local con Docker (Fase 1).

---

## Chuleta del droplet

| Para | Comando |
|---|---|
| ¿Está viva la API? | `systemctl status barbershop` / `curl localhost:5000/health` |
| Logs en vivo (Ctrl+C para salir) | `journalctl -u barbershop -f` |
| Logs de la última hora | `journalctl -u barbershop --since "1 hour ago" --no-pager` |
| Solo errores | `journalctl -u barbershop -p err -n 50 --no-pager` |
| Memoria (hay solo 1 GB y sin swap) | `free -m` |
| Qué consume memoria | `ps -eo rss,comm --sort=-rss \| head` |
| Disco | `df -h /` |
| Entrar a la BD | `sudo -u postgres psql -d BarberShopDB` |
| Consulta de solo lectura segura | Dentro de psql: `BEGIN READ ONLY;` ... `ROLLBACK;` |
| Probar la config de nginx | `nginx -t` (solo valida, no aplica) |
| Recargar nginx tras un cambio | `nginx -t && systemctl reload nginx` |
| Versión desplegada | `cat /var/www/barbershop/VERSION.txt` |
| Respaldos disponibles | `bash /root/cuadre-deploy/3-servidor.sh respaldos` |

### Qué NO tocar

- **FlotaIQ** (`flotaiq.service`, `FlotaIQDB`, su sitio de nginx) comparte el droplet y es otra app.
- **La seguridad del servidor** (SSH, firewall, usuarios, sshd): la maneja otro proceso aparte.
- **`appsettings*.json` del servidor**: si hay que cambiar un valor, se edita a mano ahí, con un `cp` de respaldo antes, y luego se reinicia.
