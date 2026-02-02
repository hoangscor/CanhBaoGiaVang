using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net.Http;
using System.Text.Json;
using System.Threading;
using GoldPriceAlertWinForms.Models;

namespace GoldPriceAlertWinForms.Providers
{
    public sealed class MetalPriceApiProvider
    {
        private readonly HttpClient _http;
        public MetalPriceApiProvider(HttpClient http) => _http = http;

        public string Name => "MetalpriceAPI";

        public async Task<PriceQuote> GetLatestGoldAsync(AppSettings settings, CancellationToken ct)
        {
            string apiKey = settings.Metal.ApiKey?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Missing MetalpriceAPI ApiKey (check settings.json).");

            string region = (settings.Metal.Region ?? "US").Trim().ToUpperInvariant();
            string host = region == "EU" ? "api-eu.metalpriceapi.com" : "api.metalpriceapi.com";

            string baseCur = (settings.BaseCurrency ?? "USD").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(baseCur)) baseCur = "USD";

            string displayCur = (settings.DisplayCurrency ?? baseCur).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(displayCur)) displayCur = baseCur;

            // /v1/latest?api_key=...&base=USD&currencies=EUR,XAU... :contentReference[oaicite:7]{index=7}
            string url =
                $"https://{host}/v1/latest?api_key={Uri.EscapeDataString(apiKey)}&base={Uri.EscapeDataString(baseCur)}&currencies=XAU,{Uri.EscapeDataString(displayCur)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("GoldPriceAlertWinForms/1.0");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var okEl) && okEl.ValueKind == JsonValueKind.False)
            {
                string info = root.TryGetProperty("error", out var errEl) ? errEl.ToString() : "Unknown error";
                throw new InvalidOperationException("MetalpriceAPI error: " + info);
            }

            string baseFromApi = root.TryGetProperty("base", out var baseEl) ? (baseEl.GetString() ?? baseCur) : baseCur;

            DateTimeOffset ts = DateTimeOffset.UtcNow;
            if (root.TryGetProperty("timestamp", out var tsEl) && tsEl.ValueKind == JsonValueKind.Number && tsEl.TryGetInt64(out var unix))
                ts = DateTimeOffset.FromUnixTimeSeconds(unix);

            if (!root.TryGetProperty("rates", out var ratesEl) || ratesEl.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("MetalpriceAPI response missing 'rates'.");

            // Price per ounce in base currency
            double basePerOunce;
            string baseXauKey = baseFromApi + "XAU";

            if (ratesEl.TryGetProperty(baseXauKey, out var baseXauEl) && baseXauEl.ValueKind == JsonValueKind.Number)
            {
                // Example: USDXAU = 1856.90 (base per oz)
                basePerOunce = baseXauEl.GetDouble();
            }
            else if (ratesEl.TryGetProperty("XAU", out var xauEl) && xauEl.ValueKind == JsonValueKind.Number)
            {
                // XAU means 1 base = XAU ounces => price per ounce = 1 / XAU
                double ouncePerBase = xauEl.GetDouble();
                if (ouncePerBase <= 0) throw new InvalidOperationException("Invalid XAU rate.");
                basePerOunce = 1.0 / ouncePerBase;
            }
            else
            {
                throw new InvalidOperationException("Cannot find XAU or BASEXAU in rates.");
            }

            // Convert base -> display currency (rate: 1 base = rate display)
            double baseToDisplay = 1.0;
            if (!string.Equals(displayCur, baseFromApi, StringComparison.OrdinalIgnoreCase))
            {
                if (ratesEl.TryGetProperty(displayCur, out var dispEl) && dispEl.ValueKind == JsonValueKind.Number)
                    baseToDisplay = dispEl.GetDouble();
                else
                    throw new InvalidOperationException("Cannot find display currency rate in response.");
            }

            double displayPricePerOunce = basePerOunce * baseToDisplay;

            return new PriceQuote
            {
                Timestamp = ts,
                Currency = displayCur,
                Unit = "oz",
                Price = displayPricePerOunce,
                Source = host
            };
        }
    }
}
