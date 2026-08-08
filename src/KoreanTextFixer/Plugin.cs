using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using TMPro;

namespace KoreanTextFixer
{
    [BepInPlugin("kr.schedule1.textfixer", "Korean Text Fixer", "1.2.0")]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource Logger;
        internal static Dictionary<string, string> Dict = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static Dictionary<string, string> StrippedDict = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static readonly Regex TagRx = new Regex("<[^<>]{1,60}>", RegexOptions.Compiled);
        internal static Dictionary<string, string> Regions = new Dictionary<string, string>
        {
            { "Northtown", "노스타운" }, { "Westville", "웨스트빌" }, { "Downtown", "다운타운" },
            { "Docks", "부두" }, { "Suburbia", "교외" }, { "Uptown", "업타운" }
        };

        public override void Load()
        {
            Logger = base.Log;
            try
            {
                LoadDictionaries();
                ClassInjector.RegisterTypeInIl2Cpp<FixerBehaviour>();
                AddComponent<FixerBehaviour>();
                Logger.LogInfo("KoreanTextFixer 1.2.0 loaded. entries=" + Dict.Count);
            }
            catch (Exception e)
            {
                Logger.LogError("KoreanTextFixer init failed: " + e);
            }
        }

        private void LoadDictionaries()
        {
            string baseDir = Path.Combine(Paths.GameRootPath, "BepInEx", "Translation", "ko", "Text");
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

    public class FixerBehaviour : MonoBehaviour
    {
        public FixerBehaviour(IntPtr ptr) : base(ptr) { }

        private float _next;
        private float _nextStat;
        private int _errors;
        private bool _dead;
        private int _replaced;
        private readonly Dictionary<int, string> _seen = new Dictionary<int, string>();
        // 리치텍스트 태그 또는 텍스트 조각 단위 분해
        private static readonly Regex Token = new Regex("<[^<>]{1,60}>|[^<]+", RegexOptions.Compiled);
        private static readonly Regex Paren = new Regex("^(\\s*)\\((.+)\\)(\\s*)$", RegexOptions.Compiled);
        private static readonly Regex Core = new Regex("^(\\s*)(.*?)(\\s*)$", RegexOptions.Compiled | RegexOptions.Singleline);

        public void Update()
        {
            if (_dead) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1.0f;
            try
            {
                Sweep();
            }
            catch (Exception e)
            {
                _errors++;
                Plugin.Logger.LogWarning("sweep error(" + _errors + "): " + e.Message);
                if (_errors >= 5) { _dead = true; Plugin.Logger.LogError("too many errors - fixer disabled"); }
            }
            if (Time.unscaledTime >= _nextStat)
            {
                _nextStat = Time.unscaledTime + 30f;
                Plugin.Logger.LogInfo("stats: replaced=" + _replaced + " cache=" + _seen.Count);
            }
        }

        private void Sweep()
        {
            int budget = 400; // 스윕당 처리 상한 (프레임 부담/위험 최소화)

            var tmps = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<TMP_Text>());
            if (tmps != null)
            {
                foreach (var o in tmps)
                {
                    if (budget <= 0) return;
                    var tmp = o.TryCast<TMP_Text>();
                    if (tmp == null) continue;
                    budget--;
                    try
                    {
                        if (!tmp.isActiveAndEnabled) continue;
                        string cur = tmp.text;
                        string outp = Check(cur, tmp.GetInstanceID());
                        if (outp != null) { tmp.text = outp; _replaced++; }
                    }
                    catch { }
                }
            }

            var uis = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<UnityEngine.UI.Text>());
            if (uis != null)
            {
                foreach (var o in uis)
                {
                    if (budget <= 0) return;
                    var ut = o.TryCast<UnityEngine.UI.Text>();
                    if (ut == null) continue;
                    budget--;
                    try
                    {
                        if (!ut.isActiveAndEnabled) continue;
                        string cur = ut.text;
                        string outp = Check(cur, ut.GetInstanceID());
                        if (outp != null) { ut.text = outp; _replaced++; }
                    }
                    catch { }
                }
            }
        }

        // 교체가 필요하면 새 문자열, 아니면 null
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
            // 이미 한글 포함 시 무시
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c >= 0xAC00 && c <= 0xD7A3) return null;
            }
            string t = src.Trim();
            if (t.Length == 0) return null;

            // 1) 통짜 매칭
            string v;
            if (Plugin.Dict.TryGetValue(t, out v)) return src.Replace(t, v);

            // 1-b) 태그 무시 매칭: 게임이 <h1> 등을 <color>로 바꿔 표시하는 경우 대응
            if (src.IndexOf('<') >= 0)
            {
                string stripped = Plugin.TagRx.Replace(src, "").Trim();
                string sv;
                if (stripped.Length > 10 && Plugin.StrippedDict.TryGetValue(stripped, out sv))
                {
                    // 사전 값의 <h1>..</h> 강조를 화면의 색상 태그로 치환
                    var cm = Regex.Match(src, "<color[^<>]*>");
                    if (cm.Success)
                    {
                        sv = sv.Replace("<h1>", cm.Value).Replace("</h>", "</color>");
                    }
                    else
                    {
                        sv = Plugin.TagRx.Replace(sv, "");
                    }
                    return sv;
                }
            }

            // 2) 리치텍스트 태그를 보존하며 조각별 번역
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

            // 3) 태그 없는 평문 조각
            string plain = TranslatePlain(src);
            return plain;
        }

        // 평문 조각 번역 (앞뒤 공백 보존). 실패 시 null
        private static string TranslatePlain(string tok)
        {
            var cm = Core.Match(tok);
            string lead = cm.Groups[1].Value, core = cm.Groups[2].Value, tail = cm.Groups[3].Value;
            if (core.Length == 0) return null;

            string v;
            if (Plugin.Dict.TryGetValue(core, out v)) return lead + v + tail;

            var pm = Paren.Match(tok);
            if (pm.Success)
            {
                string inner = pm.Groups[2].Value;
                string iv;
                if (Plugin.Regions.TryGetValue(inner, out iv) || Plugin.Dict.TryGetValue(inner, out iv))
                    return pm.Groups[1].Value + "(" + iv + ")" + pm.Groups[3].Value;
            }
            return null;
        }
    }
}
