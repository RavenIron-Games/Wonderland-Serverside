using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
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
    ///
    /// Every announcement goes through the one configured DiscordWebhookUrl. The payload is the
    /// webhook "execute" body: content (Discord markdown), plus username / avatar_url only when the
    /// matching setting is filled in - left out, the webhook posts under its own configured name and
    /// avatar. allowed_mentions is always sent: with DiscordMention empty it is an empty parse list,
    /// so a character named "@everyone" can never ping the channel through a join message; with it
    /// set, only the exact role / user ids (or everyone/here) found in that setting are allowed.
    /// </summary>
    public static class DiscordWebhook
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private const int ContentLimit = 2000; // Discord's hard cap on message content
        private static string? _warnedAvatar;

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
                        string detail = "";
                        try { detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }
                        if (detail.Length > 300) detail = detail.Substring(0, 300) + "...";
                        WonderlandDebug.LogWarning($"[DiscordNotify] webhook post returned {(int)response.StatusCode} {response.StatusCode}{(detail.Length > 0 ? ": " + detail : ".")}");
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
            if (content.Length > ContentLimit)
            {
                content = content.Substring(0, ContentLimit - 1) + "…";
            }
            var sb = new StringBuilder();
            sb.Append("{\"content\":\"").Append(JsonEscape(content)).Append('"');

            string username = WonderlandConfig.DiscordUsername?.Value;
            if (!string.IsNullOrWhiteSpace(username))
            {
                sb.Append(",\"username\":\"").Append(JsonEscape(username)).Append('"');
            }

            string avatar = WonderlandConfig.DiscordAvatarUrl?.Value;
            if (!string.IsNullOrWhiteSpace(avatar))
            {
                // Discord rejects the whole post (400) on a malformed avatar_url, so a pasted link with a stray
                // character would silently kill every announcement - validate here and warn once instead.
                if (Uri.TryCreate(avatar.Trim(), UriKind.Absolute, out Uri uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
                {
                    sb.Append(",\"avatar_url\":\"").Append(JsonEscape(uri.AbsoluteUri)).Append('"');
                    _warnedAvatar = null;
                }
                else if (_warnedAvatar != avatar)
                {
                    _warnedAvatar = avatar;
                    WonderlandDebug.LogWarning("[DiscordNotify] DiscordAvatarUrl is not an absolute http(s) URL - posting without it until it is fixed.");
                }
            }

            sb.Append(",\"allowed_mentions\":").Append(BuildAllowedMentions(WonderlandConfig.DiscordMention?.Value));
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Whitelists exactly what DiscordMention contains: &lt;@&amp;id&gt; role(s), &lt;@id&gt; /
        /// &lt;@!id&gt; user(s), and @everyone / @here via parse. Nothing else in the message can ping.</summary>
        private static string BuildAllowedMentions(string? mention)
        {
            var roles = new List<string>();
            var users = new List<string>();
            var parse = new List<string>();
            if (!string.IsNullOrWhiteSpace(mention))
            {
                foreach (Match m in Regex.Matches(mention, @"<@&([0-9]+)>")) roles.Add(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(mention, @"<@!?([0-9]+)>")) users.Add(m.Groups[1].Value);
                if (mention.Contains("@everyone") || mention.Contains("@here")) parse.Add("\"everyone\"");
            }

            var sb = new StringBuilder("{\"parse\":[").Append(string.Join(",", parse)).Append(']');
            if (roles.Count > 0) sb.Append(",\"roles\":[\"").Append(string.Join("\",\"", roles)).Append("\"]");
            if (users.Count > 0) sb.Append(",\"users\":[\"").Append(string.Join("\",\"", users)).Append("\"]");
            return sb.Append('}').ToString();
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
