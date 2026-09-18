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

function Download-FileWithRetry {
    param(
        [Parameter(Mandatory=$true)][string[]]$Urls,
        [Parameter(Mandatory=$true)][string]$OutputFile,
        [int]$MaxRetriesPerUrl = 3,
        [int]$DelaySeconds = 4,
        [long]$MinSizeBytes = 10485760 # 10 MB mínimo requerido para evitar páginas de error HTML guardadas como exe
    )

    $targetDir = Split-Path -Parent $OutputFile
    if ($targetDir -and -not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    $curlCmd = Get-Command "curl.exe" -ErrorAction SilentlyContinue
    if (-not $curlCmd) {
        $curlCmd = Get-Command "curl" -ErrorAction SilentlyContinue
    }

    $success = $false

    foreach ($url in $Urls) {
        for ($attempt = 1; $attempt -le $MaxRetriesPerUrl; $attempt++) {
            Write-Host "Intento $attempt/$MaxRetriesPerUrl descargando desde $url..." -ForegroundColor Cyan
            
            if (Test-Path $OutputFile) {
                Remove-Item $OutputFile -Force -ErrorAction SilentlyContinue
            }

            try {
                if ($curlCmd) {
                    & $curlCmd.Source -fSL --retry 3 --retry-delay 3 --connect-timeout 30 -o "$OutputFile" "$url"
                    if ($LASTEXITCODE -ne 0) {
                        throw "curl finalizó con código de error $LASTEXITCODE"
                    }
                } else {
                    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
                    Invoke-WebRequest -Uri $url -OutFile $OutputFile -UserAgent "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" -TimeoutSec 180
                }

                if (Test-Path $OutputFile) {
                    $fileSize = (Get-Item $OutputFile).Length
                    if ($fileSize -ge $MinSizeBytes) {
                        $sizeMb = [math]::Round($fileSize / 1MB, 2)
                        Write-Host "Descarga exitosa ($sizeMb MB): $OutputFile" -ForegroundColor Green
                        $success = $true
                        break
                    } else {
                        Write-Warning "El archivo descargado es demasiado pequeño ($fileSize bytes). Posible error del servidor (502/Gateway)."
                        Remove-Item $OutputFile -Force -ErrorAction SilentlyContinue
                    }
                } else {
                    Write-Warning "El archivo de destino no fue generado."
                }
            } catch {
                Write-Warning "Error en el intento ${attempt}: $_"
                if (Test-Path $OutputFile) {
                    Remove-Item $OutputFile -Force -ErrorAction SilentlyContinue
                }
            }

            if (-not $success -and $attempt -lt $MaxRetriesPerUrl) {
                Write-Host "Esperando $DelaySeconds segundos antes de reintentar..." -ForegroundColor Yellow
                Start-Sleep -Seconds $DelaySeconds
            }
        }

        if ($success) {
            break
        } else {
            Write-Warning "No se pudo descargar desde $url. Probando siguiente URL si está disponible..."
        }
    }

    if (-not $success) {
        throw "ERROR FATAL: No se pudo descargar el archivo '$OutputFile' desde ninguna de las URLs provistas."
    }
}

# 1. Descargar PostgreSQL 16.x x64 si no existe
if (-not (Test-Path $PostgresTarget) -or $Force -or ((Get-Item $PostgresTarget -ErrorAction SilentlyContinue).Length -lt 10MB)) {
    Write-Host "Descargando instalador de PostgreSQL 16.x..." -ForegroundColor Cyan
    $pgUrls = @(
        "https://sbp.enterprisedb.com/getfile.jsp?fileid=1258894"
    )
    Download-FileWithRetry -Urls $pgUrls -OutputFile $PostgresTarget -MinSizeBytes 50MB
    Write-Host "PostgreSQL listo en $PostgresTarget" -ForegroundColor Green
} else {
    Write-Host "PostgreSQL installer ya existe y es válido en $PostgresTarget" -ForegroundColor Yellow
}

# 2. Descargar ASP.NET Core Hosting Bundle si no existe
if (-not (Test-Path $DotNetTarget) -or $Force -or ((Get-Item $DotNetTarget -ErrorAction SilentlyContinue).Length -lt 10MB)) {
    Write-Host "Descargando ASP.NET Core Hosting Bundle..." -ForegroundColor Cyan
    $dotnetUrls = @(
        "https://aka.ms/dotnet/9.0/dotnet-hosting-win.exe",
        "https://download.visualstudio.microsoft.com/download/pr/97738f6d-31ca-43bc-9387-a246d6b2c2fa/e36928da5e1b7e6515b804cb3603d36b/dotnet-hosting-9.0.2-win.exe"
    )
    Download-FileWithRetry -Urls $dotnetUrls -OutputFile $DotNetTarget -MinSizeBytes 20MB
    Write-Host "ASP.NET Core Hosting Bundle listo en $DotNetTarget" -ForegroundColor Green
} else {
    Write-Host "ASP.NET Core Hosting Bundle ya existe y es válido en $DotNetTarget" -ForegroundColor Yellow
}

Write-Host "Todos los prerrequisitos están listos en $DeployDir." -ForegroundColor Green
