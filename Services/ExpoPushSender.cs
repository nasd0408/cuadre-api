using System.Text;
using System.Text.Json;

namespace BarberShop.Services
{
    /// <summary>Un mensaje push: token del dispositivo destino, titulo, cuerpo y datos extra opcionales.</summary>
    public record ExpoPushMessage(
        string To,
        string Title,
        string Body,
        object? Data = null);

    /// <summary>Resultado del envio: cuantos acepto Expo y que tokens ya no existen (app desinstalada).</summary>
    public record ExpoPushSendResult(
        int Accepted,
        IReadOnlyCollection<string> InvalidTokens);

    /// <summary>Abstraccion del envio de push, para poder simularlo en tests.</summary>
    public interface IExpoPushSender
    {
        /// <summary>Envia los mensajes y devuelve el resumen del resultado.</summary>
        Task<ExpoPushSendResult> SendAsync(
            IEnumerable<ExpoPushMessage> messages,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Envía notificaciones vía el servicio gratuito de Expo Push.
    /// https://docs.expo.dev/push-notifications/sending-notifications/
    /// </summary>
    public class ExpoPushSender : IExpoPushSender
    {
        private const string Endpoint = "https://exp.host/--/api/v2/push/send";
        private const int BatchSize = 100;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ExpoPushSender> _logger;

        /// <summary>Recibe la fabrica de HttpClient y el logger por DI.</summary>
        public ExpoPushSender(IHttpClientFactory httpClientFactory, ILogger<ExpoPushSender> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// Parte los mensajes en lotes de 100 (limite de Expo), manda cada lote en
        /// un POST y lee la respuesta: Expo devuelve un "ticket" por mensaje EN EL
        /// MISMO ORDEN, por eso se recorren con un indice paralelo. Los tickets con
        /// error DeviceNotRegistered indican tokens muertos que conviene borrar.
        /// Un lote que falla no detiene a los demas.
        /// </summary>
        public async Task<ExpoPushSendResult> SendAsync(
            IEnumerable<ExpoPushMessage> messages,
            CancellationToken ct = default)
        {
            var client = _httpClientFactory.CreateClient();
            var accepted = 0;
            var invalidTokens = new HashSet<string>(StringComparer.Ordinal);

            foreach (var batch in Chunk(messages, BatchSize))
            {
                var payload = batch.Select(m => new
                {
                    to = m.To,
                    title = m.Title,
                    body = m.Body,
                    data = m.Data,
                    sound = "default",
                    channelId = "default",
                    priority = "high",
                }).ToList();

                var json = JsonSerializer.Serialize(payload);

                try
                {
                    using var response = await SendWithRetryAsync(client, json, ct);
                    var body = await response.Content.ReadAsStringAsync(ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Expo push {Status}: {Body}", response.StatusCode, body);
                        continue;
                    }

                    using var document = JsonDocument.Parse(body);
                    if (!document.RootElement.TryGetProperty("data", out var tickets) ||
                        tickets.ValueKind != JsonValueKind.Array)
                    {
                        _logger.LogWarning("Expo push devolvió una respuesta inesperada: {Body}", body);
                        continue;
                    }

                    var index = 0;
                    foreach (var ticket in tickets.EnumerateArray())
                    {
                        if (index >= batch.Count) break;

                        var status = ticket.TryGetProperty("status", out var statusValue)
                            ? statusValue.GetString()
                            : null;

                        if (status == "ok")
                        {
                            accepted++;
                        }
                        else if (IsDeviceNotRegistered(ticket))
                        {
                            invalidTokens.Add(batch[index].To);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Expo rechazó el push para {Token}: {Ticket}",
                                MaskToken(batch[index].To),
                                ticket.GetRawText());
                        }

                        index++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enviando push a Expo");
                }
            }

            _logger.LogInformation(
                "Expo aceptó {Accepted} push(es); {Invalid} token(es) inválido(s)",
                accepted,
                invalidTokens.Count);

            return new ExpoPushSendResult(accepted, invalidTokens.ToList());
        }

        /// <summary>
        /// Reintenta hasta 3 veces si Expo responde 429 (demasiadas peticiones) o
        /// 5xx (error suyo), esperando 2 s y luego 4 s: "exponential backoff".
        /// Los 4xx no se reintentan porque el error es nuestro y se repetiria igual.
        /// </summary>
        private static async Task<HttpResponseMessage> SendWithRetryAsync(
            HttpClient client,
            string json,
            CancellationToken ct)
        {
            HttpResponseMessage? response = null;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                response?.Dispose();
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                response = await client.PostAsync(Endpoint, content, ct);

                var temporaryFailure =
                    (int)response.StatusCode == 429 ||
                    (int)response.StatusCode >= 500;

                if (!temporaryFailure || attempt == 2)
                    return response;

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct);
            }

            return response!;
        }

        /// <summary>true si el ticket dice que el dispositivo ya no esta registrado.</summary>
        private static bool IsDeviceNotRegistered(JsonElement ticket)
        {
            return ticket.TryGetProperty("details", out var details) &&
                   details.TryGetProperty("error", out var error) &&
                   error.GetString() == "DeviceNotRegistered";
        }

        /// <summary>Oculta el centro del token para no escribirlo completo en los logs.</summary>
        private static string MaskToken(string token)
        {
            if (token.Length <= 12) return "***";
            return $"{token[..8]}...{token[^4..]}";
        }

        /// <summary>
        /// Divide una secuencia en listas de tamano "size". Usa "yield return":
        /// un iterador perezoso que entrega cada lote cuando se pide, sin armar
        /// todos los lotes en memoria de antemano. (.NET 6+ trae Enumerable.Chunk
        /// que hace lo mismo.)
        /// </summary>
        private static IEnumerable<List<T>> Chunk<T>(IEnumerable<T> source, int size)
        {
            var bucket = new List<T>(size);
            foreach (var item in source)
            {
                bucket.Add(item);
                if (bucket.Count == size)
                {
                    yield return bucket;
                    bucket = new List<T>(size);
                }
            }
            if (bucket.Count > 0) yield return bucket;
        }
    }
}
