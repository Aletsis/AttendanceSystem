# Script para preparar los archivos de instalación
$ErrorActionPreference = "Stop"

Write-Host "1. Limpiando directorios de salida..." -ForegroundColor Cyan
if (Test-Path ".\Release") { Remove-Item ".\Release" -Recurse -Force }
New-Item -ItemType Directory -Path ".\Release\Web" | Out-Null
New-Item -ItemType Directory -Path ".\Release\Service" | Out-Null
New-Item -ItemType Directory -Path ".\Release\Database" | Out-Null

Write-Host "2. Publicando Aplicación Web (Blazor Server)..." -ForegroundColor Cyan
dotnet publish "src/Presentation/AttendanceSystem.Blazor.Server" -c Release -r win-x64 --self-contained true -o ".\Release\Web"

Write-Host "3. Publicando Servicio Windows (ZKTeco)..." -ForegroundColor Cyan
# IMPORTANTE: El servicio ZKTeco DEBE ser x86 porque el SDK de ZK suele ser de 32 bits.
# Usamos PublishSingleFile=false para evitar que los DLLs nativos queden atrapados en un bundle 
# que se extrae a temp, lo cual dificulta que el registro COM los encuentre.
dotnet publish "src/Presentation/AttendanceSystem.ZKTeco.Service" -c Release -r win-x86 --self-contained true -o ".\Release\Service" /p:PublishSingleFile=false

Write-Host "4. Copiando SDK ZKTeco a la raíz del servicio..." -ForegroundColor Cyan
# Copiar todos los DLLs del SDK a la carpeta raíz del servicio para asegurar visibilidad
Copy-Item ".\src\Infrastructure\AttendanceSystem.ZKTeco\lib\*.dll" -Destination ".\Release\Service\" -Force

Write-Host "5. Limpiando subcarpeta 'lib' redundante..." -ForegroundColor Cyan
# dotnet publish a veces crea una subcarpeta lib. Movemos todo lo que haya ahí a la raíz y la borramos.
if (Test-Path ".\Release\Service\lib") {
    Get-ChildItem ".\Release\Service\lib\*" | Move-Item -Destination ".\Release\Service\" -Force -ErrorAction SilentlyContinue
    Remove-Item ".\Release\Service\lib" -Recurse -Force
}

Write-Host "6. Copiando scripts de base de datos..." -ForegroundColor Cyan
Copy-Item ".\Deploy\init_db.sql" -Destination ".\Release\Database\" -ErrorAction SilentlyContinue
Copy-Item ".\Deploy\configure_db.bat" -Destination ".\Release\Database\" -ErrorAction SilentlyContinue
Copy-Item ".\Deploy\enable_iis.ps1" -Destination ".\Release\Database\" -ErrorAction SilentlyContinue
Copy-Item ".\Deploy\configure_iis.bat" -Destination ".\Release\Web\" -ErrorAction SilentlyContinue

Write-Host "7. Verificando archivos críticos..." -ForegroundColor Cyan
$CriticalFiles = @("zkemkeeper.dll", "Interop.zkemkeeper.dll", "commpro.dll", "AttendanceSystem.ZKTeco.Service.exe")
$Missing = @()

foreach ($file in $CriticalFiles) {
    if (-not (Test-Path ".\Release\Service\$file")) {
        $Missing += $file
    }
}

if ($Missing.Count -gt 0) {
    Write-Host "ERROR: Faltan archivos críticos en el Release:" -ForegroundColor Red
    $Missing | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "------------------------------------------" -ForegroundColor Green
Write-Host "COMPILACION COMPLETADA EXITOSAMENTE" -ForegroundColor Green
Write-Host "Los archivos estan listos en la carpeta .\Release" -ForegroundColor Green
Write-Host "------------------------------------------"
