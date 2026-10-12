$ErrorActionPreference = 'Stop'
while ($true) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'MotorLocalExus.Status.v1', [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $timeout = [Threading.CancellationTokenSource]::new(3000)
        $pipe.ConnectAsync($timeout.Token).GetAwaiter().GetResult()
        $buffer = New-Object byte[] 4096
        $json = [IO.MemoryStream]::new()
        while (($read = $pipe.Read($buffer, 0, $buffer.Length)) -gt 0) { $json.Write($buffer, 0, $read) }
        $status = [Text.Encoding]::UTF8.GetString($json.ToArray()) | ConvertFrom-Json
        Clear-Host
        Write-Host ("HEXODUS BioSync {0} | lector: {1} | auxiliar: {2} | detector: {3}" -f $status.Version, $status.ReaderConnected, $status.ReaderAgentConnected, $status.Detector)
        Write-Host ("Huellas: {0} | pendientes: {1} | ultimo evento: {2}" -f $status.Fingerprints, $status.Pending, $status.LastEvent)
        Write-Host ''
        $status.Logs | Select-Object -Last 30
    } catch { Write-Host "No se pudo leer el estado: $($_.Exception.Message)" -ForegroundColor Yellow }
    finally { if ($timeout) { $timeout.Dispose() }; $pipe.Dispose() }
    Start-Sleep -Seconds 2
}
