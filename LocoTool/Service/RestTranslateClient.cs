using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocoTool.Service;

public sealed class RestTranslateClient
{
    private static readonly Uri Endpoint = new("https://translate.api.cloud.yandex.net/translate/v2/translate");
    private readonly HttpClient _http;
    private readonly string _authHeaderValue;
    private readonly string? _folderId;

    /// <param name="authHeaderValue">"Api-Key xxxxx" or "Bearer xxxxx"</param>
    /// <param name="folderId">Optional folder ID if required by REST.</param>
    public RestTranslateClient(HttpClient http, string authHeaderValue, string? folderId = null)
    {
        _http = http;
        _authHeaderValue = authHeaderValue;
        _folderId = folderId;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IEnumerable<string> texts,
        string target = "en",
        string? source = "zh",
        IEnumerable<(string src, string dst, bool exact)>? glossary = null,
        bool speller = false)
    {
        const int MaxCharsPerRequest = 10000; // API limit: sum of all texts per request

        var list = texts.ToList();
        var totalLen = list.Sum(s => s?.Length ?? 0);

        if (totalLen <= MaxCharsPerRequest)
        {
            return await SendOnceAsync(list, target, source, glossary, speller).ConfigureAwait(false);
        }

        // Split into multiple requests, preserving order
        var results = new string[list.Count];
        int start = 0;
        while (start < list.Count)
        {
            int sum = 0;
            int end = start;
            while (end < list.Count)
            {
                int add = list[end]?.Length ?? 0;
                if (sum + add > MaxCharsPerRequest && end > start) break;
                if (sum + add > MaxCharsPerRequest && end == start) { end++; break; } // single oversize item
                sum += add;
                end++;
            }

            var slice = list.GetRange(start, end - start);
            var translated = await SendOnceAsync(slice, target, source, glossary, speller).ConfigureAwait(false);
            for (int i = 0; i < translated.Count && (start + i) < results.Length; i++)
                results[start + i] = translated[i] ?? string.Empty;

            start = end;
        }

        return results;
    }

    private async Task<IReadOnlyList<string>> SendOnceAsync(
        IReadOnlyList<string> batch,
        string target,
        string? source,
        IEnumerable<(string src, string dst, bool exact)>? glossary,
        bool speller)
    {
        var req = new TranslateRequest
        {
            TargetLanguageCode = target,
            SourceLanguageCode = source,
            FolderId = _folderId,
            Format = "PLAIN_TEXT",
            Speller = speller,
            Texts = batch.ToList()
        };

        if (glossary is not null)
        {
            var pairs = glossary.ToList();
            if (pairs.Count > 0)
            {
                req.GlossaryConfig = new TranslateGlossaryConfig
                {
                    GlossaryData = new GlossaryData
                    {
                        GlossaryPairs = pairs.Select(p => new GlossaryPair
                        {
                            SourceText = p.src,
                            TranslatedText = p.dst,
                            Exact = p.exact
                        }).ToList()
                    }
                };
            }
        }

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        httpReq.Headers.TryAddWithoutValidation("Authorization", _authHeaderValue);
        httpReq.Content = JsonContent.Create(req,
            options: new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

        using var resp = await _http.SendAsync(httpReq).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Translate REST error {(int)resp.StatusCode}: {body}");

        var parsed = JsonSerializer.Deserialize<TranslateResponse>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new TranslateResponse();

        return parsed.Translations?.Select(t => t.Text ?? string.Empty).ToArray()
               ?? Array.Empty<string>();
    }

    // ====== DTO ======

    public sealed class TranslateRequest
    {
        [JsonPropertyName("folderId")] public string? FolderId { get; set; }
        [JsonPropertyName("targetLanguageCode")] public string TargetLanguageCode { get; set; } = "en";
        [JsonPropertyName("sourceLanguageCode")] public string? SourceLanguageCode { get; set; }
        [JsonPropertyName("format")] public string? Format { get; set; } = "PLAIN_TEXT";
        [JsonPropertyName("speller")] public bool? Speller { get; set; }
        [JsonPropertyName("texts")] public List<string> Texts { get; set; } = new();
        [JsonPropertyName("glossaryConfig")] public TranslateGlossaryConfig? GlossaryConfig { get; set; }
    }

    public sealed class TranslateGlossaryConfig
    {
        [JsonPropertyName("glossaryData")] public GlossaryData? GlossaryData { get; set; }
    }

    public sealed class GlossaryData
    {
        [JsonPropertyName("glossaryPairs")] public List<GlossaryPair> GlossaryPairs { get; set; } = new();
    }

    public sealed class GlossaryPair
    {
        [JsonPropertyName("sourceText")] public string SourceText { get; set; } = "";
        [JsonPropertyName("translatedText")] public string TranslatedText { get; set; } = "";
        [JsonPropertyName("exact")] public bool Exact { get; set; } = true;
    }

    public sealed class TranslateResponse
    {
        [JsonPropertyName("translations")] public List<TranslatedText>? Translations { get; set; }
    }

    public sealed class TranslatedText
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("detectedLanguageCode")] public string? DetectedLanguageCode { get; set; }
    }
}
