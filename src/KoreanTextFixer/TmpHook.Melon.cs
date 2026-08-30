#if MELON
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppTMPro;

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
            if (!Installed) KLog.Warn("TMP 후킹 실패 - 폴링으로만 동작합니다");
        }

        // text 프로퍼티 setter + 문자열 하나만 받는 SetText 오버로드
        private static IEnumerable<MethodBase> Targets()
        {
            var list = new List<MethodBase>();
            var setter = AccessTools.PropertySetter(typeof(TMP_Text), "text");
            if (setter != null) list.Add(setter);
            foreach (var m in typeof(TMP_Text).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "SetText") continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(string)) list.Add(m);
            }
            return list;
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

    // XUnity.AutoTranslator의 번역 엔진을 그대로 빌려 쓴다(정규식·조립 번역은 번역기 쪽 캐시에만 있다).
    // 하드 참조를 두면 번역기가 없을 때 플러그인이 통째로 죽으므로 리플렉션으로 붙는다.
    internal static class XUnityBridge
    {
        private delegate bool TryTranslateDelegate(string untranslatedText, out string translatedText);

        private const string TypeName =
            "XUnity.AutoTranslator.Plugin.Core.AutoTranslator, XUnity.AutoTranslator.Plugin.Core";
        private const int MaxAttempts = 40; // 이만큼 시도해도 못 붙으면 사전만 쓴다

        private static TryTranslateDelegate _tryTranslate;
        private static int _attempts;
        private static int _stride;
        private static bool _gaveUp;

        internal static bool Connected { get { return _tryTranslate != null; } }

        // 번역기는 Mods 알파벳 순서상 우리보다 늦게 로드될 수 있어 붙을 때까지 다시 시도한다.
        // 다만 실패한 채로 계속 시도하면 그 자체가 프레임을 깎으므로 횟수를 제한한다.
        private static void Resolve()
        {
            if (_tryTranslate != null || _gaveUp) return;
            if (_stride++ % 600 != 0) return;
            if (++_attempts > MaxAttempts)
            {
                _gaveUp = true;
                KLog.Warn("XUnity 번역 엔진에 연결하지 못했습니다 - 사전만 사용합니다");
                return;
            }
            try
            {
                // MelonLoader는 UserLibs를 별도 로드 컨텍스트에 올린다.
                // AppDomain.GetAssemblies() 로는 안 보일 수 있어 어셈블리 한정 이름을 먼저 쓴다.
                Type t = Type.GetType(TypeName, false);
                if (t == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name != "XUnity.AutoTranslator.Plugin.Core") continue;
                        t = asm.GetType("XUnity.AutoTranslator.Plugin.Core.AutoTranslator");
                        break;
                    }
                }
                if (t == null) return;
                var def = t.GetProperty("Default", BindingFlags.Public | BindingFlags.Static);
                if (def == null) return;
                object translator = def.GetValue(null);
                if (translator == null) return; // 번역기가 아직 초기화되지 않음

                var m = translator.GetType().GetMethod("TryTranslate",
                    new[] { typeof(string), typeof(string).MakeByRefType() });
                if (m == null) return;

                // 매 호출마다 Invoke하면 인자 배열이 새로 생기므로 델리게이트로 묶어 둔다
                _tryTranslate = (TryTranslateDelegate)Delegate.CreateDelegate(
                    typeof(TryTranslateDelegate), translator, m);
                KLog.Info("XUnity 번역 엔진 연결됨");
                // 연결 전에 "번역 없음"으로 기억해 둔 것들을 다시 판단하게 한다
                Fixer.ClearHookCache();
            }
            catch (Exception e)
            {
                _gaveUp = true;
                KLog.Warn("XUnity 연결 실패(사전만 사용): " + e.Message);
            }
        }

        internal static string Translate(string src)
        {
            Resolve();
            var f = _tryTranslate;
            if (f == null) return null;
            try
            {
                string dst;
                if (f(src, out dst)) return dst;
            }
            catch { }
            return null;
        }
    }
}
#endif
