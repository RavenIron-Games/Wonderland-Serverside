using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Wonderland.Core;

namespace Wonderland.Subsystems.BarrkBot
{
    /// <summary>
    /// The facts the dedicated server itself observes, kept as one registry and persisted beside the
    /// config as Wonderland.BarrkBot.&lt;world&gt;.dat (JSON, but deliberately NOT a barrkbot_*.json name -
    /// BarrkBOT sweeps BepInEx/config up to three levels deep for /^barrkbot[_-].*\.json$/i and would
    /// otherwise see two files claiming the same facts). Everything here is server-measured, per
    /// libs-Tools/IMPLEMENTATIONS/BarrkBOTExports.md §6 (the client-side trap): peer connect/disconnect
    /// for sessions and connected time, the character ZDO's s_dead edge for deaths (PlayerLifecycleWatch),
    /// ZoneSystem global keys for bosses, and the mod's own engine counters for automation. Nothing is
    /// taken from a client's word.
    ///
    /// Players are keyed by the stable PlayerProfile id (ZDOVars.s_playerID on the character ZDO -
    /// PLAYER-IDENTITY-FACTS.md), never the peer uid. A peer whose character has not spawned yet has no
    /// row until it does; from then on sessions and connected time follow the PEER (ZNetPeer.m_uid, one
    /// per connection), not the character: a death removes the character ZDO from the server's view for
    /// 8-18 s (Game.RequestRespawn 10 s, then m_respawnLoadDuration 8 s before the new character id
    /// arrives), which always spans a 5 s sweep, so a character-presence edge would count every death as
    /// a new session. Name-keyed lookups are avoided for the same reason character names are not unique
    /// across accounts: the handshake facts (platform id, first join) are held per peer uid until the
    /// character row exists, then dropped when the peer leaves.
    /// </summary>
    public static class BarrkBotStats
    {
        public sealed class PlayerRow
        {
            public string name = "";
            public string platform_id = "";
            public string first_seen_at = "";
            public string last_seen_at = "";
            public int sessions_count;
            public long online_seconds;
            public int deaths_count;
            public int security_flags_count;
            public string welcomed_at;
        }

        public sealed class Registry
        {
            public int schema_version = 1;
            public string tracking_since = Now();
            public Dictionary<string, PlayerRow> players = new Dictionary<string, PlayerRow>();
            public int peak_players_online_count;
            public Dictionary<string, long> transfers_by_subsystem = new Dictionary<string, long>();
            public Dictionary<string, Dictionary<string, long>> transfers_by_item = new Dictionary<string, Dictionary<string, long>>();
            public long raids_blocked_count;
            public long spawns_culled_count;
            public long security_flags_count;
            public Dictionary<string, string> boss_defeated_at = new Dictionary<string, string>();
        }

        private static Registry _registry = new Registry();
        private static bool _loaded;
        // Resolved once in TryLoad and used for every later save: ZNet.instance is null again by the time
        // Plugin.OnDestroy can run, and re-resolving then is how a stray Wonderland.Cache.default.dat once
        // got written (HANDOFF-0.8.0 §5.5).
        private static string _path = "";
        private static bool _dirty;
        // Set when the existing file could not be read AND could not be moved aside: never overwrite it.
        private static bool _readOnly;

        // Stable id -> the peer uid it was last seen connected under. The session edge is "this id is
        // now under a different connection" (or a first sighting), so a respawn gap is not a session.
        private static readonly Dictionary<string, long> PeerByKey = new Dictionary<string, long>();
        // Which stable ids are currently online (their peer is still connected) - exported as online_now.
        private static readonly HashSet<string> OnlineNow = new HashSet<string>();
        // Handshake facts by peer uid until the character row exists; cleared on disconnect.
        private static readonly Dictionary<long, string> PlatformByPeer = new Dictionary<long, string>();
        private static readonly HashSet<long> PendingWelcomePeers = new HashSet<long>();

        public static Registry Current => _registry;
        public static bool Loaded => _loaded;
        public static IReadOnlyCollection<string> OnlineIds => OnlineNow;
        /// <summary>BarrkBotExportEnabled, read at use time (hot-reload). Off = dormant: no sweep, no export,
        /// hooks ignored; what was recorded stays in the .dat.</summary>
        public static bool Enabled => WonderlandConfig.BarrkBotExportEnabled?.Value == true;

        // ---- hooks called from the rest of the mod --------------------------------------------------

        /// <summary>PeerJoinLeaveHook postfix: the handshake completed. platformId is the same string the
        /// vanilla player history stores ("Steam_7656...", "PlayStation_..."); the character row is
        /// created later by Sweep once the client's Player.SetPlayerID has written s_playerID.</summary>
        public static void OnPeerJoined(long peerUid, string platformId, bool firstJoin)
        {
            if (!Enabled || peerUid == 0L) return;
            PlatformByPeer[peerUid] = platformId ?? "";
            if (firstJoin) PendingWelcomePeers.Add(peerUid);
        }

        /// <summary>ZNet.Disconnect prefix: every runtime disconnect (timeout, kick, RPC_Disconnect, handshake
        /// failure) passes through it, so the id(s) this connection carried are stamped and released here.
        /// It does NOT fire at shutdown - ZNet.StopAll only sends the outgoing Disconnect RPC, disposes the
        /// peers and clears the list - which is what OnShutdown is for.</summary>
        public static void OnPeerLeft(long peerUid)
        {
            PlatformByPeer.Remove(peerUid);
            PendingWelcomePeers.Remove(peerUid);
            if (!_loaded || peerUid == 0L) return;
            string now = Now();
            var gone = new List<string>();
            foreach (KeyValuePair<string, long> kv in PeerByKey)
            {
                if (kv.Value == peerUid) gone.Add(kv.Key);
            }
            foreach (string key in gone)
            {
                PeerByKey.Remove(key);
                OnlineNow.Remove(key);
                if (_registry.players.TryGetValue(key, out PlayerRow row))
                {
                    row.last_seen_at = now;
                    _dirty = true;
                }
            }
        }

        public static void OnDeath(long playerId, string playerName)
        {
            if (!Enabled) return;
            PlayerRow row = Ensure(playerId, playerName);
            if (row == null) return;
            row.deaths_count++;
            _dirty = true;
        }

        public static void OnBossDefeated(string bossName)
        {
            if (!Enabled || string.IsNullOrEmpty(bossName)) return;
            _registry.boss_defeated_at[bossName] = Now();
            _dirty = true;
        }

        public static void OnTransfer(string subsystem, string itemName, int amount)
        {
            if (!Enabled || amount <= 0) return;
            string sub = subsystem ?? "";
            string item = ItemKey(itemName);
            _registry.transfers_by_subsystem[sub] = Get(_registry.transfers_by_subsystem, sub) + amount;
            if (!_registry.transfers_by_item.TryGetValue(sub, out Dictionary<string, long> byItem) || byItem == null)
            {
                byItem = new Dictionary<string, long>();
                _registry.transfers_by_item[sub] = byItem;
            }
            byItem[item] = Get(byItem, item) + amount;
            _dirty = true;
        }

        public static void OnRaidBlocked() { if (!Enabled) return; _registry.raids_blocked_count++; _dirty = true; }
        public static void OnSpawnCulled() { if (!Enabled) return; _registry.spawns_culled_count++; _dirty = true; }

        /// <summary>AuditLog.Flag. Attributed to a row only by stable id (VitalsGuard / PositionWatch pass
        /// it); a flag that only carries a name or a place counts in the total alone - names collide.</summary>
        public static void OnSecurityFlag(string who, long playerId)
        {
            if (!Enabled) return;
            _registry.security_flags_count++;
            PlayerRow row = playerId != 0L ? Ensure(playerId, who) : null;
            if (row != null) row.security_flags_count++;
            _dirty = true;
        }

        /// <summary>Names of the ids currently online, from their rows - the same base as online_now and the
        /// peak, so the server block and the rows never disagree about who is on.</summary>
        public static List<string> OnlineNames()
        {
            var names = new List<string>();
            foreach (string key in OnlineNow)
            {
                if (_registry.players.TryGetValue(key, out PlayerRow row) && !string.IsNullOrEmpty(row.name)) names.Add(row.name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        // ---- periodic sweep --------------------------------------------------------------------------

        /// <summary>Called by the subsystem every sweep with the seconds elapsed since the last one. Creates
        /// rows from ConnectedCharacters (the only server-side view of who is playing), then attributes
        /// sessions, connected time, first/last seen and the online set per PEER: an id whose peer is still
        /// connected stays online through a respawn gap; an id that reappears under a new connection starts
        /// a new session.</summary>
        public static void Sweep(float elapsedSeconds, IReadOnlyList<Core.Data.ConnectedCharacter> characters)
        {
            if (!_loaded) return;
            string now = Now();
            long elapsed = (long)Math.Round(elapsedSeconds);

            var connected = new HashSet<long>();
            if (ZNet.instance != null)
            {
                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                {
                    if (peer != null) connected.Add(peer.m_uid);
                }
            }

            // 1. Characters present right now: bind their stable id to the connection carrying it. An id
            //    already bound to a connection that is still up stays with it (two live peers presenting one
            //    profile id - a copied character file - would otherwise flip the binding every sweep).
            foreach (Core.Data.ConnectedCharacter c in characters)
            {
                long id = c.PlayerId;
                if (id == 0L || c.Peer == null) continue;
                string key = id.ToString();
                PlayerRow row = Ensure(id, c.Name);
                if (row == null) continue;
                long peerUid = c.Peer.m_uid;
                bool bound = PeerByKey.TryGetValue(key, out long previousPeer);
                if (bound && previousPeer != peerUid && connected.Contains(previousPeer))
                {
                    continue;
                }
                if (!bound || previousPeer != peerUid)
                {
                    row.sessions_count++; // first sighting, or the same id under a new connection
                    PeerByKey[key] = peerUid;
                    _dirty = true;
                }
                if (PlatformByPeer.TryGetValue(peerUid, out string platform) && !string.IsNullOrEmpty(platform) && row.platform_id != platform)
                {
                    row.platform_id = platform;
                    _dirty = true;
                }
                if (PendingWelcomePeers.Remove(peerUid))
                {
                    row.welcomed_at = now;
                    _dirty = true;
                }
            }

            // 2. Every id whose connection is still up is online and earns this sweep's seconds, character
            //    present or not (dead and respawning is still connected). A connection that vanished without
            //    passing through OnPeerLeft (should not happen) is dropped here.
            OnlineNow.Clear();
            var stale = new List<string>();
            foreach (KeyValuePair<string, long> kv in PeerByKey)
            {
                if (!connected.Contains(kv.Value))
                {
                    stale.Add(kv.Key);
                    continue;
                }
                OnlineNow.Add(kv.Key);
                if (elapsed > 0 && _registry.players.TryGetValue(kv.Key, out PlayerRow row))
                {
                    row.last_seen_at = now;
                    row.online_seconds += elapsed;
                    _dirty = true;
                }
            }
            foreach (string key in stale) PeerByKey.Remove(key);

            if (OnlineNow.Count > _registry.peak_players_online_count)
            {
                _registry.peak_players_online_count = OnlineNow.Count;
                _dirty = true;
            }
        }

        /// <summary>The export is being switched off at runtime: nobody is shown online, but the id-to-
        /// connection bindings are kept (OnPeerLeft keeps them accurate while dormant) so re-enabling does not
        /// count a session for everyone still connected.</summary>
        public static void MarkAllOffline()
        {
            OnlineNow.Clear();
        }

        /// <summary>Plugin.OnDestroy. The peer list is already empty (ZNet.StopAll), so the last partial sweep
        /// interval is credited from the bindings and everyone is marked offline.</summary>
        public static void OnShutdown(float secondsSinceLastSweep)
        {
            if (!_loaded) return;
            string now = Now();
            long elapsed = (long)Math.Round(Math.Max(0f, secondsSinceLastSweep));
            foreach (string key in PeerByKey.Keys)
            {
                if (_registry.players.TryGetValue(key, out PlayerRow row))
                {
                    row.last_seen_at = now;
                    row.online_seconds += elapsed;
                    _dirty = true;
                }
            }
            OnlineNow.Clear();
            PeerByKey.Clear();
        }

        // ---- persistence -----------------------------------------------------------------------------

        /// <summary>The registry path: pinned at load; before that, resolved from the live world name.</summary>
        public static string FilePath()
        {
            if (_loaded) return _path;
            string world = ZNet.instance != null ? ZNet.instance.GetWorldName() : "";
            return Path.Combine(BepInEx.Paths.ConfigPath, $"Wonderland.BarrkBot.{world}.dat");
        }

        /// <summary>Loads once the world name is known (lazy, from OnUpdate - ZNet.instance can still be null
        /// at ZNetScene.Awake, which is how a stray Wonderland.Cache.default.dat once got written). An
        /// unreadable file is moved aside (never silently overwritten by the next save); if even that fails
        /// the registry runs in memory only for this session.</summary>
        public static bool TryLoad()
        {
            if (_loaded) return true;
            if (ZNet.instance == null || string.IsNullOrEmpty(ZNet.instance.GetWorldName())) return false;
            string path = FilePath();
            if (File.Exists(path))
            {
                try
                {
                    Registry loaded = JsonConvert.DeserializeObject<Registry>(File.ReadAllText(path));
                    if (loaded != null)
                    {
                        loaded.players ??= new Dictionary<string, PlayerRow>();
                        loaded.transfers_by_subsystem ??= new Dictionary<string, long>();
                        loaded.transfers_by_item ??= new Dictionary<string, Dictionary<string, long>>();
                        foreach (string sub in new List<string>(loaded.transfers_by_item.Keys))
                        {
                            loaded.transfers_by_item[sub] ??= new Dictionary<string, long>();
                        }
                        loaded.boss_defeated_at ??= new Dictionary<string, string>();
                        if (string.IsNullOrEmpty(loaded.tracking_since)) loaded.tracking_since = Now();
                        foreach (PlayerRow row in loaded.players.Values)
                        {
                            row.name ??= "";
                            row.platform_id ??= "";
                            row.first_seen_at ??= "";
                            row.last_seen_at ??= "";
                        }
                        _registry = loaded;
                    }
                    WonderlandDebug.LogAlways($"[BarrkBot] loaded {_registry.players.Count} player row(s) from {path}.");
                }
                catch (Exception ex)
                {
                    string aside = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
                    try
                    {
                        File.Move(path, aside);
                        WonderlandDebug.LogWarning($"[BarrkBot] could not read {path} ({ex.Message}) - moved it to {aside} and starting a fresh registry.");
                    }
                    catch (Exception moveEx)
                    {
                        _readOnly = true;
                        WonderlandDebug.LogWarning($"[BarrkBot] could not read {path} ({ex.Message}) and could not move it aside ({moveEx.Message}) - running in memory only; the file is left untouched and nothing will be saved this session.");
                    }
                }
            }
            _loaded = true;
            _path = path;
            return true;
        }

        public static void SaveIfDirty(bool force = false)
        {
            if (!_loaded || _readOnly || (!_dirty && !force)) return;
            try
            {
                WriteAtomic(_path, JsonConvert.SerializeObject(_registry, Formatting.Indented));
                _dirty = false;
            }
            catch (Exception ex)
            {
                WonderlandDebug.LogWarning($"[BarrkBot] registry save failed: {ex.Message}");
            }
        }

        /// <summary>Temp-then-rename. File.Replace has thrown on some Mono/filesystem combinations (Njord's
        /// writer hit it), so it falls back to delete + move - still never a partial file at the final path.</summary>
        public static void WriteAtomic(string path, string text)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text); // this overload is UTF-8 without a BOM
            try
            {
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            catch (Exception)
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
        }

        // ---- helpers ---------------------------------------------------------------------------------

        private static PlayerRow Ensure(long playerId, string name)
        {
            if (!_loaded || playerId == 0L) return null;
            string key = playerId.ToString();
            if (!_registry.players.TryGetValue(key, out PlayerRow row))
            {
                row = new PlayerRow { first_seen_at = Now() };
                _registry.players[key] = row;
                _dirty = true;
            }
            if (!string.IsNullOrEmpty(name) && row.name != name)
            {
                row.name = name;
                _dirty = true;
            }
            return row;
        }

        private static long Get(Dictionary<string, long> d, string k) => d.TryGetValue(k, out long v) ? v : 0L;

        /// <summary>"$item_silverore" -> "silverore": the ledger records the localisation token.</summary>
        private static string ItemKey(string itemName)
        {
            string s = itemName ?? "";
            if (s.StartsWith("$item_", StringComparison.OrdinalIgnoreCase)) s = s.Substring(6);
            else if (s.StartsWith("$", StringComparison.Ordinal)) s = s.Substring(1);
            return s.ToLowerInvariant();
        }

        public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }
}
