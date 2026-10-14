#!/usr/bin/env python3
"""
Deploy de Cuadre APP al droplet via paramiko.

Uso:
    python deploy.py preflight   # solo verifica, no conecta ni despliega
    python deploy.py server      # backend + web app
    python deploy.py backend     # dotnet publish + migraciones + restart API
    python deploy.py landing     # sube la landing estatica
    python deploy.py webapp      # build web de Expo + subida
    python deploy.py all         # todo lo anterior

Flags:
    --skip-build   No recompilar, usar el ./publish o dist existente
    --prepare-only Compilar y validar artefactos, sin conectar al droplet
    --allow-dirty  Permitir preparar/desplegar con cambios sin commit
    --yes          No pedir confirmacion

Config: deploy/config.json (ver config.template.json). NO se commitea.

Notas de seguridad:
- NUNCA sube appsettings.Production.json ni Development: la config del
  servidor se administra a mano en /var/www/barbershop.
- Hace un pg_dump de respaldo antes de aplicar migraciones.
- Las migraciones se aplican con el script idempotente de EF Core, que
  solo ejecuta las que no esten en __EFMigrationsHistory.
"""
import argparse
import glob
import json
import os
import posixpath
import shutil
import subprocess
import sys
import time

import paramiko

# Consolas Windows usan cp1252; sin esto los simbolos unicode revientan el script
if sys.stdout.encoding and sys.stdout.encoding.lower() not in ("utf-8", "utf8"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

DEPLOY_DIR = os.path.dirname(os.path.abspath(__file__))
BACKEND_DIR = os.path.dirname(DEPLOY_DIR)                 # BarberShopBackend
REPO_DIR = os.path.dirname(BACKEND_DIR)                   # BarbershopAPP
PUBLISH_DIR = os.path.join(BACKEND_DIR, "publish")
LANDING_DIR = os.path.join(REPO_DIR, "BarberShopLanding")
APP_DIR = os.path.join(REPO_DIR, "BarberShopApp")
WEBAPP_DIST = os.path.join(APP_DIR, "dist")
MIGRATIONS_SQL = os.path.join(DEPLOY_DIR, "migrations.sql")

REMOTE_APP_DIR = "/var/www/barbershop"
REMOTE_LANDING_DIR = "/var/www/landing"
REMOTE_WEBAPP_DIR = "/var/www/webApp"
SERVICE = "barbershop"
DB_NAME = "BarberShopDB"

# Nunca subir estos archivos: la config del servidor es distinta a la local
EXCLUDE_FILES = {
    "appsettings.json",
    "appsettings.Production.json",
    "appsettings.Development.json",
    "appsettings.Template.json",
}
NODE_COMMAND = None


def find_node():
    node = shutil.which("node")
    if node:
        return node
    codex_candidates = glob.glob(os.path.expanduser(
        "~/.cache/codex-runtimes/*/dependencies/node/bin/node.exe"
    ))
    return codex_candidates[0] if codex_candidates else None


def selected_targets(target):
    if target == "server":
        return {"backend", "webapp"}
    if target == "all":
        return {"backend", "landing", "webapp"}
    if target == "preflight":
        return {"backend", "webapp"}
    return {target}


def load_config():
    path = os.path.join(DEPLOY_DIR, "config.json")
    if not os.path.exists(path):
        sys.exit(
            "Falta deploy/config.json. Copia config.template.json a config.json "
            "y completa host/usuario/password."
        )
    with open(path, encoding="utf-8") as f:
        cfg = json.load(f)
    if not cfg.get("host") or not cfg.get("user"):
        sys.exit("deploy/config.json requiere host y user.")
    if not cfg.get("key_file") and not cfg.get("password"):
        sys.exit("Configura key_file o password en deploy/config.json.")
    return cfg


def connect(cfg) -> paramiko.SSHClient:
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    kwargs = {"username": cfg["user"], "timeout": 30}
    if cfg.get("key_file"):
        kwargs["key_filename"] = os.path.expanduser(cfg["key_file"])
    else:
        kwargs["password"] = cfg["password"]
    client.connect(cfg["host"], **kwargs)
    return client


def run_remote(ssh, cmd, check=True, quiet=False):
    if not quiet:
        print(f"  $ {cmd}")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=300)
    out = stdout.read().decode(errors="replace")
    err = stderr.read().decode(errors="replace")
    code = stdout.channel.recv_exit_status()
    if out.strip() and not quiet:
        print("    " + out.strip().replace("\n", "\n    "))
    if code != 0:
        print(f"    [stderr] {err.strip()}")
        if check:
            sys.exit(f"Comando remoto fallo con codigo {code}: {cmd}")
    return code, out


def run_local(cmd, cwd, env=None):
    print(f"  > {' '.join(cmd)}  (en {cwd})")
    merged_env = os.environ.copy()
    if env:
        merged_env.update(env)
    result = subprocess.run(
        cmd,
        cwd=cwd,
        shell=(os.name == "nt"),
        env=merged_env,
    )
    if result.returncode != 0:
        sys.exit(f"Comando local fallo: {' '.join(cmd)}")


def ensure_remote_dir(sftp, remote_dir):
    parts = remote_dir.strip("/").split("/")
    current = ""
    for part in parts:
        current += "/" + part
        try:
            sftp.stat(current)
        except FileNotFoundError:
            sftp.mkdir(current)


def upload_dir(sftp, local_dir, remote_dir, exclude=frozenset()):
    """Sube recursivamente a un directorio de staging."""
    uploaded = 0
    for root, dirs, files in os.walk(local_dir):
        # Nunca subir control de versiones ni basura
        dirs[:] = [d for d in dirs if d not in (".git", "node_modules", ".expo")]
        rel_root = os.path.relpath(root, local_dir)
        remote_root = remote_dir if rel_root == "." else posixpath.join(
            remote_dir, rel_root.replace(os.sep, "/"))
        ensure_remote_dir(sftp, remote_root)

        for name in files:
            if name in exclude:
                continue
            local_path = os.path.join(root, name)
            remote_path = posixpath.join(remote_root, name)
            sftp.put(local_path, remote_path)
            uploaded += 1
    print(f"  Subidos {uploaded} archivos")


def git_is_dirty(path):
    result = subprocess.run(
        ["git", "status", "--porcelain"],
        cwd=path,
        capture_output=True,
        text=True,
        shell=(os.name == "nt"),
    )
    return result.returncode != 0 or bool(result.stdout.strip())


def preflight(cfg, targets, allow_dirty=False):
    global NODE_COMMAND
    print("\n== Preflight local ==")
    required = []
    if "backend" in targets:
        required.append("dotnet")
    if "webapp" in targets:
        NODE_COMMAND = find_node()

    missing = [cmd for cmd in required if not shutil.which(cmd)]
    if missing:
        sys.exit(f"Faltan herramientas: {', '.join(missing)}")
    if "webapp" in targets and not NODE_COMMAND:
        sys.exit("Falta Node.js. Instala Node.js LTS.")

    repos = []
    if "backend" in targets:
        repos.append(BACKEND_DIR)
    if "webapp" in targets:
        repos.append(APP_DIR)
    dirty = [path for path in repos if git_is_dirty(path)]
    if dirty and not allow_dirty:
        names = ", ".join(os.path.basename(path) for path in dirty)
        sys.exit(
            f"Hay cambios sin commit en: {names}. "
            "Haz commit o usa --allow-dirty conscientemente."
        )

    if "webapp" in targets and not cfg.get("public_api_url"):
        sys.exit("Falta public_api_url en deploy/config.json.")

    if "webapp" in targets:
        print(f"  API publica: {cfg['public_api_url']}")
        print(f"  Node: {NODE_COMMAND}")
    print("  Repositorios y herramientas: OK")


def remote_preflight(cfg):
    print("\n== Preflight remoto (solo lectura) ==")
    ssh = connect(cfg)
    try:
        run_remote(ssh, "command -v rsync && command -v systemctl && command -v psql")
        run_remote(ssh, f"systemctl is-active {SERVICE}")
        run_remote(ssh, f"test -d {REMOTE_APP_DIR} && test -d {REMOTE_WEBAPP_DIR}")
        run_remote(ssh, "df -h /var | tail -1")
        run_remote(
            ssh,
            "curl -s -o /dev/null -w '%{http_code}\\n' http://localhost:5000/health",
        )
        print("  Preflight remoto correcto. No se modifico el droplet.")
    finally:
        ssh.close()


def prepare_backend(skip_build):
    if not skip_build:
        print("- Compilando backend Release...")
        run_local(
            ["dotnet", "publish", "BarberShop.csproj", "-c", "Release", "-o", PUBLISH_DIR],
            BACKEND_DIR,
        )
        print("- Generando migraciones idempotentes...")
        run_local(
            [
                "dotnet", "ef", "migrations", "script", "--idempotent",
                "-o", MIGRATIONS_SQL, "--no-build", "--configuration", "Release",
            ],
            BACKEND_DIR,
        )
    if not os.path.isfile(os.path.join(PUBLISH_DIR, "BarberShop.dll")):
        sys.exit("No existe publish/BarberShop.dll.")


def prepare_webapp(skip_build, cfg):
    if not skip_build:
        run_local(
            [
                NODE_COMMAND,
                os.path.join(APP_DIR, "node_modules", "expo", "bin", "cli"),
                "export", "--platform", "web", "--clear",
            ],
            APP_DIR,
            env={"EXPO_PUBLIC_API_URL": cfg["public_api_url"]},
        )
        run_local([NODE_COMMAND, "fix-paths.js"], APP_DIR)
    index_path = os.path.join(WEBAPP_DIST, "index.html")
    if not os.path.isfile(index_path):
        sys.exit("No existe BarberShopApp/dist/index.html.")
    with open(index_path, encoding="utf-8") as f:
        html = f.read()
    if "/app/_expo/" not in html:
        sys.exit("El build web no contiene rutas /app/_expo/. Revisa fix-paths.js.")


def prepare(targets, skip_build, cfg):
    print("\n== Preparacion local ==")
    if "backend" in targets:
        prepare_backend(skip_build)
    if "webapp" in targets:
        prepare_webapp(skip_build, cfg)
    if "landing" in targets and not os.path.isfile(os.path.join(LANDING_DIR, "index.html")):
        sys.exit("No existe BarberShopLanding/index.html.")
    print("  Artefactos listos. No se ha conectado al droplet.")


# ─────────────────────────────────────────────────────────────
# Backend
# ─────────────────────────────────────────────────────────────

def deploy_backend(ssh, sftp):
    print("\n== Backend ==")
    release_id = time.strftime("%Y%m%d_%H%M%S")
    staging = f"/tmp/cuadre_backend_{release_id}"
    backup = f"/var/backups/cuadre/backend_{release_id}"

    run_remote(ssh, "command -v rsync >/dev/null && command -v systemctl >/dev/null")
    run_remote(ssh, f"rm -rf {staging} && mkdir -p {staging}")
    upload_dir(sftp, PUBLISH_DIR, staging, exclude=EXCLUDE_FILES)

    print("- Respaldo de la BD antes de migrar...")
    run_remote(ssh, "/usr/local/bin/backup-db.sh || true")

    if os.path.exists(MIGRATIONS_SQL):
        print("- Aplicando migraciones pendientes...")
        sftp.put(MIGRATIONS_SQL, "/tmp/cuadre_migrations.sql")
        run_remote(
            ssh,
            "sudo -u postgres psql -v ON_ERROR_STOP=1 -d "
            f"{DB_NAME} -f /tmp/cuadre_migrations.sql > /tmp/cuadre_migrations.log 2>&1 "
            "&& tail -5 /tmp/cuadre_migrations.log",
        )
        run_remote(ssh, "rm -f /tmp/cuadre_migrations.sql", quiet=True)

    print("- Respaldando release actual...")
    run_remote(
        ssh,
        f"mkdir -p {backup} && rsync -a {REMOTE_APP_DIR}/ {backup}/",
    )

    print("- Activando archivos del nuevo release...")
    run_remote(
        ssh,
        f"rsync -a --delete --exclude='appsettings*.json' {staging}/ {REMOTE_APP_DIR}/",
    )
    run_remote(ssh, f"rm -rf {staging}", quiet=True)

    print("- Reiniciando servicio...")
    run_remote(ssh, f"systemctl restart {SERVICE}")

    print("- Verificando salud...")
    for attempt in range(12):
        time.sleep(5)
        code, out = run_remote(
            ssh,
            "curl -s -o /dev/null -w '%{http_code}' http://localhost:5000/health",
            check=False, quiet=True,
        )
        status = out.strip()
        print(f"  intento {attempt + 1}: /health -> {status or 'sin respuesta'}")
        if status == "200":
            print("  ✔ Backend desplegado y saludable")
            return
    print("  ✖ El health check no respondio 200. Ultimos logs:")
    run_remote(ssh, f"journalctl -u {SERVICE} -n 30 --no-pager", check=False)
    print("- Restaurando binarios del release anterior...")
    run_remote(
        ssh,
        f"rsync -a --delete --exclude='appsettings*.json' {backup}/ {REMOTE_APP_DIR}/",
    )
    run_remote(ssh, f"systemctl restart {SERVICE}", check=False)
    sys.exit("Deploy revertido: el backend nuevo no supero /health.")


# ─────────────────────────────────────────────────────────────
# Landing y web app
# ─────────────────────────────────────────────────────────────

def deploy_landing(sftp):
    print("\n== Landing ==")
    upload_dir(sftp, LANDING_DIR, REMOTE_LANDING_DIR)
    print("  ✔ Landing actualizada")


def deploy_webapp(ssh, sftp):
    print("\n== Web app (Expo) ==")
    release_id = time.strftime("%Y%m%d_%H%M%S")
    staging = f"/tmp/cuadre_webapp_{release_id}"
    backup = f"/var/backups/cuadre/webapp_{release_id}"
    run_remote(ssh, "command -v rsync >/dev/null")
    run_remote(ssh, f"rm -rf {staging} && mkdir -p {staging}")
    upload_dir(sftp, WEBAPP_DIST, staging)
    run_remote(
        ssh,
        f"mkdir -p {backup} && rsync -a {REMOTE_WEBAPP_DIR}/ {backup}/",
    )
    run_remote(ssh, f"rsync -a --delete {staging}/ {REMOTE_WEBAPP_DIR}/")
    run_remote(ssh, f"rm -rf {staging}", quiet=True)
    run_remote(ssh, f"test -s {REMOTE_WEBAPP_DIR}/index.html")
    print("  ✔ Web app actualizada")


# ─────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Deploy de Cuadre APP")
    parser.add_argument(
        "target",
        choices=["preflight", "server", "backend", "landing", "webapp", "all"],
    )
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--prepare-only", action="store_true")
    parser.add_argument("--remote-check", action="store_true")
    parser.add_argument("--allow-dirty", action="store_true")
    parser.add_argument("--yes", action="store_true")
    args = parser.parse_args()

    cfg = load_config()
    targets = selected_targets(args.target)
    preflight(cfg, targets, args.allow_dirty)

    if args.target == "preflight":
        if args.remote_check:
            remote_preflight(cfg)
            print("\nPreflight completado. No se modifico el droplet.")
        else:
            print("\nPreflight completado. No se conecto al droplet.")
        return

    prepare(targets, args.skip_build, cfg)
    if args.prepare_only:
        return

    print(f"Destino : {cfg['host']} ({args.target})")
    if not args.yes:
        answer = input("¿Continuar con el deploy? [s/N] ").strip().lower()
        if answer not in ("s", "si", "sí", "y", "yes"):
            sys.exit("Cancelado.")

    ssh = connect(cfg)
    sftp = ssh.open_sftp()
    try:
        if "backend" in targets:
            deploy_backend(ssh, sftp)
        if "landing" in targets:
            deploy_landing(sftp)
        if "webapp" in targets:
            deploy_webapp(ssh, sftp)
        print("\nDeploy completado.")
    finally:
        sftp.close()
        ssh.close()


if __name__ == "__main__":
    main()
