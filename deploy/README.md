# Release de Cuadre APP

Este flujo separa tres artefactos:

- **Backend**: `/var/www/barbershop` + migraciones PostgreSQL.
- **Web app**: build Expo servido bajo `/app/`.
- **Android**: AAB generado por EAS y subido manualmente a Play Console.

Nada se despliega con los comandos de preflight o `--prepare-only`.

Requiere Python 3, el SDK de .NET y Node.js LTS. Los preflights detectan el
runtime de Node incluido con Codex como respaldo, sin reinstalar dependencias.

## Primera vez

```
copy config.template.json config.json
# editar host, credencial SSH y public_api_url
python -m pip install -r requirements.txt
```

`config.json` está en `.gitignore` — nunca se commitea.

## Verificación sin deploy

```
python deploy.py preflight
python deploy.py server --prepare-only
```

Para verificar el droplet sin modificarlo:

```powershell
python deploy.py preflight --remote-check
```

Solo consulta herramientas, servicio, directorios, espacio y `/health`.

El segundo comando genera `publish/`, el SQL idempotente de migraciones y
`BarberShopApp/dist`, pero no abre SSH.

El preflight exige repos limpios. Durante desarrollo puede usarse
`--allow-dirty`; para una publicación real conviene siempre hacer commit.

## Deploy al droplet

```powershell
python deploy.py server
```

`server` publica backend y web app. También existen objetivos individuales:

```powershell
python deploy.py backend
python deploy.py webapp
python deploy.py landing
python deploy.py all
```

Flags:

- `--skip-build`: reutiliza `publish/` y `dist/`.
- `--prepare-only`: prepara localmente y no conecta al droplet.
- `--allow-dirty`: permite cambios sin commit.
- `--yes`: omite la confirmación interactiva del deploy.

## Qué hace el deploy de backend

1. `dotnet publish -c Release -o ./publish`
2. Genera el script SQL **idempotente** de migraciones EF (solo aplica las que
   falten en `__EFMigrationsHistory` del servidor).
3. Corre el backup diario de la BD antes de migrar (`backup-db.sh`).
4. Sube el release a un directorio temporal.
5. Respalda el release activo en `/var/backups/cuadre/`.
6. Activa los archivos con `rsync --delete`, preservando **todos** los
   `appsettings*.json` del servidor.
7. Reinicia `barbershop` y espera que `/health` responda 200.
8. Si falla, restaura automáticamente los binarios anteriores y reinicia.

Las migraciones aplicadas no se revierten automáticamente; deben diseñarse para
ser compatibles con el release anterior. La migración offline actual es aditiva.

## AAB para Play Console

Desde `BarberShopApp`:

```powershell
npm run release:check
npm run release:android
```

Sin npm global, usa directamente:

```powershell
release-android.cmd
release-android.cmd -Build
```

`release:check` valida TypeScript, configuración EAS, credenciales Firebase,
repositorio limpio y que Expo pueda producir el bundle JS de Android.

`release:android` repite el preflight, exige escribir `BUILD` e inicia:

```powershell
npx eas-cli@latest build --platform android --profile production
```

El perfil `production` ya genera `.aab` y aumenta el `versionCode` remoto. El
script **no** ejecuta `eas submit`: descarga el AAB desde el enlace de EAS y
súbelo manualmente al track deseado en Play Console.

Referencias oficiales:

- https://docs.expo.dev/deploy/build-project/
- https://docs.expo.dev/submit/android/

Orden recomendado:

1. `python deploy.py server --prepare-only`
2. `npm run release:check`
3. Commit y revisión final.
4. `python deploy.py server`
5. Confirmar web y `/health`.
6. `npm run release:android`
7. Subir el AAB manualmente a Play Console.

## Importante

- El servidor comparte droplet con FlotaIQ (puerto 5100, servicio `flotaiq`).
  Este script no lo toca.
- Si se cambia la autenticación SSH del droplet (llaves, deshabilitar
  password), actualizar también el deploy de FlotaIQ que usa el mismo acceso.
