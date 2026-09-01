#if MELON
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppTMPro;
using XUnity.AutoTranslator.Plugin.Core;

namespace KoreanTextFixer
{
    // MelonLoader의 Il2CppInterop은 비유니티 네임스페이스에 Il2Cpp 접두사를 붙인다(TMPro -> Il2CppTMPro).
    // XUnity.AutoTranslator는 "TMPro.TMP_Text" 라는 이름으로 타입을 찾기 때문에 MelonLoader에서는
    // TextMeshPro 후킹을 통째로 건너뛴다. 그래서 TMP 텍스트가 폴링으로만 번역되어
    // 표시 후 1초쯤 뒤에 바뀌거나 깜박였다.
    // 여기서 직접 set_text / SetText 를 후킹해 값이 쓰이기 직전에 번역해 넘긴다.
    internal static class TmpHook
    {
        internal static bool Installed;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var prefix = new HarmonyMethod(typeof(TmpHook).GetMethod(
                nameof(TextPrefix), BindingFlags.NonPublic | BindingFlags.Static));

            int patched = 0;
            foreach (MethodBase target in Targets())
            {
                try
                {
                    harmony.Patch(target, prefix);
                    patched++;
                    KLog.Info("hooked " + target.DeclaringType.Name + "." + target.Name);
                }
                catch (Exception e)
                {
                    KLog.Warn("hook failed: " + target.Name + " - " + e.Message);
                }
            }
            Installed = patched > 0;
            if (Installed) { InstallOnEnable(harmony); InstallCharArray(harmony); }
            else KLog.Warn("TMP 후킹 실패 - 폴링으로만 동작합니다");
        }

        // text 프로퍼티 setter + 문자열 하나만 받는 SetText 오버로드
        private static IEnumerable<MethodBase> Targets()
        {
            var list = new List<MethodBase>();
            var setter = AccessTools.PropertySetter(typeof(TMP_Text), "text");
            if (setter != null) list.Add(setter);
            foreach (var m in typeof(TMP_Text).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                // 첫 인자가 문자열인 SetText 오버로드 전부 (다인자 포함)
                if (m.Name != "SetText") continue;
                var ps = m.GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType == typeof(string)) list.Add(m);
            }
            return list;
        }

        // 프리팹에 박혀 있는 텍스트는 setter를 타지 않아 후킹에 안 걸린다.
        // 지금까지는 폴링(FindObjectsOfType)으로 주웠는데 씬이 크면 한 번에 100ms 가까이 걸려
        // 30초마다 화면이 튀었다. 텍스트가 켜지는 순간을 잡으면 폴링이 필요 없다.
        private static void InstallOnEnable(HarmonyLib.Harmony harmony)
        {
            // OnEnable은 가상 메서드라 파생 클래스(TextMeshProUGUI 등)가 오버라이드하면
            // 기반 클래스 패치로는 잡히지 않는다. 선언된 타입마다 각각 건다.
            var post = new HarmonyMethod(typeof(TmpHook).GetMethod(
                nameof(OnEnablePostfix), BindingFlags.NonPublic | BindingFlags.Static));
            foreach (var t in new[] { typeof(TMP_Text), typeof(TextMeshProUGUI), typeof(TextMeshPro) })
            {
                var m = t.GetMethod("OnEnable",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (m == null) continue;
                try
                {
                    harmony.Patch(m, null, post);
                    KLog.Info("hooked " + t.Name + ".OnEnable");
                }
                catch (Exception e)
                {
                    KLog.Warn(t.Name + ".OnEnable 후킹 실패: " + e.Message);
                }
            }
        }

        internal static void InstallCharArray(HarmonyLib.Harmony harmony)
        {
            var post = new HarmonyMethod(typeof(TmpHook).GetMethod(
                nameof(CharArrayPostfix), BindingFlags.NonPublic | BindingFlags.Static));
            int n = 0;
            foreach (var m in typeof(TMP_Text).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "SetCharArray") continue;
                try { harmony.Patch(m, null, post); n++; } catch { }
            }
            if (n > 0) KLog.Info("hooked TMP_Text.SetCharArray x" + n);
        }

        private static void CharArrayPostfix(TMP_Text __instance)
        {
            try
            {
                if (__instance == null) return;
                string cur = __instance.text;
                if (string.IsNullOrEmpty(cur)) return;
                string t = Fixer.TranslateForHook(cur);
                if (t != null) __instance.text = t;
            }
            catch { }
        }

        private static void OnEnablePostfix(TMP_Text __instance)
        {
            try
            {
                if (__instance == null) return;
                string cur = __instance.text;
                if (string.IsNullOrEmpty(cur)) return;
                string t = Fixer.TranslateForHook(cur);
                if (t != null) __instance.text = t;
            }
            catch { }
        }

        // 게임이 텍스트를 쓸 때마다(=매 프레임 여러 번) 불린다. 여기서 무거운 일을 하면 그대로 프레임을 깎는다.
        private static void TextPrefix(ref string __0)
        {
            string src = __0;
            if (string.IsNullOrEmpty(src)) return;
            try
            {
                string t = Fixer.TranslateForHook(src);
                if (t != null) __0 = t;
            }
            catch { }
        }
    }

    // XUnity.AutoTranslator의 번역 엔진을 그대로 빌려 쓴다.
    // 정규식·조립 번역(가격·이름이 끼어드는 문장)은 번역기 쪽 캐시에만 있어서 사전만으로는 못 한다.
    internal static class XUnityBridge
    {
        private const int MaxAttempts = 40; // 이만큼 시도해도 안 되면 사전만 쓴다

        private static bool _connected;
        private static bool _gaveUp;
        private static int _stride;
        private static int _attempts;

        internal static bool Connected { get { return _connected; } }

        internal static string Translate(string src)
        {
            if (!_connected)
            {
                if (_gaveUp) return null;
                // 번역기는 Mods 알파벳 순서상 우리보다 늦게 로드된다. 붙을 때까지 띄엄띄엄 확인한다.
                if (_stride++ % 600 != 0) return null;
                if (++_attempts > MaxAttempts)
                {
                    _gaveUp = true;
                    KLog.Warn("XUnity 번역 엔진에 연결하지 못했습니다 - 사전만 사용합니다");
                    return null;
                }
                try
                {
                    // 번역기 DLL이 아예 없으면 여기서 예외가 난다(그 경우 사전만 쓴다).
                    // 리플렉션으로 찾으려 했더니 MelonLoader가 UserLibs를 별도 로드 컨텍스트에
                    // 올려서 AppDomain에서도 Type.GetType에서도 보이지 않았다. 직접 참조가 확실하다.
                    if (!XUnityDirect.IsReady()) return null;
                }
                catch (Exception e)
                {
                    _gaveUp = true;
                    KLog.Warn("XUnity 번역기를 쓸 수 없습니다(사전만 사용): " + e.Message);
                    return null;
                }
                _connected = true;
                KLog.Info("XUnity 번역 엔진 연결됨");
                // 연결 전에 "번역 없음"으로 기억해 둔 것들을 다시 판단하게 한다
                Fixer.ClearHookCache();
            }

            try
            {
                return XUnityDirect.Translate(src);
            }
            catch
            {
                return null;
            }
        }
    }

    // 번역기 타입을 직접 만지는 곳을 한 군데로 모아 둔다.
    // 번역기가 없으면 이 클래스를 처음 쓰는 순간 예외가 나므로, 호출부에서 감싸 잡는다.
    internal static class XUnityDirect
    {
        internal static bool IsReady()
        {
            return AutoTranslator.Default != null;
        }

        internal static string Translate(string src)
        {
            string dst;
            if (AutoTranslator.Default.TryTranslate(src, out dst)) return dst;
            return null;
        }
    }
}
#endif
