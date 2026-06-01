# Motor definitivo nuevo - Hexodus

Este repositorio contiene solamente el codigo necesario del motor local de huellas.

No incluye carpetas generadas como `bin`, `obj` ni `release-win-x64`. La idea es subir esta carpeta a GitHub para que cualquier desarrollador pueda clonarla, compilarla y ejecutarla en su propia PC.

## Que hace

- Expone el motor local en `http://localhost:4000`.
- Guarda eventos de huella en memoria local.
- Permite al frontend consultar `GET /eventos` sin pasar por Vercel/Render.
- Mantiene el enrolamiento y la comparacion biometrica existente.
- Deja `FrontendCallbackUrl` vacio para evitar callbacks serverless.

## Requisitos

- Windows 64 bits.
- .NET 8 SDK instalado.
- Driver del lector DigitalPersona/DPUruNet instalado.
- Lector USB conectado.

Verificar .NET:

```powershell
dotnet --version
```

## Ejecutar desde codigo fuente

Abrir PowerShell en esta carpeta y correr:

```powershell
dotnet run --urls http://0.0.0.0:4000
```

Si quieres probar en otro puerto:

```powershell
dotnet run --urls http://127.0.0.1:4055
```

Para detener el motor:

```txt
Ctrl + C
```

## Generar ejecutable para Windows

Desde esta carpeta:

```powershell
.\publicar-release.bat
```

O manualmente:

```powershell
dotnet publish .\HexodusMotorLocal.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\release-win-x64
```

Despues de publicar, el ejecutable queda en:

```txt
release-win-x64/HexodusMotorLocal.exe
```

## Ejecutar el release publicado

Despues de generar `release-win-x64`, puedes correr:

```powershell
.\ejecutar-motor.bat
```

O directo:

```powershell
cd .\release-win-x64
.\HexodusMotorLocal.exe
```

## Configuracion

Archivo:

```txt
appsettings.json
```

Configuracion esperada:

```json
{
  "Motor": {
    "FrontendCallbackUrl": "",
    "CooldownMs": 3000,
    "ListenUrl": "http://0.0.0.0:4000"
  }
}
```

No configures Vercel/Render en `FrontendCallbackUrl` si quieres evitar invocaciones serverless.

## Endpoints

- `GET /estado`: diagnostico del motor, cache, lector y eventos.
- `GET /eventos?after=0`: eventos locales detectados por el lector.
- `POST /cargar-cache`: carga huellas desde el frontend.
- `GET /enrolar`: captura una nueva huella.
- `POST /comparar`: compatibilidad/manual.

## Pruebas rapidas

Con el motor corriendo:

```powershell
Invoke-WebRequest http://localhost:4000/estado -UseBasicParsing
Invoke-WebRequest http://localhost:4000/eventos?take=0 -UseBasicParsing
```

En el navegador tambien puedes abrir:

```txt
http://localhost:4000/estado
http://localhost:4000/eventos?take=0
```

## Frontend

El frontend debe apuntar al motor local:

```txt
NEXT_PUBLIC_MOTOR_URL=http://localhost:4000
```

Si no se configura esa variable, el frontend actual usa `http://localhost:4000` por defecto.

En DevTools > Network, al filtrar `eventos`, deberias ver:

```txt
http://localhost:4000/eventos?after=...
```

No deberias ver polling repetido a:

```txt
/api/asistencia/huella/eventos
```

## Inicio automatico con Windows

Opcion simple:

1. Publicar el release con `.\publicar-release.bat`.
2. Presionar `Win + R`.
3. Escribir `shell:startup`.
4. Crear un acceso directo a `ejecutar-motor.bat`.

Opcion mas robusta:

- Usar el Programador de tareas de Windows.
- Trigger: al iniciar sesion.
- Accion: ejecutar `ejecutar-motor.bat`.
- Activar reinicio si falla.

## Notas de estabilidad

- El motor mantiene el lector abierto.
- Si el SDK reporta `DP_DEVICE_FAILURE`, el motor reinicia el lector.
- Los timeouts normales por no poner el dedo no reinician el lector.
- La cache de huellas se reemplaza de forma atomica para evitar lecturas a medias cuando el frontend sincroniza.
- El detector no se queda esperando si el lector esta ocupado por enrolamiento o captura manual.
