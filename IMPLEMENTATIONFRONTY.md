📘 Guía de Migración: Kiosco de Huellas a Arquitectura WebSocket
🎯 Contexto y Objetivo
Actualmente, la pantalla de "Asistencia por Huella" (app/asistencia/huella/page.tsx) está haciendo un polling agresivo (peticiones HTTP cada 750ms) hacia una ruta Serverless en Vercel (/api/asistencia/huella/eventos). Esto está consumiendo millones de invocaciones innecesarias de nuestra capa gratuita, incluso cuando nadie está usando el lector.

La Solución: Hemos actualizado el "Motor Local" (el programa C# que corre en la computadora física de recepción) para que funcione como un servidor WebSocket.
A partir de ahora, el Frontend no le preguntará a Vercel si hay eventos. Simplemente abrirá una conexión persistente directa con el Motor Local, y este le "empujará" (Push) la información en tiempo real (0ms) cuando se detecte una huella.

⚙️ Paso 1: Configurar Variables de Entorno
Asegúrate de que el Kiosco sepa cómo encontrar al motor local. En tu archivo .env.local (y en Vercel), valida que exista esta variable:

Fragmento de código
# URL base del motor local (se convertirá automáticamente a ws:// en el código)
NEXT_PUBLIC_MOTOR_URL=http://localhost:4000
(Nota: Al usar localhost, el navegador web en recepción buscará el motor en la misma computadora física donde está abierta la página, evitando problemas de CORS).

🗑️ Paso 2: Limpieza de Código Obsoleto (Polling)
Ve a tu vista principal del kiosco (app/asistencia/huella/page.tsx) y elimina completamente la lógica antigua.
Ya NO necesitamos:

Funciones como consultarEventosMotor() o programarPollingEventos().

Constantes de tiempo como EVENT_POLL_INTERVAL_MS = 750.

Cualquier setInterval o setTimeout que haga fetch hacia /api/asistencia/huella/eventos.

💻 Paso 3: Nueva Implementación (React useEffect)
Reemplaza tu lógica de escucha con este nuevo useEffect. Está diseñado para conectarse, escuchar en silencio absoluto (sin consumir red) y auto-reconectarse si el motor local se apaga o reinicia.

TypeScript
import { useEffect, useState, useRef } from 'react';

export default function HuellaKioscoPage() {
  // Estados para dar feedback visual al recepcionista
  const [conexionEstado, setConexionEstado] = useState<'conectando' | 'conectado' | 'desconectado'>('conectando');
  const [ultimoSocio, setUltimoSocio] = useState<string | null>(null);

  // Referencia para evitar múltiples conexiones por el StrictMode de React
  const wsRef = useRef<WebSocket | null>(null);

  useEffect(() => {
    let reconnectTimer: NodeJS.Timeout;

    const conectarWebSocket = () => {
      // 1. Transformar la URL http:// a ws:// dinámicamente
      const motorUrl = process.env.NEXT_PUBLIC_MOTOR_URL || 'http://localhost:4000';
      const wsUrl = motorUrl.replace(/^http/, 'ws') + '/ws/eventos';

      // 2. Instanciar la conexión WebSocket
      const ws = new WebSocket(wsUrl);
      wsRef.current = ws;

      // 3. Evento: Conexión Exitosa
      ws.onopen = () => {
        console.log("✅ Conectado al Motor de Huellas Local");
        setConexionEstado('conectado');
      };

      // 4. Evento: Recepción de Huella (Push desde el C#)
      ws.onmessage = async (event) => {
        try {
          const data = JSON.parse(event.data);
          
          // Verificar que sea un match válido
          if (data.success && data.codigoSocio) {
            console.log("🚀 ¡Huella Detectada! Socio:", data.codigoSocio);
            setUltimoSocio(data.codigoSocio);
            
            // 🔥 AQUÍ VAMOS A VERCEL: Registramos la asistencia real
            await registrarAsistenciaEnBackend(data.codigoSocio);
          }
        } catch (error) {
          console.error("Error parseando el evento de huella:", error);
        }
      };

      // 5. Evento: Desconexión (Ej. Reiniciaron la PC o cerraron el motor C#)
      ws.onclose = () => {
        console.warn("⚠️ Desconectado del motor local. Reintentando en 3s...");
        setConexionEstado('desconectado');
        // Auto-reconexión silenciosa
        reconnectTimer = setTimeout(conectarWebSocket, 3000);
      };

      // 6. Evento: Error
      ws.onerror = (err) => {
        console.error("❌ Error de WebSocket:", err);
        ws.close(); // Fuerza a ejecutar onclose para reintentar
      };
    };

    // Iniciar conexión al montar el componente
    conectarWebSocket();

    // 🧹 Cleanup: Cerrar el socket limpiamente al salir de la página
    return () => {
      clearTimeout(reconnectTimer);
      if (wsRef.current) {
        wsRef.current.close();
      }
    };
  }, []); // Array vacío para ejecutar solo 1 vez

  // Función que ya tienes para registrar en Supabase/Backend
  const registrarAsistenciaEnBackend = async (codigoSocio: string) => {
    try {
      // Esta es la ÚNICA petición que irá a los servidores de Vercel ahora
      const response = await fetch('/api/asistencia/huellas/validar', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ codigoSocio }),
      });
      // ... tu lógica de éxito, abrir modal, reproducir sonido, etc.
    } catch (error) {
      console.error("Error al registrar asistencia:", error);
    }
  };

  return (
    <div className="p-4">
      {/* UI de Feedback sugerida */}
      <div className="mb-4 font-bold">
        {conexionEstado === 'conectado' && <span className="text-green-500">🟢 Motor Local Listo: Esperando huella...</span>}
        {conexionEstado === 'conectando' && <span className="text-yellow-500">🟡 Conectando al lector biométrico...</span>}
        {conexionEstado === 'desconectado' && <span className="text-red-500">🔴 Motor Apagado. Reconectando...</span>}
      </div>
      
      {/* Resto de tu UI */}
    </div>
  );
}
🧹 Paso 4: Eliminar Archivos Obsoletos (Limpieza de Repositorio)
Ya que hemos eliminado a Vercel como "puente" para estar preguntando por los eventos locales, las siguientes rutas Serverless (API Routes) ya no tienen ninguna utilidad. Puedes borrarlas de tu código fuente:

❌ Eliminar carpeta entera: app/api/asistencia/huella/eventos/

❌ Eliminar carpeta entera: app/api/asistencia/huella/callback/

❌ Eliminar archivo (si aplica): lib/asistencia-huella-events.ts

🚦 Paso 5: QA y Criterios de Aceptación (Checklist)
Antes de hacer el Merge o paso a Producción, verifica esto en tu navegador (con el Motor C# corriendo en local):

[ ] Abre la pestaña "Network" (Red) en las DevTools de Chrome: Asegúrate de tener el filtro en "All" o "WS". No deberías ver un spam de peticiones ejecutándose repetitivamente. La red debe estar en completo silencio después de la carga inicial.

[ ] Busca la conexión WebSocket: En la pestaña Network, debes ver un solo request con status 101 Switching Protocols apuntando a /ws/eventos.

[ ] Prueba de Auto-Recuperación: Cierra el ejecutable del motor C# en tu computadora. El frontend debe mostrar "🔴 Motor Apagado". Vuelve a abrir el motor C#. El frontend debe volver automáticamente a "🟢 Motor Local Listo" sin que tengas que refrescar la página.

[ ] Prueba de Asistencia Real: Coloca tu dedo en el lector físico. Revisa en la pestaña "Network" que solamente se dispare una petición POST hacia /api/asistencia/huellas/validar.