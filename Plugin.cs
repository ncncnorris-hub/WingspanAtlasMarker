using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Storage;           // Storage.Save
// Game types live in the global namespace of the ScriptAssembly interop image.

namespace WingspanAtlasMarker
{
    [BepInPlugin(Guid, "Wingspan Atlas Marker", "1.6.0")]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.ncn.wingspanatlasmarker";
        internal static ManualLogSource Logger;

        // ANSI / virtual-terminal support for a bright highlight bar on black.
        private static bool _ansi;
        private const int STD_OUTPUT_HANDLE = -11;
        private const uint ENABLE_VT = 0x0004;
        [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int n);
        [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr h, out uint mode);
        [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr h, uint mode);

        private static void TryEnableAnsi()
        {
            try
            {
                var h = GetStdHandle(STD_OUTPUT_HANDLE);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) return;
                if (!GetConsoleMode(h, out uint mode)) return;
                if (SetConsoleMode(h, mode | ENABLE_VT)) _ansi = true;
            }
            catch { }
        }

        // Throttle on the GLOBAL frame counter (immune to extra GameLoader
        // instances and to the deltaTime feedback that froze an earlier probe).
        private const int FrameStep = 15;
        private static int _nextFrame;
        private static string _lastScene = "";

        // Signature (id+location) of the unplayed birds last printed; we only
        // re-print to the console when this signature actually changes.
        private static string _lastKey = "";

        private static readonly string[] NestNames = { "None", "Ground", "Bowl", "Platform", "Cavity", "Wild" };

        // ANSI bright-background codes (black text) cycled per bird line:
        // yellow, green, cyan, magenta, white, red.
        private static readonly string[] BgColors = { "103", "102", "106", "105", "107", "101" };

        // Held to keep the native callback alive. The console filter hides
        // Message-level logs; we relay select Unity Debug.Log lines back at
        // Warning level so they survive while the rest of the spam stays hidden.
        private static Application.LogCallback _logCb;
        private static readonly string[] RelayTags = { "[Operations]" };

        public override void Load()
        {
            Logger = Log;
            // Best-effort: let the console render emoji / non-ASCII. May be ignored
            // depending on the console host & font, but the messages stay readable.
            try { System.Console.OutputEncoding = new UTF8Encoding(false); } catch { }
            TryEnableAnsi();
            Logger.LogInfo($"WingspanAtlasMarker v1.6.0 (relay [Operations], ansi={_ansi}) starting...");
            try { new Harmony(Guid).PatchAll(); Logger.LogInfo("Patched GameLoader.Update."); }
            catch (Exception e) { Logger.LogError($"Setup failed: {e}"); }
            try
            {
                _logCb = DelegateSupport.ConvertDelegate<Application.LogCallback>(
                    new Action<string, string, LogType>(OnUnityLog));
                Application.add_logMessageReceived(_logCb);
            }
            catch (Exception e) { Logger.LogError($"Log relay setup failed: {e}"); }
        }

        // Relay select Unity logs (hidden by the Message-level console filter)
        // back at Warning level so they show alongside our alerts.
        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            try
            {
                if (string.IsNullOrEmpty(condition)) return;
                for (int i = 0; i < RelayTags.Length; i++)
                    if (condition.IndexOf(RelayTags[i], StringComparison.Ordinal) >= 0)
                    {
                        Logger.LogWarning(condition);
                        return;
                    }
            }
            catch { }
        }

        internal static void Tick()
        {
            int f = Time.frameCount;
            if (f < _nextFrame) return;
            _nextFrame = f + FrameStep;
            try { Refresh(); }
            catch (Exception e) { Logger.LogError($"[Refresh] {e}"); }
        }

        private static void Refresh()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene != _lastScene)
            {
                _lastScene = scene;
                if (scene != "Game") _lastKey = "";
            }

            // Only act inside an actual match. The Bird Atlas (menu scene 'Init')
            // already shows revealed/blank, so no hint is needed there.
            if (scene != "Game")
                return;

            var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<BirdCardView>());
            if (arr == null || arr.Length == 0)
            {
                _lastKey = "";
                return;
            }

            var played = Save.I?.BirdAtlasSave?.UnlockedBirdsID;

            // Pre-fetch the container transforms ONCE per refresh (only a handful
            // of instances). Classifying each card then costs a single cheap native
            // Transform.IsChildOf call instead of a generic GetComponentInParent
            // walk per card (that walk froze the game when previews spawned many
            // cards). Hand = local player's hand (skip the automa/AI hand).
            var handTfs = new List<Transform>();
            var trayTfs = new List<Transform>();
            try
            {
                var hands = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<PlayerHandUI>());
                for (int i = 0; i < hands.Length; i++)
                {
                    var h = hands[i].TryCast<PlayerHandUI>();
                    if (h == null || h.IsAutoma) continue;
                    handTfs.Add(h.transform);
                }
            }
            catch { }
            try
            {
                var trays = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<BirdDeckUI>());
                for (int i = 0; i < trays.Length; i++)
                {
                    var t = trays[i].TryCast<BirdDeckUI>();
                    if (t == null) continue;
                    trayTfs.Add(t.transform);
                }
            }
            catch { }

            // Collect unplayed birds that are actionable this turn: in the local
            // player's hand or face-up in the deck tray. Dedup by id; the same
            // bird never appears in both places at once. Sorted by id so the
            // change signature is order-stable across frames.
            var current = new SortedDictionary<int, KeyValuePair<string, string>>();
            for (int i = 0; i < arr.Length; i++)
            {
                BirdCardView card;
                try { card = arr[i].TryCast<BirdCardView>(); }
                catch { continue; }
                if (card == null) continue;

                try
                {
                    int id = (int)card.BirdID;
                    if (id <= 0 || id == 999999) continue; // Any/None placeholder, not a real bird
                    if (played != null && played.Contains(id)) continue;
                    if (current.ContainsKey(id)) continue;

                    var ct = card.transform;
                    string label = null;
                    for (int j = 0; j < handTfs.Count; j++)
                        if (ct.IsChildOf(handTfs[j])) { label = "HAND"; break; }
                    if (label == null)
                        for (int j = 0; j < trayTfs.Count; j++)
                            if (ct.IsChildOf(trayTfs[j])) { label = "TRAY"; break; }
                    if (label == null) continue; // not hand/tray -> ignore

                    current[id] = new KeyValuePair<string, string>(CleanName(card.gameObject.name), label);
                }
                catch { /* skip a bad card, keep scanning */ }
            }

            // Re-print only when the (id+location) signature changed.
            var keySb = new StringBuilder();
            foreach (var kv in current)
                keySb.Append(kv.Key).Append(kv.Value.Value).Append('|');
            string key = keySb.ToString();
            if (key == _lastKey) return;
            _lastKey = key;

            if (current.Count == 0)
            {
                Logger.LogInfo(">> no unplayed birds in hand/tray");
                return;
            }

            // One bird per line. Adjacent lines get different bright highlight
            // backgrounds (cycled) so multiple birds are easy to tell apart.
            Logger.LogWarning(_ansi
                ? $"\x1b[30;100m UNPLAYED ({current.Count}) \x1b[0m"
                : $"===== UNPLAYED ({current.Count}) =====");

            int ci = 0;
            foreach (var kv in current)
            {
                string line = $" {kv.Value.Key}({kv.Key}) [{kv.Value.Value}]{Stats(kv.Key)} ";
                if (_ansi)
                    Logger.LogWarning($"\x1b[30;{BgColors[ci % BgColors.Length]}m{line}\x1b[0m");
                else
                    Logger.LogWarning($"!!! {line}");
                ci++;
            }
        }

        // Static card stats from BirdDatabase, keyed by the bird id we already
        // have. Works for any bird regardless of play state. Degrades to "" on any
        // failure so the alert still shows name + location.
        private static string Stats(int id)
        {
            try
            {
                var bd = BirdDatabase.Get(id);
                if (bd == null) return "";
                int vp = bd.VictoryPoints.GetDecrypted();
                int eggs = bd.EggLimit.GetDecrypted();
                int ws = bd.WingspanCM.GetDecrypted();
                int nest = (int)bd.NestType;
                string nestName = (nest >= 0 && nest < NestNames.Length) ? NestNames[nest] : nest.ToString();
                return $" {{VP:{vp} Eggs:{eggs} WS:{ws} Nest:{nestName} Hab:{Hab(bd)}}}";
            }
            catch (Exception e) { Logger.LogError($"[Stats {id}] {e.Message}"); return ""; }
        }

        // Playable habitats, e.g. "F/G/W" (Forest/Grass/Wetlands) or "none".
        private static string Hab(BirdData bd)
        {
            var sb = new StringBuilder();
            if (bd.HabitatForest.GetDecrypted()) sb.Append('F');
            if (bd.HabitatGrass.GetDecrypted()) { if (sb.Length > 0) sb.Append('/'); sb.Append('G'); }
            if (bd.HabitatWetlands.GetDecrypted()) { if (sb.Length > 0) sb.Append('/'); sb.Append('W'); }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        // "Eurasian_Jay_190(Clone)" -> "Eurasian Jay"
        private static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            int clone = raw.IndexOf("(Clone)", StringComparison.Ordinal);
            if (clone >= 0) raw = raw.Substring(0, clone);
            int us = raw.LastIndexOf('_');
            if (us > 0 && us < raw.Length - 1)
            {
                bool allDigits = true;
                for (int i = us + 1; i < raw.Length; i++)
                    if (!char.IsDigit(raw[i])) { allDigits = false; break; }
                if (allDigits) raw = raw.Substring(0, us);
            }
            return raw.Replace('_', ' ');
        }
    }

    [HarmonyPatch(typeof(GameLoader), "Update")]
    static class HB_GameLoaderUpdate
    {
        static void Postfix() => Plugin.Tick();

        // GameLoader.Update throws NREs on some frames; Unity normally swallows them
        // but Harmony surfaces them to Il2CppInterop and floods the log. Restore the
        // original behavior by swallowing the game's own exception.
        static Exception Finalizer(Exception __exception) => null;
    }
}
