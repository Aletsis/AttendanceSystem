# Script de Monitoreo del Servicio ZKTeco
# Ejecutar en segundo plano para monitorear el servicio continuamente

param(
    [string]$ServiceName = "AttendanceSystem.ZKTeco",
    [int]$CheckIntervalSeconds = 60,
    [int]$GrpcPort = 5001,
    [string]$DeviceIP = "192.168.1.100",
    [int]$DevicePort = 4370,
    [string]$LogPath = "C:\Logs\ZKTecoMonitor",
    [string]$EmailTo = "",
    [string]$EmailFrom = "",
    [string]$SmtpServer = ""
)

function Write-Log {
    param($Message, $Level = "INFO")
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    
    # Escribir en consola con color
    switch ($Level) {
        "ERROR" { Write-Host $logMessage -ForegroundColor Red }
        "WARNING" { Write-Host $logMessage -ForegroundColor Yellow }
        "SUCCESS" { Write-Host $logMessage -ForegroundColor Green }
        default { Write-Host $logMessage -ForegroundColor Cyan }
    }
    
    # Escribir en archivo de log
    if ($LogPath) {
        New-Item -Path $LogPath -ItemType Directory -Force | Out-Null
        $logFile = Join-Path $LogPath "monitor-$(Get-Date -Format 'yyyyMMdd').log"
        Add-Content -Path $logFile -Value $logMessage
    }
}

function Send-Alert {
    param($Subject, $Body)
    
    Write-Log "Enviando alerta: $Subject" "WARNING"
    
    if ($EmailTo -and $EmailFrom -and $SmtpServer) {
        try {
            Send-MailMessage -To $EmailTo `
                -From $EmailFrom `
                -Subject "ALERTA: $Subject" `
                -Body $Body `
                -SmtpServer $SmtpServer `
                -ErrorAction Stop
            
            Write-Log "Alerta enviada por email" "SUCCESS"
        }
        catch {
            Write-Log "Error al enviar email: $($_.Exception.Message)" "ERROR"
        }
    }
    
    # Aquí podrías agregar otras formas de notificación:
    # - SMS
    # - Slack
    # - Teams
    # - Telegram
    # etc.
}

function Test-ServiceHealth {
    $issues = @()
    
    # 1. Verificar que el servicio está corriendo
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    
    if ($null -eq $service) {
        $issues += "El servicio '$ServiceName' no existe"
        Write-Log "El servicio no existe" "ERROR"
        return $issues
    }
    
    if ($service.Status -ne "Running") {
        $issues += "El servicio está en estado: $($service.Status)"
        Write-Log "Servicio no está corriendo: $($service.Status)" "ERROR"
        
        # Intentar reiniciar
        Write-Log "Intentando reiniciar el servicio..." "WARNING"
        try {
            Start-Service -Name $ServiceName -ErrorAction Stop
            Start-Sleep -Seconds 5
            
            $service = Get-Service -Name $ServiceName
            if ($service.Status -eq "Running") {
                Write-Log "Servicio reiniciado exitosamente" "SUCCESS"
                Send-Alert "Servicio ZKTeco reiniciado" "El servicio se detuvo y fue reiniciado automáticamente a las $(Get-Date)"
            }
            else {
                $issues += "No se pudo reiniciar el servicio"
                Write-Log "No se pudo reiniciar el servicio" "ERROR"
            }
        }
        catch {
            $issues += "Error al reiniciar: $($_.Exception.Message)"
            Write-Log "Error al reiniciar: $($_.Exception.Message)" "ERROR"
        }
    }
    else {
        Write-Log "Servicio OK: Running" "SUCCESS"
    }
    
    # 2. Verificar puerto gRPC
    $port = Get-NetTCPConnection -LocalPort $GrpcPort -State Listen -ErrorAction SilentlyContinue
    
    if ($null -eq $port) {
        $issues += "Puerto gRPC $GrpcPort no está escuchando"
        Write-Log "Puerto gRPC $GrpcPort no está escuchando" "ERROR"
    }
    else {
        Write-Log "Puerto gRPC $GrpcPort OK" "SUCCESS"
    }
    
    # 3. Verificar conectividad con dispositivo ZKTeco
    if ($DeviceIP) {
        $deviceConnection = Test-NetConnection -ComputerName $DeviceIP -Port $DevicePort -WarningAction SilentlyContinue -ErrorAction SilentlyContinue
        
        if (-not $deviceConnection.TcpTestSucceeded) {
            $issues += "No se puede conectar al dispositivo ZKTeco en $DeviceIP:$DevicePort"
            Write-Log "No se puede conectar al dispositivo ZKTeco" "WARNING"
        }
        else {
            Write-Log "Conectividad con dispositivo ZKTeco OK" "SUCCESS"
        }
    }
    
    # 4. Verificar uso de memoria
    $process = Get-Process | Where-Object { $_.ProcessName -like "*AttendanceSystem.ZKTeco*" } | Select-Object -First 1
    
    if ($process) {
        $memoryMB = [math]::Round($process.WorkingSet64 / 1MB, 2)
        Write-Log "Uso de memoria: $memoryMB MB" "INFO"
        
        # Alerta si usa más de 500 MB
        if ($memoryMB -gt 500) {
            $issues += "Uso de memoria alto: $memoryMB MB"
            Write-Log "Uso de memoria alto: $memoryMB MB" "WARNING"
        }
    }
    
    # 5. Verificar logs de errores recientes
    $logsPath = "C:\Services\AttendanceSystem.ZKTeco\logs"
    if (Test-Path $logsPath) {
        $recentErrors = Get-ChildItem -Path $logsPath -Filter "*.log" | 
        Get-Content | 
        Select-String -Pattern "ERROR|FATAL|Exception" | 
        Select-Object -Last 5
        
        if ($recentErrors) {
            Write-Log "Se encontraron errores recientes en los logs" "WARNING"
            # No agregar a issues para no saturar alertas
        }
    }
    
    return $issues
}

# Inicio del monitoreo
Write-Log "=== Iniciando monitoreo del servicio ZKTeco ===" "INFO"
Write-Log "Servicio: $ServiceName" "INFO"
Write-Log "Intervalo de verificación: $CheckIntervalSeconds segundos" "INFO"
Write-Log "Puerto gRPC: $GrpcPort" "INFO"
Write-Log "Dispositivo ZKTeco: $DeviceIP:$DevicePort" "INFO"

if ($EmailTo) {
    Write-Log "Alertas por email habilitadas: $EmailTo" "INFO"
}

Write-Log "Presiona Ctrl+C para detener el monitoreo" "INFO"
Write-Log "" "INFO"

$consecutiveFailures = 0
$lastAlertTime = $null

while ($true) {
    try {
        $issues = Test-ServiceHealth
        
        if ($issues.Count -gt 0) {
            $consecutiveFailures++
            
            Write-Log "Se encontraron $($issues.Count) problema(s)" "WARNING"
            foreach ($issue in $issues) {
                Write-Log "  - $issue" "WARNING"
            }
            
            # Enviar alerta solo si hay 3 fallos consecutivos y han pasado al menos 15 minutos desde la última alerta
            if ($consecutiveFailures -ge 3) {
                $shouldAlert = $false
                
                if ($null -eq $lastAlertTime) {
                    $shouldAlert = $true
                }
                else {
                    $minutesSinceLastAlert = ((Get-Date) - $lastAlertTime).TotalMinutes
                    if ($minutesSinceLastAlert -ge 15) {
                        $shouldAlert = $true
                    }
                }
                
                if ($shouldAlert) {
                    $alertBody = "Se detectaron los siguientes problemas con el servicio ZKTeco:`n`n"
                    $alertBody += ($issues -join "`n")
                    $alertBody += "`n`nFecha: $(Get-Date)"
                    $alertBody += "`nServidor: $env:COMPUTERNAME"
                    
                    Send-Alert "Problemas con servicio ZKTeco" $alertBody
                    $lastAlertTime = Get-Date
                }
            }
        }
        else {
            if ($consecutiveFailures -gt 0) {
                Write-Log "Servicio recuperado después de $consecutiveFailures fallo(s)" "SUCCESS"
                
                if ($consecutiveFailures -ge 3) {
                    Send-Alert "Servicio ZKTeco recuperado" "El servicio se ha recuperado y está funcionando normalmente."
                }
            }
            
            $consecutiveFailures = 0
            Write-Log "Todos los checks OK" "SUCCESS"
        }
        
    }
    catch {
        Write-Log "Error durante el monitoreo: $($_.Exception.Message)" "ERROR"
    }
    
    Write-Log "Próxima verificación en $CheckIntervalSeconds segundos..." "INFO"
    Write-Log "" "INFO"
    
    Start-Sleep -Seconds $CheckIntervalSeconds
}
