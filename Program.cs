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
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

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

async Task EnviarMensaje(HttpContext ctx, string texto) {
    Console.WriteLine(texto);
    var json = JsonSerializer.Serialize(new { tipo = "mensaje", texto = texto });
    await ctx.Response.WriteAsync(json + "\n");
    await ctx.Response.Body.FlushAsync();
}

app.MapGet("/", () => "¡Motor DPUruNet Moderno Activo y Escuchando!");

app.MapGet("/estado", () => Results.Ok(new {
    cacheCargada = MemoriaHuellas.EstaCargada,
    huellasEnMemoria = MemoriaHuellas.TotalHuellas,
    lectorConectado = MotorBiometrico.LectorConectado,
    detectorContinuo = "activo",
    lectorOcupado = MotorBiometrico.LectorOcupado,
    ultimoUsoLectorUtc = MotorBiometrico.UltimoUsoUtc,
    ultimoErrorLector = MotorBiometrico.UltimoError,
    eventosPendientes = EventosHuellaMotor.TotalEventos
}));

app.MapGet("/eventos", (HttpRequest request) => {
    long after = 0;
    int take = 20;

    if (long.TryParse(request.Query["after"], out var afterParam) && afterParam > 0) {
        after = afterParam;
    }

    if (int.TryParse(request.Query["take"], out var takeParam)) {
        take = Math.Min(Math.Max(takeParam, 0), 50);
    }

    return Results.Ok(new HuellaMotorEventsResponse {
        Success = true,
        Data = EventosHuellaMotor.Obtener(after, take)
    });
});

// ========================================================
// ENDPOINT: Cargar Huellas y "Calentar" el USB
// ========================================================
app.MapPost("/cargar-cache", ([FromBody] PeticionMatch request) => {
    var nuevasHuellas = new List<Fmd>();
    var nuevoIndice = new Dictionary<int, string>();
    
    int index = 0;
    foreach (var socio in request.BaseDeDatos) {
        if (string.IsNullOrEmpty(socio.HuellaTemplate)) continue;
        try {
            Fmd fmdDb = Fmd.DeserializeXml(socio.HuellaTemplate);
            nuevasHuellas.Add(fmdDb);
            nuevoIndice[index] = socio.CodigoSocio;
            index++;
        } catch { continue; }
    }
    
    MemoriaHuellas.ReemplazarCache(nuevasHuellas, nuevoIndice);
    
    // ¡MAGIA! Encendemos el lector USB en segundo plano mientras se cargan los datos
    MotorBiometrico.InicializarLector();
    
    Console.WriteLine($"\n[SISTEMA] Se cargaron {index} huellas. Motor USB pre-calentado.");
    return Results.Ok(new { success = true, totalCargadas = index });
});

// ========================================================
// 1. ENDPOINT: COMPARAR (Lector Always-On)
// ========================================================
app.MapPost("/comparar", async (HttpContext ctx) => {
    ctx.Response.ContentType = "application/x-ndjson";
    
    try {
        var cache = MemoriaHuellas.ObtenerSnapshot();
        if (!cache.EstaCargada || cache.Huellas.Count == 0) {
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { tipo = "resultado", success = false, message = "Sin huellas en memoria." }) + "\n");
            return;
        }

        await EnviarMensaje(ctx, "Ponga el dedo en el lector...");
        
        Fmd? fmdCapturado = await MotorBiometrico.CapturarHuellaAsync(async (msg) => await EnviarMensaje(ctx, msg));
        
        if (fmdCapturado == null) {
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { tipo = "resultado", success = false, message = "Lectura fallida o cancelada." }) + "\n");
            return;
        }

        IdentifyResult identifyResult = Comparison.Identify(fmdCapturado, 0, cache.Huellas, 21474, 1);

        if (identifyResult.ResultCode == Constants.ResultCode.DP_SUCCESS && identifyResult.Indexes.Length > 0) {
            string codigoSocioMatch = cache.IndexToCodigoSocio[identifyResult.Indexes[0][0]];
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
// 2. ENDPOINT: ENROLAR (Lector Always-On)
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

public class PeticionMatch { public List<SocioDb> BaseDeDatos { get; set; } = new List<SocioDb>(); }
public class SocioDb { public string CodigoSocio { get; set; } = ""; public string HuellaTemplate { get; set; } = ""; }
public class EventoHuella {
    public string CodigoSocio { get; set; } = "";
    public DateTime FechaUtc { get; set; }
    public string Origen { get; set; } = "lector-automatico";
}

public class HuellaMotorEventsResponse {
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public HuellaMotorEventsData Data { get; set; } = new();
}

public class HuellaMotorEventsData {
    [JsonPropertyName("eventos")]
    public List<HuellaMotorEventDto> Eventos { get; set; } = new();

    [JsonPropertyName("latestId")]
    public long LatestId { get; set; }
}

public class HuellaMotorEventDto {
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("receivedAt")]
    public DateTime ReceivedAt { get; set; }

    [JsonPropertyName("codigoSocio")]
    public string? CodigoSocio { get; set; }

    [JsonPropertyName("confidence")]
    public int? Confidence { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "match";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "motor";

    [JsonPropertyName("raw")]
    public Dictionary<string, object?> Raw { get; set; } = new();
}

// ========================================================
// MEMORIA RAM DEL MOTOR (CACHÉ ULTRA RÁPIDO)
// ========================================================
public static class MemoriaHuellas {
    private static readonly object LockCache = new();
    public static List<Fmd> ListaFmds = new List<Fmd>();
    public static Dictionary<int, string> IndexToCodigoSocio = new Dictionary<int, string>();
    public static bool EstaCargada = false;

    public static int TotalHuellas {
        get {
            lock (LockCache) {
                return ListaFmds.Count;
            }
        }
    }

    public static void ReemplazarCache(List<Fmd> nuevasHuellas, Dictionary<int, string> nuevoIndice) {
        lock (LockCache) {
            ListaFmds = nuevasHuellas;
            IndexToCodigoSocio = nuevoIndice;
            EstaCargada = true;
        }
    }

    public static HuellasCacheSnapshot ObtenerSnapshot() {
        lock (LockCache) {
            return new HuellasCacheSnapshot(
                EstaCargada,
                ListaFmds.ToList(),
                new Dictionary<int, string>(IndexToCodigoSocio));
        }
    }
}

public record HuellasCacheSnapshot(
    bool EstaCargada,
    List<Fmd> Huellas,
    Dictionary<int, string> IndexToCodigoSocio);

public static class EventosHuellaMotor {
    private const int MaxEventos = 200;
    private static readonly object LockEventos = new();
    private static readonly List<HuellaMotorEventDto> Eventos = new();
    private static long _ultimoId = 0;

    public static int TotalEventos {
        get {
            lock (LockEventos) {
                return Eventos.Count;
            }
        }
    }

    public static HuellaMotorEventDto RegistrarMatch(EventoHuella evento) {
        var nuevoEvento = new HuellaMotorEventDto {
            Id = Interlocked.Increment(ref _ultimoId),
            ReceivedAt = evento.FechaUtc == default ? DateTime.UtcNow : evento.FechaUtc,
            CodigoSocio = evento.CodigoSocio,
            Confidence = 100,
            Success = true,
            Message = $"Huella detectada para {evento.CodigoSocio}",
            Kind = "match",
            Source = "motor",
            Raw = new Dictionary<string, object?> {
                ["codigoSocio"] = evento.CodigoSocio,
                ["CodigoSocio"] = evento.CodigoSocio,
                ["fechaUtc"] = evento.FechaUtc,
                ["origen"] = evento.Origen
            }
        };

        lock (LockEventos) {
            Eventos.Add(nuevoEvento);
            if (Eventos.Count > MaxEventos) {
                Eventos.RemoveRange(0, Eventos.Count - MaxEventos);
            }
        }

        return nuevoEvento;
    }

    public static HuellaMotorEventsData Obtener(long afterId, int take) {
        lock (LockEventos) {
            long latestId = Eventos.Count > 0 ? Eventos[^1].Id : 0;
            var eventos = take <= 0
                ? new List<HuellaMotorEventDto>()
                : Eventos
                    .Where(evento => evento.Id > afterId)
                    .OrderBy(evento => evento.Id)
                    .Take(take)
                    .ToList();

            return new HuellaMotorEventsData {
                LatestId = latestId,
                Eventos = eventos
            };
        }
    }
}

// ========================================================
// CONTROLADOR DEL HARDWARE DPUruNet (ALWAYS-ON)
// ========================================================
public static class MotorBiometrico {
    private static Reader? _lectorGlobal = null;
    private static readonly SemaphoreSlim _lectorLock = new(1, 1);
    private static DateTime? _ultimoUsoUtc = null;
    private static string _ultimoError = "";
    public static bool LectorConectado => _lectorGlobal != null;
    public static bool LectorOcupado => _lectorLock.CurrentCount == 0;
    public static DateTime? UltimoUsoUtc => _ultimoUsoUtc;
    public static string UltimoError => _ultimoError;

    // Conecta el hardware 1 sola vez y lo deja abierto
    public static void InicializarLector() {
        try {
            if (_lectorGlobal == null) {
                ReaderCollection readers = ReaderCollection.GetReaders();
                if (readers.Count > 0) {
                    _lectorGlobal = readers[0];
                    _lectorGlobal.Open(Constants.CapturePriority.DP_PRIORITY_EXCLUSIVE);
                    _ultimoError = "";
                    Console.WriteLine("[HARDWARE] Lector USB conectado y en espera permanente.");
                }
            }
        } catch (Exception ex) {
            _ultimoError = ex.Message;
            Console.WriteLine("[HARDWARE] Error USB: " + ex.Message);
        }
    }

    // Auto-recuperación por si desconectan el cable
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
            _ultimoUsoUtc = DateTime.UtcNow;
            if (_lectorGlobal == null) InicializarLector();
            if (_lectorGlobal == null) { 
                await onMessage("Error: Lector no encontrado. Conecte el USB."); 
                return null; 
            }

            Fmd? fmdFinal = null;
            // ¡La lectura ahora arranca instantáneamente!
            CaptureResult captureResult = _lectorGlobal.Capture(Constants.Formats.Fid.ANSI, Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT, 15000, _lectorGlobal.Capabilities.Resolutions[0]);
            
            // Si el lector fue desconectado a la fuerza, el SDK lanza DP_DEVICE_FAILURE
            if (captureResult.ResultCode == Constants.ResultCode.DP_DEVICE_FAILURE) {
                await onMessage("Lector desconectado. Reiniciando...");
                _ultimoError = "DP_DEVICE_FAILURE";
                ReiniciarLector();
                return null;
            }

            if (captureResult.ResultCode == Constants.ResultCode.DP_SUCCESS && captureResult.Quality == Constants.CaptureQuality.DP_QUALITY_GOOD) {
                DataResult<Fmd> fmdResult = FeatureExtraction.CreateFmdFromFid(captureResult.Data, Constants.Formats.Fmd.ANSI);
                if (fmdResult.ResultCode == Constants.ResultCode.DP_SUCCESS) fmdFinal = fmdResult.Data;
            } else if (captureResult.ResultCode != Constants.ResultCode.DP_SUCCESS) {
                _ultimoError = captureResult.ResultCode.ToString();
                await onMessage($"Error de lectura o tiempo agotado.");
            }
            
            // ELIMINAMOS EL _lectorGlobal.Dispose() PARA MANTENERLO VIVO
            return fmdFinal;
        } finally {
            _lectorLock.Release();
        }
    }

    public static async Task<Fmd?> CapturarHuellaSilenciosaAsync(CancellationToken cancellationToken) {
        if (!await _lectorLock.WaitAsync(0, cancellationToken)) {
            return null;
        }

        try {
            _ultimoUsoUtc = DateTime.UtcNow;
            if (_lectorGlobal == null) InicializarLector();
            if (_lectorGlobal == null) return null;

            CaptureResult captureResult = _lectorGlobal.Capture(
                Constants.Formats.Fid.ANSI,
                Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT,
                1000,
                _lectorGlobal.Capabilities.Resolutions[0]);

            if (captureResult.ResultCode == Constants.ResultCode.DP_DEVICE_FAILURE) {
                Console.WriteLine("[DETECTOR] Lector desconectado. Reiniciando...");
                _ultimoError = "DP_DEVICE_FAILURE";
                ReiniciarLector();
                return null;
            }

            if (captureResult.ResultCode != Constants.ResultCode.DP_SUCCESS || captureResult.Quality != Constants.CaptureQuality.DP_QUALITY_GOOD) {
                if (captureResult.ResultCode != Constants.ResultCode.DP_SUCCESS) {
                    _ultimoError = captureResult.ResultCode.ToString();
                }
                return null;
            }

            DataResult<Fmd> fmdResult = FeatureExtraction.CreateFmdFromFid(captureResult.Data, Constants.Formats.Fmd.ANSI);
            if (fmdResult.ResultCode != Constants.ResultCode.DP_SUCCESS) return null;

            _ultimoError = "";
            return fmdResult.Data;
        } finally {
            _lectorLock.Release();
        }
    }

    public static async Task<Fmd?> EnrolarSocioAsync(Func<string, Task> onMessage) {
        await _lectorLock.WaitAsync();
        try {
            _ultimoUsoUtc = DateTime.UtcNow;
            if (_lectorGlobal == null) InicializarLector();
            if (_lectorGlobal == null) { await onMessage("Lector no conectado."); return null; }

            List<Fmd> preEnrollmentFmds = new List<Fmd>();
            int count = 0;
            
            while (count < 4) {
                await onMessage($"[Toque {count + 1} de 4] Ponga el dedo en el lector...");
                
                CaptureResult captureResult = _lectorGlobal.Capture(Constants.Formats.Fid.ANSI, Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT, 15000, _lectorGlobal.Capabilities.Resolutions[0]);
                
                if (captureResult.ResultCode == Constants.ResultCode.DP_DEVICE_FAILURE) {
                    await onMessage("Lector desconectado.");
                    _ultimoError = "DP_DEVICE_FAILURE";
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
                var cache = MemoriaHuellas.ObtenerSnapshot();
                if (!cache.EstaCargada || cache.Huellas.Count == 0) {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                Fmd? huella = await MotorBiometrico.CapturarHuellaSilenciosaAsync(stoppingToken);
                if (huella == null) {
                    continue;
                }

                IdentifyResult identifyResult = Comparison.Identify(huella, 0, cache.Huellas, 21474, 1);
                if (identifyResult.ResultCode != Constants.ResultCode.DP_SUCCESS || identifyResult.Indexes.Length == 0) {
                    continue;
                }

                string codigoSocio = cache.IndexToCodigoSocio[identifyResult.Indexes[0][0]];
                lock (_cooldownSocios) {
                    if (_cooldownSocios.Contains(codigoSocio)) {
                        continue;
                    }
                }

                _ = ActivarCooldown(codigoSocio, stoppingToken);

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
        EventosHuellaMotor.RegistrarMatch(evento);
        Console.WriteLine($"[DETECTOR] Evento local registrado. Socio: {evento.CodigoSocio}");

        string? callbackUrl = _configuration["Motor:FrontendCallbackUrl"];
        if (string.IsNullOrWhiteSpace(callbackUrl)) {
            return;
        }

        HttpClient client = _httpClientFactory.CreateClient();
        string payload = JsonSerializer.Serialize(evento);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync(callbackUrl, content, cancellationToken);
        if (response.IsSuccessStatusCode) {
            Console.WriteLine($"[DETECTOR] Evento enviado al front. Socio: {evento.CodigoSocio}");
            return;
        }

        Console.WriteLine($"[DETECTOR] El front respondió {(int)response.StatusCode} para socio {evento.CodigoSocio}");
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
