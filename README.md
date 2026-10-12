# HEXODUS BioSync

Motor local de huellas para Windows 11 x64. La versión de producción está en `MotorLocalExus/` e incluye servicio Windows, auxiliar del lector, ventana de estado, instalador y pruebas aisladas.

## Descarga directa

- `HEXODUS-BioSync-Setup.exe`: instalador compilado para el cliente.
- `HEXODUS-BioSync-Setup.sha256`: hash SHA-256 para verificar que el instalador no fue alterado.

El instalador se publica mediante Git LFS porque supera el límite normal de archivos de GitHub.

## Instalación

1. Ejecuta `HEXODUS-BioSync-Setup.exe` como administrador.
2. La detección del lector es opcional; el controlador DigitalPersona se instala por separado.
3. Configura una sola vez la dirección del frontend HEXODUS, el identificador de terminal y la clave de comunicación.
4. El servicio queda configurado para iniciar con Windows y espera la pantalla de asistencia por SSE en `localhost:4000`.
5. Abre HEXODUS y usa los socios reales de la base de datos. El motor no crea datos ficticios ni modifica el esquema de Supabase.

La ventana de estado se llama **HEXODUS BioSync**, muestra el lector, la pantalla, la caché, pendientes, último evento y logs. Cerrarla solo la oculta; no detiene el servicio.

## Código y compilación

La guía completa está en [`LEEME-MotorLocalExus.md`](LEEME-MotorLocalExus.md). Para generar el instalador desde Windows con .NET 8 e Inno Setup 6 disponibles (dotnet e ISCC.exe en PATH, o instalados en .tool-cache):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\compilar-motor-exus.ps1
```

El código de `MotorLocalExus/` conserva los identificadores internos y las rutas de datos de instalaciones anteriores para permitir actualizaciones sin perder configuración ni cola local.

## Alcance de esta versión

- Comunicación de asistencia mediante SSE, sin polling continuo del frontend.
- Caché local de socios con huella y cola durable de eventos.
- Arranque automático y recuperación del servicio de Windows.
- Lector opcional durante la instalación; puede conectarse después.
- Icono `HEXODUS_BioSync.ico` integrado en la aplicación y el instalador.
- Pruebas aisladas sin insertar datos en Supabase.

El código histórico del motor que está en la raíz se conserva para referencia; `MotorLocalExus/` es el paquete recomendado para producción.
