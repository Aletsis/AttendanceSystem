# Habilita IIS con lo que necesita una app ASP.NET Core / Blazor Server (incluido WebSockets).
# Windows 10/11 usa Enable-WindowsOptionalFeature (DISM); Windows Server 2019+ usa Install-WindowsFeature.
# No instala ASP.NET 4.x, ISAPI, FTP, WebDAV ni CGI: ASP.NET Core sólo necesita el módulo ANCM del Hosting Bundle.
param(
    [string]$LogFile = "$env:TEMP\enable_iis.log"
)

$ErrorActionPreference = "Stop"
try { Start-Transcript -Path $LogFile -Append | Out-Null } catch { }

$exitCode = 0
try {
    $isServer = (Get-CimInstance Win32_OperatingSystem).ProductType -ne 1
    Write-Host "Iniciando configuración de IIS ($(if ($isServer) { 'Windows Server' } else { 'Windows Desktop' }))..."

    if ($isServer) {
        $features = @(
            "Web-Server", "Web-WebServer",
            "Web-Common-Http", "Web-Default-Doc", "Web-Static-Content", "Web-Http-Errors",
            "Web-Health", "Web-Http-Logging",
            "Web-Security", "Web-Filtering",
            "Web-Performance", "Web-Stat-Compression",
            "Web-App-Dev", "Web-WebSockets",   # WebSockets: CRÍTICO para Blazor Server (SignalR)
            "Web-Mgmt-Tools", "Web-Mgmt-Console"
        )
        $result = Install-WindowsFeature -Name $features
        Write-Host "Install-WindowsFeature: Success=$($result.Success) RestartNeeded=$($result.RestartNeeded)"
        if (-not $result.Success) { throw "Install-WindowsFeature no pudo instalar IIS." }
    }
    else {
        $features = @(
            "IIS-WebServerRole", "IIS-WebServer",
            "IIS-CommonHttpFeatures", "IIS-DefaultDocument", "IIS-StaticContent", "IIS-HttpErrors",
            "IIS-HealthAndDiagnostics", "IIS-HttpLogging",
            "IIS-Security", "IIS-RequestFiltering",
            "IIS-Performance", "IIS-HttpCompressionStatic",
            "IIS-ApplicationDevelopment", "IIS-WebSockets",   # WebSockets: CRÍTICO para Blazor Server (SignalR)
            "IIS-WebServerManagementTools", "IIS-ManagementConsole"
        )
        $result = Enable-WindowsOptionalFeature -Online -FeatureName $features -All -NoRestart
        Write-Host "Enable-WindowsOptionalFeature: RestartNeeded=$($result.RestartNeeded)"
    }

    if (-not (Test-Path "$env:windir\System32\inetsrv\appcmd.exe")) {
        throw "IIS no quedó instalado: no existe appcmd.exe."
    }
    Write-Host "IIS y WebSockets configurados correctamente."
}
catch {
    Write-Host "ERROR: $_"
    $exitCode = 1
}
finally {
    try { Stop-Transcript | Out-Null } catch { }
}
exit $exitCode
