using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wonderland.Core;

namespace Wonderland.Subsystems.DiscordNotify
{
    /// <summary>
    /// Fire-and-forget Discord webhook sender. One shared HttpClient (reused across calls -
    /// a new instance per request risks socket exhaustion under Mono). Delivery is best-effort:
    /// a failed or rate-limited post is logged and dropped, never retried, since nothing else in
    /// the mod depends on an announcement actually landing.
    /// </summary>
    public static class DiscordWebhook
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public static bool IsConfigured()
        {
            return WonderlandConfig.DiscordNotifyEnabled?.Value == true
                && !string.IsNullOrWhiteSpace(WonderlandConfig.DiscordWebhookUrl?.Value);
        }

        /// <summary>Fire-and-forget - never blocks the caller (RPC handlers, the world-ready hook).</summary>
        public static void Send(string content)
        {
            if (!IsConfigured() || string.IsNullOrEmpty(content))
            {
                return;
            }

            _ = SendInternal(content, CancellationToken.None);
        }

        /// <summary>
        /// Blocks up to timeoutMs. Only for the server-offline message from Plugin.OnDestroy: the
        /// process can exit immediately after Shutdown() returns, which would kill a fire-and-forget
        /// task before it ever gets to run.
        /// </summary>
        public static void SendBlocking(string content, int timeoutMs = 3000)
        {
            if (!IsConfigured() || string.IsNullOrEmpty(content))
            {
                return;
            }

            try
            {
                using (var cts = new CancellationTokenSource(timeoutMs))
                {
                    SendInternal(content, cts.Token).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[DiscordNotify] shutdown post failed: {ex.Message}");
            }
        }

        private static async Task SendInternal(string content, CancellationToken token)
        {
            try
            {
                string json = BuildPayload(content);
                using (var body = new StringContent(json, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await Client.PostAsync(WonderlandConfig.DiscordWebhookUrl.Value, body, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        WonderlandDebug.LogWarning($"[DiscordNotify] webhook post returned {(int)response.StatusCode} {response.StatusCode}.");
                    }
                }
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[DiscordNotify] webhook post failed: {ex.Message}");
            }
        }

        private static string BuildPayload(string content)
        {
            var sb = new StringBuilder();
            sb.Append("{\"content\":\"").Append(JsonEscape(content)).Append('"');

            string username = WonderlandConfig.DiscordUsername?.Value;
            if (!string.IsNullOrWhiteSpace(username))
            {
                sb.Append(",\"username\":\"").Append(JsonEscape(username)).Append('"');
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static string JsonEscape(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
