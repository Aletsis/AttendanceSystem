$tlbimpPath = "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\TlbImp.exe"
$dllPath = "src\Infrastructure\AttendanceSystem.ZKTeco\lib\zkemkeeper.dll"
$outPath = "src\Infrastructure\AttendanceSystem.ZKTeco\lib\Interop.zkemkeeper.dll"

Write-Host "Generando assembly de interoperabilidad..."
Write-Host "DLL origen: $dllPath"
Write-Host "DLL destino: $outPath"

& $tlbimpPath $dllPath /out:$outPath /namespace:zkemkeeper /verbose

if ($LASTEXITCODE -eq 0) {
    Write-Host "Assembly de interoperabilidad generado exitosamente!" -ForegroundColor Green
} else {
    Write-Host "Error al generar el assembly de interoperabilidad. Código: $LASTEXITCODE" -ForegroundColor Red
}
