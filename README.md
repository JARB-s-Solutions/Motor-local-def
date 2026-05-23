# HexodusMotorLocal

Motor local de huella dactilar para Hexodus. Expone una API HTTP local en el puerto `4000`, usa el SDK `DPUruNet` para hablar con el lector Digital Persona y se integra con el frontend por `NEXT_PUBLIC_MOTOR_URL`.

## Que incluye esta carpeta

- Codigo fuente .NET 8 del motor.
- `DPUruNet.dll`, dependencia nativa requerida por el lector.
- Configuracion base en `appsettings.json`.
- `.gitignore` para evitar subir `bin/`, `obj/`, ejecutables y publicaciones pesadas.

La carpeta esta pensada para subirse a Git como codigo fuente limpio, sin arrastrar temporales de compilacion ni el publish `win-x64` pesado.

## Integracion con el frontend

El frontend usa:

```env
NEXT_PUBLIC_MOTOR_URL=http://localhost:4000
```

Archivos principales del frontend que hablan con el motor:

- `lib/motor-huella.ts`: sincroniza la cache de huellas con `POST /cargar-cache`.
- `app/asistencia/huella/page.tsx`: consulta `GET /estado` y usa el motor para captura/comparacion.
- `components/socios/captura-huella-modal.tsx`: usa `GET /enrolar` para registrar una huella.
- `app/api/asistencia/huella/callback/route.ts`: recibe eventos automaticos del motor.

El callback local recomendado para desarrollo es:

```json
"FrontendCallbackUrl": "http://localhost:3000/api/asistencia/huella/callback"
```

## Requisitos

- Windows x64.
- .NET 8 SDK o Runtime.
- Driver del lector Digital Persona instalado.
- Lector USB conectado.

## Ejecutar desde codigo fuente

Desde esta carpeta:

```powershell
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

El motor escucha por default en:

```text
http://localhost:4000
```

## Endpoints del motor

### `GET /`

Prueba basica de vida del motor.

### `GET /estado`

Devuelve estado del motor:

```json
{
  "cacheCargada": true,
  "huellasEnMemoria": 10,
  "lectorConectado": true,
  "detectorContinuo": "activo"
}
```

### `POST /cargar-cache`

Carga las huellas en memoria y precalienta el lector USB.

Body:

```json
{
  "baseDeDatos": [
    {
      "codigoSocio": "SOC-001",
      "huellaTemplate": "<Fmd>...</Fmd>"
    }
  ]
}
```

Respuesta:

```json
{
  "success": true,
  "totalCargadas": 1
}
```

### `POST /comparar`

Captura una huella, la compara contra la cache en memoria y responde en formato NDJSON.

Resultado exitoso:

```json
{ "tipo": "resultado", "success": true, "codigoSocio": "SOC-001" }
```

### `GET /enrolar`

Hace el enrolamiento de una huella nueva con 4 lecturas y devuelve el template XML.

Resultado exitoso:

```json
{ "tipo": "resultado", "success": true, "huellaTemplate": "<Fmd>...</Fmd>" }
```

## Flujo normal de uso

1. Levantar el frontend.
2. Levantar el motor local.
3. Entrar a la pantalla de huella en el frontend.
4. El frontend sincroniza huellas y llama `POST /cargar-cache`.
5. El motor queda escuchando el lector.
6. Cuando detecta una huella conocida, envia un `POST` al callback configurado.

## Subir a Git

Desde esta carpeta:

```powershell
git init
git add .
git commit -m "Add Hexodus local fingerprint motor"
git branch -M main
git remote add origin <URL_DEL_REPO>
git push -u origin main
```
