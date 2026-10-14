$IP = $env:CUADRE_SERVER_IP
if (-not $IP) { throw "Define la IP del servidor: `$env:CUADRE_SERVER_IP = 'x.x.x.x'" }

Write-Host "Publicando..." -ForegroundColor Green
dotnet publish -c Release -o ./publish

Write-Host "Eliminando appsettings..." -ForegroundColor Green
Remove-Item ./publish/appsettings.*.json -ErrorAction SilentlyContinue
Remove-Item ./publish/appsettings.json -ErrorAction SilentlyContinue

Write-Host "Subiendo todo al servidor..." -ForegroundColor Green
scp -r ./publish/. root@${IP}:/var/www/barbershop/

Write-Host "Reiniciando servicio..." -ForegroundColor Green
ssh root@$IP "systemctl restart barbershop"

Write-Host "Deploy completado!" -ForegroundColor Green