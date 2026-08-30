using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime;
using UnityEngine;
#if MELON
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace KoreanTextFixer
{
    // 로더(BepInEx / MelonLoader)에 의존하지 않는 공통 코드.
    // 진입점은 Plugin.BepInEx.cs / Plugin.Melon.cs 가 각각 담당한다.

    internal static class KLog
    {
        internal static Action<string> Info = delegate { };
        internal static Action<string> Warn = delegate { };
        internal static Action<string> Error = delegate { };
    }

    internal static class Translations
    {
        internal static Dictionary<string, string> Dict = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static Dictionary<string, string> StrippedDict = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static readonly Regex TagRx = new Regex("<[^<>]{1,60}>", RegexOptions.Compiled);
        internal static Dictionary<string, string> Regions = new Dictionary<string, string>
        {
            { "Northtown", "노스타운" }, { "Westville", "웨스트빌" }, { "Downtown", "다운타운" },
            { "Docks", "부두" }, { "Suburbia", "교외" }, { "Uptown", "업타운" }
        };

        // baseDir: 번역 txt가 들어있는 폴더 (로더마다 위치가 다르다)
        internal static void Load(string baseDir)
        {
            string[] files = { "Korean_Base.txt", "Korean_Extracted.txt", "Korean_Composites.txt" };
            foreach (string f in files)
            {
                string p = Path.Combine(baseDir, f);
                if (!File.Exists(p)) continue;
                foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                {
                    if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("sr:") || line.StartsWith("r:")) continue;
                    int eq = -1;
                    for (int i = 0; i < line.Length; i++)
                    {
                        if (line[i] == '=' && (i == 0 || line[i - 1] != '\\')) { eq = i; break; }
                    }
                    if (eq < 1) continue;
                    string k = Unescape(line.Substring(0, eq));
                    string v = Unescape(line.Substring(eq + 1));
                    if (v.Length == 0) continue;
                    if (!Dict.ContainsKey(k)) Dict[k] = v;
                    string kt = k.Trim();
                    if (kt.Length > 0 && !Dict.ContainsKey(kt)) Dict[kt] = v.Trim();
                    if (k.IndexOf('<') >= 0)
                    {
                        string ks = TagRx.Replace(k, "").Trim();
                        if (ks.Length > 10) StrippedDict[ks] = v;
                    }
                }
            }
        }

        private static string Unescape(string s)
        {
            return s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\=", "=").Replace("\\\\", "\\");
        }
    }

    // 화면에 떠 있는 TMP_Text / UI.Text 를 훑으면서 남은 영어를 사전으로 교체한다.
    internal class Fixer
    {
        private float _nextRefresh;
        private float _nextStat;
        private int _errors;
        private bool _dead;
        private int _replaced;
        private int _cursor;
        private readonly List<TMP_Text> _tmps = new List<TMP_Text>(512);
        private readonly List<UnityEngine.UI.Text> _uis = new List<UnityEngine.UI.Text>(128);
        private readonly Dictionary<int, string> _seen = new Dictionary<int, string>();
        private const int PerFrame = 30;      // 프레임당 처리 개수 (스파이크 방지)
        private readonly float _refreshEvery; // 목록 재수집 주기

        private readonly bool _polling;

        internal Fixer(bool polling)
        {
            _polling = polling;
            // FindObjectsOfType는 비싸다. 후킹이 걸려 있으면 폴링은 후킹이 닿지 않는 텍스트
            // (프리팹에 박혀 있어 setter를 안 타는 것들)만 주우면 되므로 느리게 돌아도 된다.
            _refreshEvery = 3f;
#if MELON
            // 후킹이 걸려 있고 정적 문구는 미리 구워져 있으니, 폴링이 자주 돌 이유가 없다.
            // FindObjectsOfType는 씬이 클수록 비싸고 그때마다 프레임이 튄다.
            if (TmpHook.Installed) _refreshEvery = 30f;
#endif
        }

        private static readonly Regex Token = new Regex("<[^<>]{1,60}>|[^<]+", RegexOptions.Compiled);
        private static readonly Regex Paren = new Regex("^(\\s*)\\((.+)\\)(\\s*)$", RegexOptions.Compiled);
        private static readonly Regex Core = new Regex("^(\\s*)(.*?)(\\s*)$", RegexOptions.Compiled | RegexOptions.Singleline);

        public void Tick()
        {
            if (_dead) return;
            if (_polling)
            {
            try
            {
                int total = _tmps.Count + _uis.Count;
                if (_cursor >= total && Time.unscaledTime >= _nextRefresh)
                {
                    RefreshLists();
                    _nextRefresh = Time.unscaledTime + _refreshEvery;
                    _cursor = 0;
                    total = _tmps.Count + _uis.Count;
                }
                int end = Math.Min(_cursor + PerFrame, total);
                for (; _cursor < end; _cursor++)
                {
                    if (_cursor < _tmps.Count) ProcessTmp(_tmps[_cursor]);
                    else ProcessUi(_uis[_cursor - _tmps.Count]);
                }
            }
            catch (Exception e)
            {
                _errors++;
                KLog.Warn("fixer error(" + _errors + "): " + e.Message);
                if (_errors >= 5) { _dead = true; KLog.Error("too many errors - fixer disabled"); }
            }
            }
            if (Time.unscaledTime >= _nextStat)
            {
                _nextStat = Time.unscaledTime + 60f;
                // 후킹이 실제로 얼마나 자주 불리는지 봐야 프레임 부담의 출처를 가릴 수 있다
                long calls = HookCalls - _lastHookCalls, work = HookWork - _lastHookWork;
                _lastHookCalls = HookCalls; _lastHookWork = HookWork;
                KLog.Info("stats: replaced=" + _replaced + " tracked=" + (_tmps.Count + _uis.Count)
                    + " hookCache=" + HookCache.Count
                    + " hookCalls/s=" + (calls / 60) + " hookWork/s=" + (work / 60)
                    + " refreshMs=" + _refreshMs);
            }
        }

        private long _refreshMs;

        private void RefreshLists()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _tmps.Clear();
            _uis.Clear();
            var t = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<TMP_Text>());
            if (t != null) { foreach (var o in t) { var c = o.TryCast<TMP_Text>(); if (c != null) _tmps.Add(c); } }
            var u = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<UnityEngine.UI.Text>());
            if (u != null) { foreach (var o in u) { var c = o.TryCast<UnityEngine.UI.Text>(); if (c != null) _uis.Add(c); } }
            sw.Stop();
            _refreshMs = sw.ElapsedMilliseconds;
        }

        private void ProcessTmp(TMP_Text tmp)
        {
            try
            {
                if (tmp == null || !tmp.isActiveAndEnabled) return;
                string cur = tmp.text;
                string outp = Check(cur, tmp.GetInstanceID());
                if (outp != null) { tmp.text = outp; _replaced++; }
            }
            catch { }
        }

        private void ProcessUi(UnityEngine.UI.Text ut)
        {
            try
            {
                if (ut == null || !ut.isActiveAndEnabled) return;
                string cur = ut.text;
                string outp = Check(cur, ut.GetInstanceID());
                if (outp != null) { ut.text = outp; _replaced++; }
            }
            catch { }
        }

        // 후킹된 setter가 값을 쓰기 직전에 호출한다.
        // 같은 문자열이 매 프레임 들어오므로 결과(번역 없음 = null 포함)를 기억해 둔다.
        private static readonly Dictionary<string, string> HookCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private const int HookCacheLimit = 4096;

        // 진단용: 후킹이 불린 총 횟수와, 걸러지지 않고 실제 조회까지 간 횟수
        internal static long HookCalls;
        internal static long HookWork;
        private long _lastHookCalls;
        private long _lastHookWork;

        internal static string TranslateForHook(string src)
        {
            // 게임이 텍스트를 쓸 때마다 불린다. 캐시에 닿기 전에 확실한 것부터 싸게 걸러낸다.
            // 돈·시간·수량처럼 매 프레임 바뀌는 값은 사전에 있을 리 없고, 캐시에 쌓으면 캐시만 망가진다.
            HookCalls++;
            if (!HasLatinLetter(src)) return null;
            if (HasHangul(src)) return null; // 이미 번역된 값

            HookWork++;
            string cached;
            if (HookCache.TryGetValue(src, out cached)) return cached;

            string result = Translate(src);
#if MELON
            // 사전에 없으면 정규식·조립 번역을 아는 번역기에 물어본다
            if (result == null)
            {
                result = XUnityBridge.Translate(src);
                if (result == src) result = null;
            }
#endif
            if (HookCache.Count >= HookCacheLimit) HookCache.Clear();
            HookCache[src] = result;
            return result;
        }

        private static bool HasLatinLetter(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) return true;
            }
            return false;
        }

        private static bool HasHangul(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= 0xAC00 && c <= 0xD7A3) return true;
            }
            return false;
        }

        internal static void ClearHookCache()
        {
            HookCache.Clear();
        }

        private string Check(string cur, int id)
        {
            if (string.IsNullOrEmpty(cur)) return null;
            string last;
            if (_seen.TryGetValue(id, out last) && last == cur) return null;

            string fixedText = Translate(cur);
            if (fixedText != null && fixedText != cur)
            {
                _seen[id] = fixedText;
                return fixedText;
            }
            _seen[id] = cur;
            return null;
        }

        private static string Translate(string src)
        {
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c >= 0xAC00 && c <= 0xD7A3) return null;
            }
            string t = src.Trim();
            if (t.Length == 0) return null;

            // 1) 통짜 매칭
            string v;
            if (Translations.Dict.TryGetValue(t, out v)) return src.Replace(t, v);

            // 1-b) 태그 무시 매칭 (게임이 <h1>을 <color>로 바꿔 표시하는 경우)
            if (src.IndexOf('<') >= 0)
            {
                string stripped = Translations.TagRx.Replace(src, "").Trim();
                string sv;
                if (stripped.Length > 10 && Translations.StrippedDict.TryGetValue(stripped, out sv))
                {
                    var cm = Regex.Match(src, "<color[^<>]*>");
                    if (cm.Success) sv = sv.Replace("<h1>", cm.Value).Replace("</h>", "</color>");
                    else sv = Translations.TagRx.Replace(sv, "");
                    return sv;
                }
            }

            // 2) 태그 보존 조각 번역
            if (src.IndexOf('<') >= 0)
            {
                var sb = new StringBuilder(src.Length + 16);
                bool changed = false;
                foreach (Match m in Token.Matches(src))
                {
                    string tok = m.Value;
                    if (tok.Length > 0 && tok[0] == '<') { sb.Append(tok); continue; }
                    string rep = TranslatePlain(tok);
                    if (rep != null) { sb.Append(rep); changed = true; }
                    else sb.Append(tok);
                }
                if (changed) return sb.ToString();
                return null;
            }

            // 3) 평문 조각
            return TranslatePlain(src);
        }

        private static string TranslatePlain(string tok)
        {
            var cm = Core.Match(tok);
            string lead = cm.Groups[1].Value, core = cm.Groups[2].Value, tail = cm.Groups[3].Value;
            if (core.Length == 0) return null;

            string v;
            if (Translations.Dict.TryGetValue(core, out v)) return lead + v + tail;

            var pm = Paren.Match(tok);
            if (pm.Success)
            {
                string inner = pm.Groups[2].Value;
                string iv;
                if (Translations.Regions.TryGetValue(inner, out iv) || Translations.Dict.TryGetValue(inner, out iv))
                    return pm.Groups[1].Value + "(" + iv + ")" + pm.Groups[3].Value;
            }
            return null;
        }
    }
}
