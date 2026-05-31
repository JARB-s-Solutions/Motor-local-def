using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using DPUruNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options => {
    options.AddPolicy("PermitirTodo", policy => {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddHttpClient();
builder.Services.AddHostedService<DeteccionContinuaService>();

var app = builder.Build();
app.UseCors("PermitirTodo");

// ACTIVAR SOPORTE PARA WEBSOCKETS EN EL SERVIDOR
app.UseWebSockets();

async Task EnviarMensaje(HttpContext ctx, string texto) {
    Console.WriteLine(texto);
    var json = JsonSerializer.Serialize(new { tipo = "mensaje", texto = texto });
    await ctx.Response.WriteAsync(json + "\n");
    await ctx.Response.Body.FlushAsync();
}

app.MapGet("/", () => "¡Motor DPUruNet Moderno Activo, Escuchando y con WebSockets!");

app.MapGet("/estado", () => Results.Ok(new {
    cacheCargada = MemoriaHuellas.EstaCargada,
    huellasEnMemoria = MemoriaHuellas.ListaFmds.Count,
    lectorConectado = MotorBiometrico.LectorConectado,
    detectorContinuo = "activo",
    kioscosConectados = WebSocketHandler.ConexionesActivas // Nuevo indicador
}));

// ========================================================
// ENDPOINT: WEBSOCKET (El Portero de Kioscos)
// ========================================================
app.Map("/ws/eventos", async context => {
    if (context.WebSockets.IsWebSocketRequest) {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        WebSocketHandler.AddSocket(webSocket);
        Console.WriteLine($"[WEBSOCKET] 🟢 Nuevo Kiosco conectado. Total: {WebSocketHandler.ConexionesActivas}");
        
        var buffer = new byte[1024 * 4];
        try {
            // Mantiene la conexión viva y abierta escuchando
            while (webSocket.State == System.Net.WebSockets.WebSocketState.Open) {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
            }
        } catch { 
            // Se ignora si el Kiosco cierra la pestaña de golpe
        } finally {
            WebSocketHandler.RemoveSocket(webSocket);
            Console.WriteLine($"[WEBSOCKET] 🔴 Kiosco desconectado. Total: {WebSocketHandler.ConexionesActivas}");
        }
    } else {
        context.Response.StatusCode = 400; // Bad Request si intentan entrar por HTTP normal
    }
});

// ========================================================
// ENDPOINT: Cargar Huellas y "Calentar" el USB
// ========================================================
app.MapPost("/cargar-cache", ([FromBody] PeticionMatch request) => {
    MemoriaHuellas.ListaFmds.Clear();
    MemoriaHuellas.IndexToCodigoSocio.Clear();
    
    int index = 0;
    foreach (var socio in request.BaseDeDatos) {
        if (string.IsNullOrEmpty(socio.HuellaTemplate)) continue;
        try {
            Fmd fmdDb = Fmd.DeserializeXml(socio.HuellaTemplate);
            MemoriaHuellas.ListaFmds.Add(fmdDb);
            MemoriaHuellas.IndexToCodigoSocio[index] = socio.CodigoSocio;
            index++;
        } catch { continue; }
    }
    
    MemoriaHuellas.EstaCargada = true;
    MotorBiometrico.InicializarLector();
    
    Console.WriteLine($"\n[SISTEMA] Se cargaron {index} huellas. Motor USB pre-calentado.");
    return Results.Ok(new { success = true, totalCargadas = index });
});

// ========================================================
// ENDPOINT: COMPARAR (Lector Always-On)
// ========================================================
app.MapPost("/comparar", async (HttpContext ctx) => {
    ctx.Response.ContentType = "application/x-ndjson";
    try {
        if (!MemoriaHuellas.EstaCargada || MemoriaHuellas.ListaFmds.Count == 0) {
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { tipo = "resultado", success = false, message = "Sin huellas en memoria." }) + "\n");
            return;
        }

        await EnviarMensaje(ctx, "Ponga el dedo en el lector...");
        Fmd? fmdCapturado = await MotorBiometrico.CapturarHuellaAsync(async (msg) => await EnviarMensaje(ctx, msg));
        
        if (fmdCapturado == null) {
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { tipo = "resultado", success = false, message = "Lectura fallida o cancelada." }) + "\n");
            return;
        }

        IdentifyResult identifyResult = Comparison.Identify(fmdCapturado, 0, MemoriaHuellas.ListaFmds, 21474, 1);

        if (identifyResult.ResultCode == Constants.ResultCode.DP_SUCCESS && identifyResult.Indexes.Length > 0) {
            string codigoSocioMatch = MemoriaHuellas.IndexToCodigoSocio[identifyResult.Indexes[0][0]];
            await EnviarMensaje(ctx, $"🎉 ¡Match biométrico! Código: {codigoSocioMatch}");
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { tipo = "resultado", success = true, codigoSocio = codigoSocioMatch }) + "\n");
            return;
        }
        
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { tipo = "resultado", success = false, message = "Socio no reconocido." }) + "\n");

    } catch (Exception ex) {
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { tipo = "resultado", success = false, message = ex.Message }) + "\n");
    }
});

// ========================================================
// ENDPOINT: ENROLAR (Lector Always-On)
// ========================================================
app.MapGet("/enrolar", async (HttpContext ctx) => {
    ctx.Response.ContentType = "application/x-ndjson";
    try {
        await EnviarMensaje(ctx, "Iniciando enrolamiento...");
        Fmd? enrolamiento = await MotorBiometrico.EnrolarSocioAsync(async (msg) => await EnviarMensaje(ctx, msg));
        
        if (enrolamiento != null) {
            string xmlHuella = Fmd.SerializeXml(enrolamiento);
            await EnviarMensaje(ctx, "¡Huella maestra creada exitosamente!");
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { tipo = "resultado", success = true, huellaTemplate = xmlHuella }) + "\n");
        } else {
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { tipo = "resultado", success = false, message = "Se canceló o falló el registro." }) + "\n");
        }
    } catch (Exception ex) {
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { tipo = "resultado", success = false, message = ex.Message }) + "\n");
    }
});

string listenUrl = builder.Configuration["Motor:ListenUrl"] ?? "http://0.0.0.0:4000";
Console.WriteLine($"[SISTEMA] Motor escuchando en: {listenUrl}");
app.Run(listenUrl);

// ========================================================
// CLASE: WEBSOCKET HANDLER (El Administrador de Push)
// ========================================================
public static class WebSocketHandler {
    private static readonly List<System.Net.WebSockets.WebSocket> Sockets = new();
    private static readonly object Lock = new();

    public static int ConexionesActivas { get { lock (Lock) return Sockets.Count; } }

    public static void AddSocket(System.Net.WebSockets.WebSocket socket) { lock (Lock) Sockets.Add(socket); }
    public static void RemoveSocket(System.Net.WebSockets.WebSocket socket) { lock (Lock) Sockets.Remove(socket); }

    public static async Task BroadcastAsync(object data) {
        // Aseguramos que el JSON se envíe en CamelCase (codigoSocio) para que JavaScript lo lea nativamente
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(data, options);
        var buffer = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(buffer);

        List<System.Net.WebSockets.WebSocket> currentSockets;
        lock (Lock) { currentSockets = Sockets.ToList(); }

        foreach (var socket in currentSockets) {
            if (socket.State == System.Net.WebSockets.WebSocketState.Open) {
                try {
                    await socket.SendAsync(segment, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
                } catch { 
                    // Ignoramos fallos individuales de red para no detener el broadcast a otros kioscos
                }
            }
        }
    }
}

// ========================================================
// MODELOS DE DATOS
// ========================================================
public class PeticionMatch { public List<SocioDb> BaseDeDatos { get; set; } = new List<SocioDb>(); }
public class SocioDb { public string CodigoSocio { get; set; } = ""; public string HuellaTemplate { get; set; } = ""; }
public class EventoHuella {
    public string CodigoSocio { get; set; } = "";
    public DateTime FechaUtc { get; set; }
    public string Origen { get; set; } = "lector-automatico";
}

// ========================================================
// MEMORIA RAM DEL MOTOR (CACHÉ ULTRA RÁPIDO)
// ========================================================
public static class MemoriaHuellas {
    public static List<Fmd> ListaFmds = new List<Fmd>();
    public static Dictionary<int, string> IndexToCodigoSocio = new Dictionary<int, string>();
    public static bool EstaCargada = false;
}

// ========================================================
// CONTROLADOR DEL HARDWARE DPUruNet (ALWAYS-ON)
// ========================================================
public static class MotorBiometrico {
    private static Reader? _lectorGlobal = null;
    private static readonly SemaphoreSlim _lectorLock = new(1, 1);
    public static bool LectorConectado => _lectorGlobal != null;

    public static void InicializarLector() {
        try {
            if (_lectorGlobal == null) {
                ReaderCollection readers = ReaderCollection.GetReaders();
                if (readers.Count > 0) {
                    _lectorGlobal = readers[0];
                    _lectorGlobal.Open(Constants.CapturePriority.DP_PRIORITY_EXCLUSIVE);
                    Console.WriteLine("[HARDWARE] Lector USB conectado y en espera permanente.");
                }
            }
        } catch (Exception ex) {
            Console.WriteLine("[HARDWARE] Error USB: " + ex.Message);
        }
    }

    public static void ReiniciarLector() {
        if (_lectorGlobal != null) {
            try { _lectorGlobal.Dispose(); } catch { }
            _lectorGlobal = null;
        }
        InicializarLector();
    }

    public static async Task<Fmd?> CapturarHuellaAsync(Func<string, Task> onMessage) {
        await _lectorLock.WaitAsync();
        try {
            if (_lectorGlobal == null) InicializarLector();
            if (_lectorGlobal == null) { 
                await onMessage("Error: Lector no encontrado. Conecte el USB."); 
                return null; 
            }

            Fmd? fmdFinal = null;
            CaptureResult captureResult = _lectorGlobal.Capture(Constants.Formats.Fid.ANSI, Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT, 15000, _lectorGlobal.Capabilities.Resolutions[0]);
            
            if (captureResult.ResultCode == Constants.ResultCode.DP_DEVICE_FAILURE) {
                await onMessage("Lector desconectado. Reiniciando...");
                ReiniciarLector();
                return null;
            }

            if (captureResult.ResultCode == Constants.ResultCode.DP_SUCCESS && captureResult.Quality == Constants.CaptureQuality.DP_QUALITY_GOOD) {
                DataResult<Fmd> fmdResult = FeatureExtraction.CreateFmdFromFid(captureResult.Data, Constants.Formats.Fmd.ANSI);
                if (fmdResult.ResultCode == Constants.ResultCode.DP_SUCCESS) fmdFinal = fmdResult.Data;
            } else if (captureResult.ResultCode != Constants.ResultCode.DP_SUCCESS) {
                await onMessage($"Error de lectura o tiempo agotado.");
            }
            
            return fmdFinal;
        } finally {
            _lectorLock.Release();
        }
    }

    public static async Task<Fmd?> CapturarHuellaSilenciosaAsync(CancellationToken cancellationToken) {
        await _lectorLock.WaitAsync(cancellationToken);
        try {
            if (_lectorGlobal == null) InicializarLector();
            if (_lectorGlobal == null) return null;

            CaptureResult captureResult = _lectorGlobal.Capture(
                Constants.Formats.Fid.ANSI,
                Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT,
                1000,
                _lectorGlobal.Capabilities.Resolutions[0]);

            if (captureResult.ResultCode == Constants.ResultCode.DP_DEVICE_FAILURE) {
                Console.WriteLine("[DETECTOR] Lector desconectado. Reiniciando...");
                ReiniciarLector();
                return null;
            }

            if (captureResult.ResultCode != Constants.ResultCode.DP_SUCCESS || captureResult.Quality != Constants.CaptureQuality.DP_QUALITY_GOOD) {
                return null;
            }

            DataResult<Fmd> fmdResult = FeatureExtraction.CreateFmdFromFid(captureResult.Data, Constants.Formats.Fmd.ANSI);
            if (fmdResult.ResultCode != Constants.ResultCode.DP_SUCCESS) return null;

            return fmdResult.Data;
        } finally {
            _lectorLock.Release();
        }
    }

    public static async Task<Fmd?> EnrolarSocioAsync(Func<string, Task> onMessage) {
        await _lectorLock.WaitAsync();
        try {
            if (_lectorGlobal == null) InicializarLector();
            if (_lectorGlobal == null) { await onMessage("Lector no conectado."); return null; }

            List<Fmd> preEnrollmentFmds = new List<Fmd>();
            int count = 0;
            
            while (count < 4) {
                await onMessage($"[Toque {count + 1} de 4] Ponga el dedo en el lector...");
                
                CaptureResult captureResult = _lectorGlobal.Capture(Constants.Formats.Fid.ANSI, Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT, 15000, _lectorGlobal.Capabilities.Resolutions[0]);
                
                if (captureResult.ResultCode == Constants.ResultCode.DP_DEVICE_FAILURE) {
                    await onMessage("Lector desconectado.");
                    ReiniciarLector();
                    return null;
                }

                if (captureResult.ResultCode == Constants.ResultCode.DP_SUCCESS) {
                    if (captureResult.Quality == Constants.CaptureQuality.DP_QUALITY_GOOD) {
                        DataResult<Fmd> fmdResult = FeatureExtraction.CreateFmdFromFid(captureResult.Data, Constants.Formats.Fmd.ANSI);
                        if (fmdResult.ResultCode == Constants.ResultCode.DP_SUCCESS) {
                            preEnrollmentFmds.Add(fmdResult.Data);
                            count++;
                            await onMessage($"-> ¡Lectura {count} exitosa! Levante el dedo por favor.");
                            Task.Delay(1000).Wait(); 
                        }
                    } else if (captureResult.Quality == Constants.CaptureQuality.DP_QUALITY_CANCELED) {
                        break;
                    } else {
                        await onMessage($"-> Mala calidad. Limpie el sensor.");
                        Task.Delay(1000).Wait();
                    }
                } else {
                    break; 
                }
            }
            
            if (preEnrollmentFmds.Count == 4) {
                DataResult<Fmd> result = DPUruNet.Enrollment.CreateEnrollmentFmd(Constants.Formats.Fmd.ANSI, preEnrollmentFmds);
                if (result.ResultCode == Constants.ResultCode.DP_SUCCESS) return result.Data;
            }
            return null;
        } finally {
            _lectorLock.Release();
        }
    }
}

// ========================================================
// SERVICIO DE ESCUCHA CONTINUA EN BACKGROUND
// ========================================================
public class DeteccionContinuaService : BackgroundService {
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly HashSet<string> _cooldownSocios = new();

    public DeteccionContinuaService(IHttpClientFactory httpClientFactory, IConfiguration configuration) {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        Console.WriteLine("[DETECTOR] Servicio de escucha continua iniciado.");

        while (!stoppingToken.IsCancellationRequested) {
            try {
                if (!MemoriaHuellas.EstaCargada || MemoriaHuellas.ListaFmds.Count == 0) {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                Fmd? huella = await MotorBiometrico.CapturarHuellaSilenciosaAsync(stoppingToken);
                if (huella == null) {
                    continue;
                }

                IdentifyResult identifyResult = Comparison.Identify(huella, 0, MemoriaHuellas.ListaFmds, 21474, 1);
                if (identifyResult.ResultCode != Constants.ResultCode.DP_SUCCESS || identifyResult.Indexes.Length == 0) {
                    continue;
                }

                string codigoSocio = MemoriaHuellas.IndexToCodigoSocio[identifyResult.Indexes[0][0]];
                lock (_cooldownSocios) {
                    if (_cooldownSocios.Contains(codigoSocio)) {
                        continue;
                    }
                }

                _ = ActivarCooldown(codigoSocio, stoppingToken);

                // DISPARO INSTANTÁNEO POR WEBSOCKET A TODOS LOS KIOSCOS
                var eventoWs = new {
                    codigoSocio = codigoSocio,
                    fechaUtc = DateTime.UtcNow,
                    success = true,
                    origen = "lector-automatico"
                };
                await WebSocketHandler.BroadcastAsync(eventoWs);
                Console.WriteLine($"[WEBSOCKET] 🚀 Match empujado a {WebSocketHandler.ConexionesActivas} Kiosco(s): {codigoSocio}");

                // Fallback Legacy: Mantiene la petición HTTP por si decides usarla en el futuro.
                // (Si FrontendCallbackUrl está vacío en appsettings.json, esto simplemente hace log y no consume internet)
                await NotificarFrontAsync(new EventoHuella {
                    CodigoSocio = codigoSocio,
                    FechaUtc = DateTime.UtcNow
                }, stoppingToken);

            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                Console.WriteLine("[DETECTOR] Error: " + ex.Message);
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task NotificarFrontAsync(EventoHuella evento, CancellationToken cancellationToken) {
        string? callbackUrl = _configuration["Motor:FrontendCallbackUrl"];
        if (string.IsNullOrWhiteSpace(callbackUrl)) {
            // Ya no es un error, es el comportamiento deseado al usar WebSockets.
            return;
        }

        HttpClient client = _httpClientFactory.CreateClient();
        string payload = JsonSerializer.Serialize(evento);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        try {
            HttpResponseMessage response = await client.PostAsync(callbackUrl, content, cancellationToken);
            if (response.IsSuccessStatusCode) {
                Console.WriteLine($"[HTTP] Evento enviado al front. Socio: {evento.CodigoSocio}");
            } else {
                Console.WriteLine($"[HTTP] El front respondió {(int)response.StatusCode} para socio {evento.CodigoSocio}");
            }
        } catch {
            Console.WriteLine($"[HTTP] Falló la conexión al webhook de Vercel.");
        }
    }

    private async Task ActivarCooldown(string codigoSocio, CancellationToken cancellationToken) {
        int cooldownMs = _configuration.GetValue<int?>("Motor:CooldownMs") ?? 3000;
        lock (_cooldownSocios) {
            _cooldownSocios.Add(codigoSocio);
        }

        try {
            await Task.Delay(cooldownMs, cancellationToken);
        } catch (OperationCanceledException) {
            // Ignorado
        } finally {
            lock (_cooldownSocios) {
                _cooldownSocios.Remove(codigoSocio);
            }
        }
    }
}