# Plan de endurecimiento del droplet (PENDIENTE DE APROBACIÓN)

Nada de esto se ha ejecutado. Son los comandos exactos, para revisar y aprobar.
Contexto: 41,275 intentos fallidos de SSH en auth.log, root+password habilitado,
UFW inactivo, sin fail2ban, 1GB RAM sin swap.

**⚠ Coordinación con FlotaIQ:** el deploy de FlotaIQ usa paramiko con la misma
password de root. Los pasos 1-2 NO rompen nada (agregan llave, no quitan password).
El paso 3 (deshabilitar password) SÍ requiere actualizar antes el script de
FlotaIQ para usar `key_filename`. Por eso está separado.

## Paso 0 — Generar llave en la PC de Nicolas (local, sin riesgo)

```powershell
ssh-keygen -t ed25519 -f $env:USERPROFILE\.ssh\cuadre_droplet -C "nicolas-pc"
```

## Paso 1 — Instalar la llave pública en el droplet (no rompe nada)

```bash
# agregar el contenido de cuadre_droplet.pub a /root/.ssh/authorized_keys
cat >> /root/.ssh/authorized_keys  # (o via ssh-copy-id)
chmod 600 /root/.ssh/authorized_keys
```

Verificar: `ssh -i ~/.ssh/cuadre_droplet root@TU_IP_DEL_SERVIDOR` debe entrar sin password.
Después, cambiar `key_file` en deploy/config.json a la ruta de la llave.

## Paso 2 — fail2ban + UFW + swap (no rompe nada)

```bash
apt-get update && apt-get install -y fail2ban
systemctl enable --now fail2ban          # defaults: banea IPs tras 5 fallos ssh

ufw allow OpenSSH
ufw allow 'Nginx Full'                    # 80 + 443
ufw --force enable

# swap de 2GB (evita OOM con 1GB de RAM y dos APIs .NET + Postgres)
fallocate -l 2G /swapfile
chmod 600 /swapfile
mkswap /swapfile && swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab
```

## Paso 3 — Deshabilitar password SSH (SOLO después de actualizar FlotaIQ)

Prerequisito: el deploy de FlotaIQ debe usar la llave (paramiko `key_filename`).

```bash
# /etc/ssh/sshd_config.d/99-hardening.conf
printf 'PasswordAuthentication no\nPermitRootLogin prohibit-password\n' \
  > /etc/ssh/sshd_config.d/99-hardening.conf
sshd -t && systemctl reload ssh
```

Mantener una sesión SSH abierta mientras se prueba, por si hay que revertir.

## Paso 4 — Rotación de secretos (requiere deploy + editar config del servidor)

1. **JWT Secret**: generar 64 chars aleatorios, editar `/var/www/barbershop/appsettings.Production.json`,
   `systemctl restart barbershop`. Efecto: todos los usuarios deben volver a iniciar
   sesión una vez (con refresh tokens ya desplegados, solo esa vez).
2. **Brevo API key**: crear nueva en el panel de Brevo, actualizar el archivo, borrar la vieja.
3. **Gmail app password**: revocar en https://myaccount.google.com/apppasswords y crear
   una nueva solo si aún se usa SMTP (el código actual envía por Brevo API).
4. **Password de postgres**: `ALTER USER postgres PASSWORD '...'` + actualizar
   appsettings del servidor (y el de FlotaIQ si comparte usuario postgres).
5. **Password de root**: `passwd` — actualizar deploy/config.json de ambos proyectos
   (o mejor: completar paso 3 y eliminar el uso de passwords).
