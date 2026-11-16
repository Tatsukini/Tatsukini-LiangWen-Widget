using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LiangWenWidget;

public enum ApiHealth { Ok, Crowded, Down }

public sealed record ProbeResult(ApiHealth Health, double LatencyMs, double? Balance, string? Currency, string Detail);

public sealed class DeepSeekClient
{
    private readonly HttpClient _http = new();

    public async Task<ProbeResult> ProbeAsync(AppConfig cfg, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, cfg.BaseUrl.TrimEnd('/') + "/user/balance");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSec));

            using var resp = await _http.SendAsync(req, cts.Token);
            sw.Stop();
            var body = await resp.Content.ReadAsStringAsync(cts.Token);

            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                return new ProbeResult(ApiHealth.Crowded, sw.Elapsed.TotalMilliseconds, null, null, "429 限流");

            if ((int)resp.StatusCode >= 500)
                return new ProbeResult(ApiHealth.Down, sw.Elapsed.TotalMilliseconds, null, null, $"HTTP {(int)resp.StatusCode}");

            if (!resp.IsSuccessStatusCode)
                return new ProbeResult(ApiHealth.Down, sw.Elapsed.TotalMilliseconds, null, null, $"HTTP {(int)resp.StatusCode}");

            double? balance = null;
            string? currency = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var err))
                {
                    string? msg = err.TryGetProperty("message", out var m) ? m.GetString() : err.ToString();
                    return new ProbeResult(ApiHealth.Ok, sw.Elapsed.TotalMilliseconds, null, null,
                        $"查询失败: {msg}");
                }

                // shape A: total_balance at root
                if (root.TryGetProperty("total_balance", out var tb) && tb.ValueKind == JsonValueKind.String)
                    balance = double.TryParse(tb.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
                if (root.TryGetProperty("currency", out var cur) && cur.ValueKind == JsonValueKind.String)
                    currency = cur.GetString();

                // shape B: sum over balance_infos (DeepSeek's actual layout)
                if (balance is null && root.TryGetProperty("balance_infos", out var infos)
                    && infos.ValueKind == JsonValueKind.Array && infos.GetArrayLength() > 0)
                {
                    double sum = 0; bool any = false;
                    foreach (var info in infos.EnumerateArray())
                    {
                        if (info.TryGetProperty("total_balance", out var ib) && ib.ValueKind == JsonValueKind.String
                            && double.TryParse(ib.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var iv))
                        { sum += iv; any = true; }
                        if (currency is null && info.TryGetProperty("currency", out var ic) && ic.ValueKind == JsonValueKind.String)
                            currency = ic.GetString();
                    }
                    if (any) balance = sum;
                }
            }
            catch { /* keep balance null */ }

            var health = sw.Elapsed.TotalMilliseconds >= cfg.CrowdedLatencyMs ? ApiHealth.Crowded : ApiHealth.Ok;
            return new ProbeResult(health, sw.Elapsed.TotalMilliseconds, balance, currency,
                health == ApiHealth.Crowded ? $"高延迟 {sw.Elapsed.TotalMilliseconds:F0}ms" : $"正常 {sw.Elapsed.TotalMilliseconds:F0}ms");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ProbeResult(ApiHealth.Down, sw.Elapsed.TotalMilliseconds, null, null, $"超时 >{cfg.TimeoutSec}s");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeResult(ApiHealth.Down, sw.Elapsed.TotalMilliseconds, null, null, ex.Message);
        }
    }
}
