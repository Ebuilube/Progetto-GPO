// SERVER - Telemetria sicura (HttpListener + JSON Schema da file)
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

class Server
{
    // Gruppo 14 di RFC 3526 (2048 bit), g = 2
    static BigInteger P = Hex(
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74" +
        "020BBEA63B139B22514A08798E3404DDEF9519B3CD3A431B302B0A6DF25F1437" +
        "4FE1356D6D51C245E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
        "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3DC2007CB8A163BF05" +
        "98DA48361C55D39A69163FA8FD24CF5F83655D23DCA3AD961C62F356208552BB" +
        "9ED529077096966D670C354E4ABC9804F1746C08CA18217C32905E462E36CE3B" +
        "E39E772C180E86039B2783A2EC07A28FB5C55DF06F4C52C9DE2BCBF695581718" +
        "3995497CEA956AE515D2261898FA051015728E5A8AACAA68FFFFFFFFFFFFFFFF");
    static BigInteger G = 2;

    // session_id -> chiave di sessione
    static ConcurrentDictionary<string, byte[]> sessioni = new();
    // misure salvate (stringhe JSON)
    static List<string> misure = new();

    // Schemi caricati dalla cartella schemas/
    static JsonSchema schHandshake = Carica("handshake_request.json");
    static JsonSchema schTelemetry = Carica("telemetry.json");
    static JsonSchema schReadings = Carica("readings_request.json");

    static void Main()
    {
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:11111/");
        listener.Start();

        while (true)
        {
            Console.WriteLine("Waiting connection ... ");
            HttpListenerContext ctx = listener.GetContext();
            try
            {
                Handle(ctx);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                try { Send(ctx, 500, Err("errore interno")); } catch { }
            }
        }
    }

    // ---------- Smistamento ----------
    static void Handle(HttpListenerContext ctx)
    {
        string path = ctx.Request.Url!.AbsolutePath;

        string[] endpoints = { "/api/handshake", "/api/telemetry", "/api/readings", "/api/close" };
        if (!endpoints.Contains(path))
        {
            Send(ctx, 404, Err("endpoint inesistente"));
            return;
        }
        if (ctx.Request.HttpMethod != "POST")
        {
            Send(ctx, 501, Err("metodo non implementato"));
            return;
        }

        JsonNode? body;
        try
        {
            using StreamReader reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            body = JsonNode.Parse(reader.ReadToEnd());
        }
        catch { body = null; }
        if (body == null)
        {
            Send(ctx, 400, Err("JSON non valido"));
            return;
        }

        if (path == "/api/handshake") Handshake(ctx, body);
        else if (path == "/api/telemetry") Telemetry(ctx, body);
        else if (path == "/api/readings") Readings(ctx, body);
        else Close(ctx, body);
    }

    // ---------- /api/handshake (in chiaro, validato con lo schema) ----------
    static void Handshake(HttpListenerContext ctx, JsonNode body)
    {
        string? problema = Valida(schHandshake, body);
        if (problema != null) { Send(ctx, 400, Err(problema)); return; }

        BigInteger p = Hex((string)body["p"]!);
        BigInteger g = Hex((string)body["g"]!);
        BigInteger A = Hex((string)body["A"]!);

        if (p != P || g != G) { Send(ctx, 400, Err("gruppo DH non supportato")); return; }
        if (A <= 1 || A >= P - 1) { Send(ctx, 400, Err("chiave pubblica non valida")); return; }

        BigInteger b = new BigInteger(RandomNumberGenerator.GetBytes(32), isUnsigned: true);
        BigInteger B = BigInteger.ModPow(G, b, P);   // B = g^b mod p
        BigInteger K = BigInteger.ModPow(A, b, P);   // K = A^b mod p

        byte[] chiave = SHA256.HashData(K.ToByteArray(isUnsigned: true, isBigEndian: true));
        string sid = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLower();
        sessioni[sid] = chiave;
        Console.WriteLine("Nuova sessione " + sid);

        Send(ctx, 200, new JsonObject { ["session_id"] = sid, ["B"] = B.ToString("X") }.ToJsonString());
    }

    // ---------- /api/telemetry ----------
    static void Telemetry(HttpListenerContext ctx, JsonNode busta)
    {
        // prima DECIFRA...
        if (!Apri(ctx, busta, out string sid, out byte[] chiave, out JsonNode? msg)) return;

        // ...poi VALIDA
        string? problema = Valida(schTelemetry, msg!);
        if (problema != null)
        {
            Reply(ctx, 400, sid, chiave, new JsonObject { ["error"] = problema });
            return;
        }

        lock (misure)
        {
            misure.Add(msg!.ToJsonString());
            File.AppendAllText("readings.json", msg.ToJsonString() + "\n");
        }

        // allarme se fuori intervallo
        string tipo = (string)msg["cargo_type"]!;
        double t = (double)msg["temperature_c"]!;
        (double min, double max) = tipo switch
        {
            "pharma" => (2.0, 8.0),
            "food" => (0.0, 6.0),
            _ => (-25.0, -15.0)   // frozen
        };
        bool allarme = t < min || t > max;

        JsonObject ack = new JsonObject { ["status"] = "ok", ["alarm"] = allarme };
        if (allarme) ack["message"] = $"temperatura {t} fuori dall'intervallo {min}..{max} per {tipo}";
        Reply(ctx, 200, sid, chiave, ack);
    }

    // ---------- /api/readings ----------
    static void Readings(HttpListenerContext ctx, JsonNode busta)
    {
        if (!Apri(ctx, busta, out string sid, out byte[] chiave, out JsonNode? msg)) return;

        string? problema = Valida(schReadings, msg!);
        if (problema != null)
        {
            Reply(ctx, 400, sid, chiave, new JsonObject { ["error"] = problema });
            return;
        }

        string sensore = (string)msg!["sensor_id"]!;
        DateTimeOffset? da = msg["from"] != null ? DateTimeOffset.Parse((string)msg["from"]!) : null;
        DateTimeOffset? a = msg["to"] != null ? DateTimeOffset.Parse((string)msg["to"]!) : null;

        JsonArray risultato = new JsonArray();
        lock (misure)
        {
            foreach (string s in misure)
            {
                JsonNode m = JsonNode.Parse(s)!;
                if ((string)m["sensor_id"]! != sensore) continue;
                DateTimeOffset ts = DateTimeOffset.Parse((string)m["timestamp"]!);
                if (da != null && ts < da) continue;
                if (a != null && ts > a) continue;
                risultato.Add(m);
            }
        }
        Reply(ctx, 200, sid, chiave, new JsonObject { ["count"] = risultato.Count, ["readings"] = risultato });
    }

    // ---------- /api/close ----------
    static void Close(HttpListenerContext ctx, JsonNode busta)
    {
        if (!Apri(ctx, busta, out string sid, out byte[] chiave, out JsonNode? msg)) return;
        Reply(ctx, 200, sid, chiave, new JsonObject { ["status"] = "closed" });
        sessioni.TryRemove(sid, out _);   // cancella la chiave
        Console.WriteLine("Sessione chiusa " + sid);
    }

    // ---------- Controlla la sessione e decifra la busta ----------
    static bool Apri(HttpListenerContext ctx, JsonNode busta, out string sid, out byte[] chiave, out JsonNode? msg)
    {
        sid = "";
        chiave = Array.Empty<byte>();
        msg = null;
        try
        {
            sid = (string?)busta["session_id"] ?? "";
            if (sid == "") { Send(ctx, 401, Err("session_id mancante")); return false; }
            if (!sessioni.TryGetValue(sid, out byte[]? k)) { Send(ctx, 404, Err("sessione inesistente")); return false; }
            chiave = k;

            byte[] dati = Convert.FromBase64String((string)busta["ciphertext"]!);
            byte[] tag = Convert.FromBase64String((string)busta["tag"]!);
            byte[] nonce = dati[..12];
            byte[] cifrato = dati[12..];
            byte[] chiaro = new byte[cifrato.Length];

            using AesGcm aes = new AesGcm(chiave, 16);
            aes.Decrypt(nonce, cifrato, tag, chiaro);   // eccezione se il tag non torna

            msg = JsonNode.Parse(Encoding.UTF8.GetString(chiaro));
            if (msg == null) throw new Exception();
            return true;
        }
        catch
        {
            // decifratura o tag falliti: 400 senza elaborare nulla
            Send(ctx, 400, Err("busta non valida o manomessa"));
            return false;
        }
    }

    // ---------- Validazione con lo schema (null = valido) ----------
    static string? Valida(JsonSchema schema, JsonNode nodo)
    {
        JsonElement elemento = JsonDocument.Parse(nodo.ToJsonString()).RootElement;
        EvaluationResults r = schema.Evaluate(elemento, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true
        });
        return r.IsValid ? null : JsonSerializer.Serialize(r);
    }

    static JsonSchema Carica(string nome)
    {
        return JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "schemas", nome));
    }

    // ---------- Risposte ----------
    static string Err(string testo)
    {
        return new JsonObject { ["error"] = testo }.ToJsonString();
    }

    static void Send(HttpListenerContext ctx, int code, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    // risposta cifrata: ciphertext = nonce (12 byte) + dati cifrati
    static void Reply(HttpListenerContext ctx, int code, string sid, byte[] chiave, JsonNode msg)
    {
        byte[] chiaro = Encoding.UTF8.GetBytes(msg.ToJsonString());
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] cifrato = new byte[chiaro.Length];
        byte[] tag = new byte[16];

        using AesGcm aes = new AesGcm(chiave, 16);
        aes.Encrypt(nonce, chiaro, cifrato, tag);

        JsonObject busta = new JsonObject
        {
            ["session_id"] = sid,
            ["ciphertext"] = Convert.ToBase64String(nonce.Concat(cifrato).ToArray()),
            ["tag"] = Convert.ToBase64String(tag)
        };
        Send(ctx, code, busta.ToJsonString());
    }

    static BigInteger Hex(string s)
    {
        return BigInteger.Parse("0" + s, NumberStyles.HexNumber);
    }
}