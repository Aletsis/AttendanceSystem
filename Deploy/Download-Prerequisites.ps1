# Script para descargar automáticamente los instaladores de terceros requeridos por Inno Setup
param(
    [string]$DeployDir = "$PSScriptRoot",
    [switch]$Force = $false
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $DeployDir)) {
    $DeployDir = ".\Deploy"
}

$PostgresTarget = Join-Path $DeployDir "postgresql-installer.exe"
$DotNetTarget = Join-Path $DeployDir "dotnet-hosting.exe"

# 1. Descargar PostgreSQL 16.6 x64 si no existe
if (-not (Test-Path $PostgresTarget) -or $Force) {
    Write-Host "Descargando instalador de PostgreSQL 16.x..." -ForegroundColor Cyan
    # URL directa de PostgreSQL para Windows x64 de EnterpriseDB
    $pgUrl = "https://sbp.enterprisedb.com/getfile.jsp?fileid=1258894"
    Invoke-WebRequest -Uri $pgUrl -OutFile $PostgresTarget -UserAgent "Mozilla/5.0"
    Write-Host "PostgreSQL descargado exitosamente en $PostgresTarget" -ForegroundColor Green
} else {
    Write-Host "PostgreSQL installer ya existe en $PostgresTarget" -ForegroundColor Yellow
}

# 2. Descargar ASP.NET Core Hosting Bundle si no existe
if (-not (Test-Path $DotNetTarget) -or $Force) {
    Write-Host "Descargando ASP.NET Core Hosting Bundle..." -ForegroundColor Cyan
    # URL oficial de Microsoft para Hosting Bundle
    $dotnetUrl = "https://download.visualstudio.microsoft.com/download/pr/97738f6d-31ca-43bc-9387-a246d6b2c2fa/e36928da5e1b7e6515b804cb3603d36b/dotnet-hosting-9.0.2-win.exe"
    Invoke-WebRequest -Uri $dotnetUrl -OutFile $DotNetTarget -UserAgent "Mozilla/5.0"
    Write-Host "ASP.NET Core Hosting Bundle descargado exitosamente en $DotNetTarget" -ForegroundColor Green
} else {
    Write-Host "ASP.NET Core Hosting Bundle ya existe en $DotNetTarget" -ForegroundColor Yellow
}

Write-Host "Todos los prerrequisitos están listos en $DeployDir." -ForegroundColor Green
