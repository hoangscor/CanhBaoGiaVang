using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using GoldPriceAlertWinForms.Models;

namespace GoldPriceAlertWinForms.Services
{
    public sealed class RemoteConfigService
    {
        private readonly HttpClient _http;
        public RemoteConfigService(HttpClient http) => _http = http;

        public async Task<RemoteConfig?> LoadAsync(string url, CancellationToken ct)
        {
            string json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("args", out var args) &&
                args.ValueKind == JsonValueKind.Object)
            {
                return ParseArgs(args);
            }

            return null;
        }

        private static RemoteConfig ParseArgs(JsonElement args)
        {
            return new RemoteConfig
            {
                PollMinutes = GetInt(args, "pollMin"),
                BaseCurrency = GetString(args, "base"),
                DisplayCurrency = GetString(args, "display"),
                Unit = GetString(args, "unit"),

                Min = GetDouble(args, "min"),
                Max = GetDouble(args, "max"),
                DropAbs = GetDouble(args, "dropAbs"),
                DropPct = GetDouble(args, "dropPct"),
                CooldownMinutes = GetInt(args, "cooldownMin"),

                EnableMin = GetBool(args, "enableMin"),
                EnableMax = GetBool(args, "enableMax"),
                EnableDropAbs = GetBool(args, "enableDropAbs"),
                EnableDropPct = GetBool(args, "enableDropPct"),

                ToAddresses = GetString(args, "to"),
                MetalApiKey = GetString(args, "metalApiKey"),
                Region = GetString(args, "region")
            };
        }

        private static string? GetString(JsonElement args, string key)
        {
            if (!args.TryGetProperty(key, out var el)) return null;
            if (el.ValueKind == JsonValueKind.String) return el.GetString();
            return el.ToString();
        }

        private static double? GetDouble(JsonElement args, string key)
        {
            var s = GetString(args, key);
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        private static int? GetInt(JsonElement args, string key)
        {
            var s = GetString(args, key);
            if (string.IsNullOrWhiteSpace(s)) return null;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        private static bool? GetBool(JsonElement args, string key)
        {
            var s = GetString(args, key);
            if (string.IsNullOrWhiteSpace(s)) return null;

            if (bool.TryParse(s, out var b)) return b;
            if (s == "1") return true;
            if (s == "0") return false;
            return null;
        }
    }
}
