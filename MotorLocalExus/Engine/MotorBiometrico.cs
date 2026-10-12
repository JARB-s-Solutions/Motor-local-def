using DPUruNet;
using Exus;

// El servicio corre en Session 0 y no debe abrir el SDK USB directamente.
// Toda captura se ejecuta en el auxiliar interactivo, que comparte la sesion
// del usuario donde Windows expone el lector DigitalPersona.
public static class MotorBiometrico
{
    public static ReaderBridge? Bridge { get; set; }
    private static readonly SemaphoreSlim OperationLock = new(1, 1);
    private static string _ultimoError = "";
    private static DateTime? _ultimoUsoUtc;
    private static bool _enrolando;

    public static bool LectorConectado => Bridge?.Connected == true && Bridge.Status.Connected;
    public static bool AuxiliarConectado => Bridge?.Connected == true;
    public static bool LectorOcupado => OperationLock.CurrentCount == 0;
    public static bool Enrolando => _enrolando;
    public static string Estado => !AuxiliarConectado ? "waiting_agent" : !LectorConectado ? (_ultimoError.Length > 0 ? "error" : "disconnected") : LectorOcupado ? "busy" : "ready";
    public static DateTime? UltimoUsoUtc => _ultimoUsoUtc ?? Bridge?.Status.LastUseUtc;
    public static string UltimoError => _ultimoError.Length > 0 ? _ultimoError : Bridge?.Status.Error ?? "";

    public static async Task EnsureReaderAsync(CancellationToken cancellationToken)
    {
        var bridge = Bridge;
        if (bridge == null || !bridge.Connected) return;
        try { Apply(await bridge.Call("status", null, cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _ultimoError = ex.Message; }
    }

    public static Task<Fmd?> CapturarHuellaAsync(Func<string, Task> onMessage, CancellationToken cancellationToken = default) => Capture("capture-manual", onMessage, cancellationToken);
    public static Task<Fmd?> CapturarHuellaSilenciosaAsync(CancellationToken cancellationToken = default) => Capture("capture", null, cancellationToken);

    private static async Task<Fmd?> Capture(string kind, Func<string, Task>? onMessage, CancellationToken cancellationToken)
    {
        var bridge = Bridge;
        if (bridge == null || !bridge.Connected)
        {
            _ultimoError = "READER_AGENT_NOT_CONNECTED";
            if (onMessage != null) await onMessage("El auxiliar del lector aún no está iniciado.");
            return null;
        }
        if (!await OperationLock.WaitAsync(0, cancellationToken)) return null;
        try
        {
            _ultimoUsoUtc = DateTime.UtcNow;
            var reply = await bridge.Call(kind, onMessage, cancellationToken);
            Apply(reply);
            return Deserialize(reply.Template);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex)
        {
            _ultimoError = ex.Message;
            if (onMessage != null) await onMessage("No se pudo comunicar con el lector. Se reintentará automáticamente.");
            return null;
        }
        finally { OperationLock.Release(); }
    }

    public static async Task<Fmd?> EnrolarSocioAsync(Func<string, Task> onMessage, CancellationToken cancellationToken = default)
    {
        var bridge = Bridge;
        if (bridge == null || !bridge.Connected) { await onMessage("El auxiliar del lector aún no está iniciado."); return null; }
        if (!await OperationLock.WaitAsync(0, cancellationToken)) return null;
        try
        {
            _enrolando = true; _ultimoUsoUtc = DateTime.UtcNow;
            var reply = await bridge.Call("enroll", onMessage, cancellationToken);
            Apply(reply);
            return Deserialize(reply.Template);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex) { _ultimoError = ex.Message; return null; }
        finally { _enrolando = false; OperationLock.Release(); }
    }

    private static void Apply(ReaderReply reply)
    {
        _ultimoError = reply.Error ?? "";
        if (reply.LastUseUtc.HasValue) _ultimoUsoUtc = reply.LastUseUtc;
    }

    private static Fmd? Deserialize(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        try { return Fmd.DeserializeXml(xml); }
        catch { _ultimoError = "FMD_INVALIDA"; return null; }
    }
}
