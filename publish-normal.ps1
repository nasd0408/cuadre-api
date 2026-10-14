$IP = $env:CUADRE_SERVER_IP
if (-not $IP) { throw "Define la IP del servidor: `$env:CUADRE_SERVER_IP = 'x.x.x.x'" }

Write-Host "Publicando..." -ForegroundColor Green
dotnet publish -c Release -o ./publish

Write-Host "Subiendo solo lo necesario..." -ForegroundColor Green
scp ./publish/BarberShop.dll root@${IP}:/var/www/barbershop/
scp ./publish/BarberShop.pdb root@${IP}:/var/www/barbershop/

Write-Host "Reiniciando servicio..." -ForegroundColor Green
ssh root@$IP "systemctl restart barbershop"

Write-Host "Deploy completado!" -ForegroundColor Green