#if MELON
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;

namespace KoreanTextFixer
{
    // 화면에 떠 있는 영어를 그 자리에서 통째로 뽑아내는 진단 도구.
    //
    // 후킹은 게임이 set_text 로 값을 쓸 때만 볼 수 있다. 프리팹에 박혀 있거나
    // 다른 경로로 그려지는 글자는 후킹에 안 걸리고, 그래서 미번역 수집에도 안 잡힌다.
    // "수집은 비어 있는데 화면엔 영어가 많다"는 상황을 가르려면 화면을 직접 훑어야 한다.
    internal static class Diagnostics
    {
        internal static KeyCode DumpKey = KeyCode.F9;
        internal static KeyCode ToggleKey = KeyCode.F10;
        internal static bool Disabled;          // F10: 플러그인 처리 전체 정지 (프레임 원인 가리기용)

        private static string _dumpPath;
        private static int _dumpCount;

        internal static void Init(string dumpPath)
        {
            _dumpPath = dumpPath;
            KLog.Info("진단: F9 = 화면의 영어 덤프, F10 = 번역 처리 켜기/끄기");
        }

        internal static void Tick()
        {
            try
            {
                if (Input.GetKeyDown(ToggleKey))
                {
                    Disabled = !Disabled;
                    KLog.Info(Disabled ? "번역 처리 정지 (F10으로 복구)" : "번역 처리 재개");
                }
                if (Input.GetKeyDown(DumpKey)) Dump();
            }
            catch { }
        }

        private static void Dump()
        {
            var rows = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            var tmps = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<TMP_Text>());
            if (tmps != null)
            {
                foreach (var o in tmps)
                {
                    var t = o.TryCast<TMP_Text>();
                    if (t == null) continue;
                    Collect(rows, seen, "TMP", Path(t.transform), Safe(t.text), t.isActiveAndEnabled);
                }
            }
            var uis = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<UnityEngine.UI.Text>());
            if (uis != null)
            {
                foreach (var o in uis)
                {
                    var t = o.TryCast<UnityEngine.UI.Text>();
                    if (t == null) continue;
                    Collect(rows, seen, "UI", Path(t.transform), Safe(t.text), t.isActiveAndEnabled);
                }
            }

            try
            {
                _dumpCount++;
                var sb = new StringBuilder();
                sb.AppendLine("// ==== 덤프 " + _dumpCount + " : 화면에 떠 있는 영어 " + rows.Count + "개 ====");
                foreach (string r in rows) sb.AppendLine(r);
                File.AppendAllText(_dumpPath, sb.ToString(), Encoding.UTF8);
                KLog.Info("화면 덤프 " + rows.Count + "개 -> " + _dumpPath);
            }
            catch (Exception e)
            {
                KLog.Warn("덤프를 쓰지 못했습니다: " + e.Message);
            }
        }

        private static void Collect(List<string> rows, HashSet<string> seen, string kind, string path, string text, bool active)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!Fixer.LooksEnglish(text)) return;
            string row = (active ? "" : "[숨김] ") + kind + "\t" + path + "\t" + text.Replace("\r", "").Replace("\n", "\\n");
            if (seen.Add(row)) rows.Add(row);
        }

        private static string Safe(string s)
        {
            try { return s; } catch { return null; }
        }

        // 계층 경로를 남겨야 어떤 UI인지 찾아갈 수 있다
        private static string Path(Transform t)
        {
            try
            {
                var sb = new StringBuilder(t.name);
                var p = t.parent;
                int depth = 0;
                while (p != null && depth++ < 6)
                {
                    sb.Insert(0, p.name + "/");
                    p = p.parent;
                }
                return sb.ToString();
            }
            catch { return "?"; }
        }
    }
}
#endif
