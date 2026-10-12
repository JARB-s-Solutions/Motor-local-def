param([string]$InnoCompiler)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$sdk = Join-Path $taskRoot '.tool-cache/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCommand) { $sdk = $dotnetCommand.Source }
}
if (-not $InnoCompiler) {
    $cachedInno = Join-Path $taskRoot '.tool-cache/inno/ISCC.exe'
    if (Test-Path -LiteralPath $cachedInno) { $InnoCompiler = $cachedInno }
    else {
        $innoCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($innoCommand) { $InnoCompiler = $innoCommand.Source }
    }
}
if (-not (Test-Path -LiteralPath $sdk)) { throw 'No se encontró .NET 8. Instala el SDK o deja dotnet en PATH.' }
if (-not (Test-Path -LiteralPath $InnoCompiler)) { throw 'No se encontró Inno Setup 6 (ISCC.exe). Instálalo o indica -InnoCompiler.' }
$buildRoot = Join-Path $taskRoot '.tool-cache/exus-build'
$payload = Join-Path $buildRoot 'payload'
Push-Location $taskRoot
try {
    & $sdk publish 'MotorLocalExus/Engine/MotorLocalExus.Service.csproj' -c Release -r win-x64 --self-contained true -o (Join-Path $payload 'Service') -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el servicio.' }
    & $sdk publish 'MotorLocalExus/Reader/MotorLocalExus.Reader.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $payload 'Reader')
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el auxiliar del lector.' }
    & $sdk publish 'MotorLocalExus/Desktop/MotorLocalExus.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $payload 'Desktop')
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar la interfaz.' }
    & $InnoCompiler /Qp ('/DPayload=' + $payload) ('/DOutputFolder=' + $taskRoot) 'MotorLocalExus/Installer/MotorLocalExus.iss'
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar el instalador.' }
    Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $taskRoot 'HEXODUS-BioSync-Setup.exe') | Select-Object Algorithm,Hash,Path
} finally { Pop-Location }
