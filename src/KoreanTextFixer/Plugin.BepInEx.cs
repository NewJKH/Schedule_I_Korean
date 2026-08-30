#if !MELON
using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KoreanTextFixer
{
    [BepInPlugin("kr.schedule1.textfixer", "Korean Text Fixer", "1.4.0")]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            KLog.Info = s => Log.LogInfo(s);
            KLog.Warn = s => Log.LogWarning(s);
            KLog.Error = s => Log.LogError(s);
            try
            {
                Translations.Load(Path.Combine(Paths.GameRootPath, "BepInEx", "Translation", "ko", "Text"));
                ClassInjector.RegisterTypeInIl2Cpp<FixerBehaviour>();
                AddComponent<FixerBehaviour>();
                KLog.Info("KoreanTextFixer 1.4.0 (BepInEx) loaded. entries=" + Translations.Dict.Count);
            }
            catch (Exception e)
            {
                KLog.Error("KoreanTextFixer init failed: " + e);
            }
        }
    }

    public class FixerBehaviour : MonoBehaviour
    {
        public FixerBehaviour(IntPtr ptr) : base(ptr) { }

        private readonly Fixer _fixer = new Fixer();

        public void Update()
        {
            _fixer.Tick();
        }
    }
}
#endif
