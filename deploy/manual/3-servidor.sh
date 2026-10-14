#!/usr/bin/env bash
# =============================================================================
# PASO 3 (en el droplet) — Aplicar el release, un subcomando a la vez
# =============================================================================
# Se corre como root en el droplet:
#   bash /root/cuadre-deploy/3-servidor.sh <subcomando> [ID]
#
# Orden normal de un deploy (ver README.md):
#   estado                     -> foto de como esta todo ANTES de tocar nada
#   desempaquetar <ID>         -> descomprime el paquete en /tmp (no toca la app)
#   backup-bd                  -> pg_dump de la base (red de seguridad)
#   migrar <ID>                -> aplica migraciones pendientes a la BD
#   respaldar-release <ID>     -> copia la version que corre hoy, para rollback
#   activar <ID>               -> reemplaza los binarios de /var/www/barbershop
#   reiniciar                  -> systemctl restart barbershop
#   verificar                  -> espera a que /health responda 200 y muestra logs
#   limpiar <ID>               -> borra los temporales de /tmp
#
# Si algo sale mal despues de "activar":
#   rollback <ID>              -> vuelve a los binarios respaldados en ese release
#   respaldos                  -> lista respaldos de binarios y de la BD
#
# Este script SOLO toca: /tmp/cuadre_*, /var/www/barbershop,
# /var/backups/cuadre y el servicio "barbershop". Nunca FlotaIQ, nginx ni la
# configuracion de seguridad del servidor.
# =============================================================================

# -e: aborta si un comando falla. -u: error si se usa una variable sin definir.
# -o pipefail: en "a | b", si "a" falla, la tuberia entera cuenta como fallo.
set -euo pipefail

APP_DIR=/var/www/barbershop
BACKUP_DIR=/var/backups/cuadre
SERVICE=barbershop
DB=BarberShopDB
HEALTH_URL=http://localhost:5000/health

# Colores para distinguir de un vistazo lo que es OK de lo que es error.
verde()  { printf '\033[32m%s\033[0m\n' "$*"; }
rojo()   { printf '\033[31m%s\033[0m\n' "$*"; }
titulo() { printf '\n\033[36m== %s ==\033[0m\n' "$*"; }

# Muchos subcomandos necesitan el ID del release; si falta, se explica y se sale.
requiere_id() {
    if [[ -z "${ID:-}" ]]; then
        rojo "Falta el ID del release. Ejemplo: $0 $CMD 20260925_1830"
        exit 1
    fi
}

cmd_estado() {
    titulo "Servicio"
    systemctl is-active "$SERVICE" || true
    titulo "Version desplegada"
    cat "$APP_DIR/VERSION.txt" 2>/dev/null || echo "(sin VERSION.txt: desplegada antes del runbook)"
    titulo "Salud"
    curl -s -o /dev/null -w "HTTP %{http_code}\n" "$HEALTH_URL" || true
    titulo "Ultimas migraciones aplicadas"
    sudo -u postgres psql -d "$DB" -tAc \
        'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 3'
    titulo "Memoria y disco"
    free -m | head -2
    df -h / | tail -1
    titulo "Releases subidos esperando en /tmp"
    ls -1 /tmp/cuadre_backend_*.tar.gz 2>/dev/null || echo "(ninguno)"
}

cmd_desempaquetar() {
    requiere_id
    local paquete=/tmp/cuadre_backend_$ID.tar.gz
    local destino=/tmp/cuadre_backend_$ID
    [[ -f "$paquete" ]] || { rojo "No existe $paquete. Corriste 2-subir.ps1?"; exit 1; }
    titulo "Descomprimiendo en $destino"
    rm -rf "$destino" && mkdir -p "$destino"
    tar -xzf "$paquete" -C "$destino"
    # Dos comprobaciones de seguridad antes de seguir:
    [[ -f "$destino/BarberShop.dll" ]] || { rojo "El paquete no trae BarberShop.dll"; exit 1; }
    if ls "$destino"/appsettings*.json >/dev/null 2>&1; then
        rojo "El paquete trae appsettings*.json: NO debe (pisaria la config del servidor)."
        exit 1
    fi
    cat "$destino/VERSION.txt" 2>/dev/null || true
    verde "OK. Nada en produccion ha cambiado todavia."
}

cmd_backup_bd() {
    titulo "Backup de la base de datos"
    # Es el mismo script que corre el cron de las 3 AM (respalda BarberShopDB y FlotaIQDB).
    /usr/local/bin/backup-db.sh
    ls -lh /var/backups/postgres/${DB}_*.sql.gz | tail -1
    verde "OK. Ese archivo es el que usarias para restaurar la BD si una migracion sale mal."
}

cmd_migrar() {
    requiere_id
    local sql=/tmp/cuadre_migrations_$ID.sql
    local log=/tmp/cuadre_migrations_$ID.log
    [[ -f "$sql" ]] || { rojo "No existe $sql"; exit 1; }
    titulo "Migraciones ANTES"
    sudo -u postgres psql -d "$DB" -tAc \
        'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 3'
    titulo "Aplicando $sql"
    # ON_ERROR_STOP=1: al primer error psql se detiene y devuelve codigo != 0.
    # Sin eso, psql seguiria ejecutando las sentencias siguientes a un fallo.
    if ! sudo -u postgres psql -v ON_ERROR_STOP=1 -d "$DB" -f "$sql" >"$log" 2>&1; then
        tail -20 "$log"
        rojo "La migracion fallo. La app vieja sigue corriendo sin cambios de binarios."
        rojo "Revisa $log. Si la BD quedo a medias, restaura el backup (ver README)."
        exit 1
    fi
    titulo "Migraciones DESPUES"
    sudo -u postgres psql -d "$DB" -tAc \
        'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 3'
    verde "OK. Log completo en $log"
}

cmd_respaldar_release() {
    requiere_id
    local destino=$BACKUP_DIR/backend_antes_de_$ID
    titulo "Respaldando la version actual en $destino"
    mkdir -p "$destino"
    # rsync -a copia preservando permisos y fechas. Incluye los appsettings
    # para que el respaldo sea una copia exacta de lo que corre hoy.
    rsync -a "$APP_DIR/" "$destino/"
    du -sh "$destino"
    verde "OK. Para volver a esta version: $0 rollback $ID"
}

cmd_activar() {
    requiere_id
    local origen=/tmp/cuadre_backend_$ID
    [[ -f "$origen/BarberShop.dll" ]] || { rojo "Falta $origen. Corre antes: desempaquetar $ID"; exit 1; }
    [[ -d "$BACKUP_DIR/backend_antes_de_$ID" ]] || {
        rojo "No hay respaldo de la version actual. Corre antes: respaldar-release $ID"; exit 1; }
    titulo "Copiando binarios nuevos a $APP_DIR"
    # --delete: borra del destino lo que ya no existe en el release (DLLs viejas).
    # --exclude: los appsettings del servidor no se tocan ni se borran.
    rsync -a --delete --exclude='appsettings*.json' "$origen/" "$APP_DIR/"
    verde "OK. Los archivos estan cambiados, pero el proceso sigue corriendo la version"
    verde "vieja en memoria hasta que reinicies (siguiente paso: reiniciar)."
}

cmd_reiniciar() {
    titulo "Reiniciando $SERVICE"
    systemctl restart "$SERVICE"
    sleep 3
    systemctl --no-pager status "$SERVICE" | head -12
}

cmd_verificar() {
    titulo "Esperando a /health (hasta 60 s)"
    local codigo=000
    for _ in $(seq 1 12); do
        codigo=$(curl -s -o /dev/null -w '%{http_code}' "$HEALTH_URL" || true)
        [[ "$codigo" == "200" ]] && break
        echo "  todavia $codigo, esperando 5 s..."
        sleep 5
    done
    titulo "Ultimas 30 lineas del log"
    journalctl -u "$SERVICE" -n 30 --no-pager
    if [[ "$codigo" == "200" ]]; then
        verde "API sana (HTTP 200). Prueba tambien desde fuera: https://cuadreapp.duckdns.org/health"
    else
        rojo "La API NO responde 200 (ultimo codigo: $codigo). Revisa el log de arriba."
        rojo "Si no lo arreglas rapido: $0 rollback <ID>"
        exit 1
    fi
}

cmd_rollback() {
    requiere_id
    local origen=$BACKUP_DIR/backend_antes_de_$ID
    [[ -d "$origen" ]] || { rojo "No existe $origen"; cmd_respaldos; exit 1; }
    titulo "Volviendo a la version respaldada en $origen"
    rsync -a --delete --exclude='appsettings*.json' "$origen/" "$APP_DIR/"
    cmd_reiniciar
    cmd_verificar
    echo
    rojo "Ojo: esto revierte los BINARIOS, no la base de datos."
    echo "Si las migraciones nuevas solo agregaron columnas/tablas, la version vieja"
    echo "funciona igual (simplemente las ignora). Si hace falta revertir la BD,"
    echo "sigue la seccion 'Restaurar la base de datos' del README."
}

cmd_limpiar() {
    requiere_id
    rm -rf "/tmp/cuadre_backend_$ID" "/tmp/cuadre_backend_$ID.tar.gz" "/tmp/cuadre_migrations_$ID.sql"
    verde "Temporales de /tmp borrados (el log de migraciones se conserva)."
}

cmd_respaldos() {
    titulo "Respaldos de binarios ($BACKUP_DIR)"
    ls -1d "$BACKUP_DIR"/backend_* 2>/dev/null || echo "(ninguno)"
    titulo "Backups de la BD (/var/backups/postgres)"
    ls -lh /var/backups/postgres/${DB}_*.sql.gz 2>/dev/null | tail -5
}

[[ $EUID -eq 0 ]] || { rojo "Corre este script como root."; exit 1; }

CMD=${1:-ayuda}
ID=${2:-}

case "$CMD" in
    estado)             cmd_estado ;;
    desempaquetar)      cmd_desempaquetar ;;
    backup-bd)          cmd_backup_bd ;;
    migrar)             cmd_migrar ;;
    respaldar-release)  cmd_respaldar_release ;;
    activar)            cmd_activar ;;
    reiniciar)          cmd_reiniciar ;;
    verificar)          cmd_verificar ;;
    rollback)           cmd_rollback ;;
    limpiar)            cmd_limpiar ;;
    respaldos)          cmd_respaldos ;;
    *)  sed -n '2,26p' "$0" ;;   # muestra la cabecera de este archivo como ayuda
esac
