# =============================================================================
# PASO 1 (en tu PC) — Preparar el release
# =============================================================================
# Que hace, en orden:
#   1. Comprueba que el repo no tenga cambios sin commit (lo que despliegas
#      debe ser exactamente algo que existe en git, para poder volver a el).
#   2. Corre los tests. Si alguno falla, NO se sigue.
#   3. Compila en Release a ./publish  (los binarios que correran en el droplet).
#   4. Genera deploy/migrations.sql: un script SQL "idempotente" con TODAS las
#      migraciones de EF. Idempotente = se puede correr N veces; cada migracion
#      comprueba en __EFMigrationsHistory si ya se aplico y, si es asi, la salta.
#
# Nada de esto toca el droplet.
#
# Uso (desde la carpeta BarberShopBackend):
#   powershell -ExecutionPolicy Bypass -File deploy\manual\1-preparar.ps1
#   ... -PermitirCambios   -> deja preparar con cambios sin commit (solo pruebas)
# =============================================================================
param(
    [switch]$PermitirCambios
)

$ErrorActionPreference = "Stop"
# La carpeta del backend es dos niveles arriba de este script.
$backend = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $backend

function Paso($texto) { Write-Host "`n== $texto ==" -ForegroundColor Cyan }

# Los comandos externos (git, dotnet) no lanzan excepciones al fallar:
# hay que mirar $LASTEXITCODE a mano despues de cada uno.
function Revisar($que) {
    if ($LASTEXITCODE -ne 0) { throw "Fallo: $que (codigo $LASTEXITCODE)" }
}

Paso "1/4 Estado de git"
$rama = git rev-parse --abbrev-ref HEAD; Revisar "git rev-parse"
$commit = git rev-parse --short HEAD; Revisar "git rev-parse"
$cambios = git status --porcelain
Write-Host "Rama: $rama  Commit: $commit"
if ($cambios -and -not $PermitirCambios) {
    Write-Host $cambios
    throw "Hay cambios sin commit. Haz commit o usa -PermitirCambios (solo para pruebas)."
}

Paso "2/4 Tests"
dotnet test BarberShop.Tests --nologo -v q; Revisar "dotnet test"

Paso "3/4 Compilar Release en ./publish"
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
dotnet publish BarberShop.csproj -c Release -o publish --nologo -v q; Revisar "dotnet publish"
if (-not (Test-Path publish\BarberShop.dll)) { throw "No se genero publish\BarberShop.dll" }

Paso "4/4 Script de migraciones idempotente"
dotnet ef migrations script --idempotent --no-build --configuration Release -o deploy\migrations.sql; Revisar "dotnet ef migrations script"

# Deja constancia de que commit se esta desplegando, para saberlo en el servidor.
"$rama $commit $(Get-Date -Format s)" | Out-File -Encoding ascii publish\VERSION.txt

Write-Host "`nListo. Release preparado desde $rama@$commit." -ForegroundColor Green
Write-Host "Siguiente: deploy\manual\2-subir.ps1"
