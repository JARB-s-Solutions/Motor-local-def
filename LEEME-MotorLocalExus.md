# HEXODUS BioSync - instalador 2.2.0

## Instalacion

1. Abre **HEXODUS-BioSync-Setup.exe** y acepta el permiso de administrador de Windows. Ese permiso se solicita para instalar y configurar el servicio; no se repite al encender la PC.
2. En la comprobacion del lector selecciona **No / Omitir** si el dispositivo aun no esta conectado. **Si / Buscar lector** solo informa si Windows lo detecta; el controlador se instala por separado.
3. La configuracion inicial puede cargar **MotorLocalExus.exusconfig** si existe junto al instalador. Pulsa **Guardar e iniciar motor**. En otra PC usa la direccion de HEXODUS, el identificador de terminal y la clave autorizada para esa instalacion.
4. Abre **HEXODUS BioSync** desde Inicio o el acceso directo. La ventana muestra el servicio, el auxiliar del lector, la pantalla, la cantidad de huellas, pendientes, ultimo evento y logs.
5. Abre HEXODUS y entra a la pantalla de asistencia. Usa los socios existentes de la base de datos; el motor no crea datos ficticios ni modifica la estructura de Supabase.

El motor se inicia con Windows y queda esperando la pantalla de asistencia. El auxiliar del lector se inicia en la sesion del usuario para que el controlador USB funcione correctamente. Cerrar la ventana de estado no detiene el servicio.

**MotorLocalExus.exusconfig es privado y esta cifrado para una computadora.** No compartas ese archivo como configuracion universal. Si no hay clave, el servicio queda instalado y visible, pero no registra asistencias autenticadas.

## Que instala

- Windows 11 x64 Intel/AMD; no Windows ARM.
- Programa en `C:\Program Files\Motor Local Exus` (el producto se muestra como HEXODUS BioSync).
- Configuracion protegida, cola y logs en `C:\ProgramData\Motor Local Exus\private`.
- Servicio `MotorLocalExus` con inicio automatico y cuenta limitada `LocalService`.
- Auxiliar interactivo del lector en la sesion de Windows, con reconexion automatica.
- Recuperacion del servicio a los 5, 15 y 60 segundos tras un fallo.
- Comunicacion de asistencia por SSE en `localhost:4000` y diagnostico por canal local de Windows.
- Entorno .NET incluido. No se necesita Node, Visual Studio ni instalar .NET.
- El controlador DigitalPersona se instala por separado. El motor puede instalarse sin lector.

## Desinstalar

En Configuracion de Windows -> Aplicaciones -> HEXODUS BioSync, o desde el acceso de desinstalacion en Inicio. Requiere administrador para eliminar el servicio.

Elimina programa, inicio automatico, configuracion, cola y logs del producto. Si hay eventos pendientes, solicita confirmacion antes de eliminarlos. Conserva el driver, Supabase, frontend, backend y las carpetas de desarrollo.

## Compilacion interna

El codigo del instalador esta en `MotorLocalExus`. Para generar el paquete desde la carpeta raiz:

`powershell -NoProfile -File tools\compilar-motor-exus.ps1`

Las pruebas automatizadas usan carpetas aisladas y no insertan socios ni asistencias en la base de datos.
