# =============================================================================
# PASO 2 (en tu PC) — Subir el release al droplet
# =============================================================================
# Que hace:
#   1. Empaqueta ./publish en un .tar.gz SIN los appsettings*.json. La config
#      del servidor (cadena de conexion, JWT secret, clave de Brevo) vive SOLO
#      en el droplet y nunca se pisa con la de tu PC.
#   2. Sube por scp (copia sobre SSH) al /tmp del droplet:
#        - el paquete          -> /tmp/cuadre_backend_<ID>.tar.gz
#        - las migraciones     -> /tmp/cuadre_migrations_<ID>.sql
#        - el script servidor  -> /root/cuadre-deploy/3-servidor.sh
#
# Subir NO cambia nada de lo que esta corriendo: solo deja archivos en /tmp.
#
# <ID> es la fecha y hora (ej. 20260925_1830). Anotalo: todos los pasos del
# servidor lo piden.
#
# Te pedira la contrasena de SSH una vez por cada comando ssh/scp.
#
# Uso (desde BarberShopBackend):
#   powershell -ExecutionPolicy Bypass -File deploy\manual\2-subir.ps1
# =============================================================================
$ErrorActionPreference = "Stop"
$backend = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $backend

function Revisar($que) {
    if ($LASTEXITCODE -ne 0) { throw "Fallo: $que (codigo $LASTEXITCODE)" }
}

# Host y usuario salen del mismo config.json que usa deploy.py (no se commitea).
$config = Get-Content deploy\config.json -Raw | ConvertFrom-Json
$destino = "$($config.user)@$($config.host)"

if (-not (Test-Path publish\BarberShop.dll)) { throw "Falta publish\. Corre antes 1-preparar.ps1" }
if (-not (Test-Path deploy\migrations.sql)) { throw "Falta deploy\migrations.sql. Corre antes 1-preparar.ps1" }

$id = Get-Date -Format "yyyyMMdd_HHmm"
$paquete = Join-Path $env:TEMP "cuadre_backend_$id.tar.gz"

Write-Host "== Empaquetando publish (sin appsettings) ==" -ForegroundColor Cyan
# tar.exe viene con Windows 10+. -C cambia de carpeta antes de empaquetar,
# asi el .tar.gz no lleva la ruta "publish/" delante.
tar -czf $paquete --exclude "appsettings*.json" -C publish .; Revisar "tar"
Write-Host ("Paquete: {0:N1} MB" -f ((Get-Item $paquete).Length / 1MB))

Write-Host "`n== Subiendo al droplet ($destino) ==" -ForegroundColor Cyan
ssh $destino "mkdir -p /root/cuadre-deploy"; Revisar "ssh mkdir"
scp $paquete "${destino}:/tmp/cuadre_backend_$id.tar.gz"; Revisar "scp paquete"
scp deploy\migrations.sql "${destino}:/tmp/cuadre_migrations_$id.sql"; Revisar "scp migraciones"
scp deploy\manual\3-servidor.sh "${destino}:/root/cuadre-deploy/3-servidor.sh"; Revisar "scp script"

Remove-Item $paquete

Write-Host "`nListo. ID del release: $id" -ForegroundColor Green
Write-Host "Ahora entra al droplet y sigue el README (paso 3):"
Write-Host "  ssh $destino"
Write-Host "  bash /root/cuadre-deploy/3-servidor.sh estado"
