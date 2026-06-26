# Script híbrido para habilitar IIS y WebSockets (Windows Desktop y Server)
$ErrorActionPreference = "Stop"

function Enable-FeatureHybrid {
    param(
        [string]$DesktopName, # Nombre para Enable-WindowsOptionalFeature (DISM)
        [string]$ServerName   # Nombre para Install-WindowsFeature (ServerManager)
    )

    if (Get-Command "Install-WindowsFeature" -ErrorAction SilentlyContinue) {
        # Entorno Windows Server
        Write-Host "Server Detectado: Instalando $ServerName..."
        try {
            Install-WindowsFeature -Name $ServerName -IncludeAllSubFeature -IncludeManagementTools | Out-Null
        }
        catch {
            Write-Warning "No se pudo instalar $ServerName con ServerManager. Intentando método alternativo..."
            Enable-WindowsOptionalFeature -Online -FeatureName $DesktopName -All -NoRestart
        }
    }
    else {
        # Entorno Windows Desktop (10/11)
        Write-Host "Desktop Detectado: Habilitando $DesktopName..."
        Enable-WindowsOptionalFeature -Online -FeatureName $DesktopName -All -NoRestart
    }
}

Write-Host "Iniciando configuración de IIS..."

# 1. Habilitar IIS Core
Enable-FeatureHybrid -DesktopName "IIS-WebServerRole" -ServerName "Web-Server"

# 2. Habilitar Herramientas de Administración
Enable-FeatureHybrid -DesktopName "IIS-ManagementConsole" -ServerName "Web-Mgmt-Console"

# 3. Habilitar ASP.NET y Extensiones
Enable-FeatureHybrid -DesktopName "IIS-ASPNET45" -ServerName "Web-Asp-Net45"
Enable-FeatureHybrid -DesktopName "IIS-NetFxExtensibility45" -ServerName "Web-Net-Ext45"
Enable-FeatureHybrid -DesktopName "IIS-ISAPIExtensions" -ServerName "Web-ISAPI-Ext"
Enable-FeatureHybrid -DesktopName "IIS-ISAPIGlobalFilter" -ServerName "Web-ISAPI-Filter"

# 4. Habilitar WebSockets (CRÍTICO para Blazor Server)
# Esto asegura que SignalR funcione correctamente y no falle en bucles de reconexión
Enable-FeatureHybrid -DesktopName "IIS-WebSockets" -ServerName "Web-WebSockets"

Write-Host "IIS y WebSockets configurados correctamente."
