param(
    [switch]$NoPause,
    [switch]$DryRun,          # 실제로 바꾸지 않고 무엇이 바뀌는지만 보여준다
    [switch]$Restore,         # 백업해 둔 원본으로 되돌린다
    [switch]$Report,          # 어떤 스크립트(클래스)에 문구가 들어 있는지만 집계한다
    [int]$MinLength = 12,     # 이보다 짧은 문구는 건드리지 않는다 (식별자 오인 방지)
    [string]$GamePath = ""
)
# 게임 파일에 원문이 그대로 들어 있는 문구를 한글로 직접 교체한다(베이킹).
# 런타임 번역기가 볼 일이 없어져 그만큼 프레임 부담과 번역 지연이 사라진다.
# 가격·이름이 끼어드는 조립 문장은 파일에 완성된 형태가 없어 교체 대상이 아니다 - 그건 플러그인이 처리한다.
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
try { Get-ChildItem $here -Recurse -File | Unblock-File -ErrorAction SilentlyContinue } catch {}

function Get-SteamLibraries {
    $roots = New-Object System.Collections.Generic.List[string]
    foreach ($rk in @("HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam")) {
        try {
            $v = Get-ItemProperty -Path $rk -ErrorAction Stop
            foreach ($name in @("SteamPath", "InstallPath")) {
                $sp = $v.$name
                if ($sp) { $roots.Add(($sp -replace '/', '\')) }
            }
        } catch {}
    }
    $roots.Add("C:\Program Files (x86)\Steam")
    foreach ($d in (Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Root -match '^[A-Z]:\\$' })) {
        foreach ($sub in @("Steam", "SteamLibrary", "Program Files (x86)\Steam", "Program Files\Steam", "Games\Steam")) {
            $roots.Add((Join-Path $d.Root $sub))
        }
    }
    $libs = New-Object System.Collections.Generic.List[string]
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        $libs.Add($r)
        $vdf = Join-Path $r "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libs.Add($m.Groups[1].Value.Replace('\\', '\'))
            }
        }
    }
    return @($libs | Select-Object -Unique)
}

if ($GamePath) { $GamePath = $GamePath.Trim('"').Trim() }
if (-not $GamePath -or -not (Test-Path (Join-Path $GamePath "Schedule I.exe"))) {
    $GamePath = $null
    foreach ($lib in (Get-SteamLibraries)) {
        $c = Join-Path $lib "steamapps\common\Schedule I"
        if (Test-Path (Join-Path $c "Schedule I.exe")) { $GamePath = $c; break }
    }
}
if (-not $GamePath) {
    Write-Host "게임 폴더를 찾지 못했습니다." -ForegroundColor Red
    if (-not $NoPause) { pause }
    exit 1
}
# 읽기만 하는 모드(-DryRun/-Report)는 게임이 켜져 있어도 괜찮다
if (-not $DryRun -and -not $Report -and (Get-Process -Name "Schedule I" -ErrorAction SilentlyContinue)) {
    Write-Host "게임을 먼저 종료하세요." -ForegroundColor Red
    if (-not $NoPause) { pause }
    exit 1
}
Write-Host ("게임 경로: " + $GamePath) -ForegroundColor Green

# ---------- 되돌리기 ----------
if ($Restore) {
    $bakDir = Join-Path $GamePath "KoreanPatch_backup"
    if (-not (Test-Path $bakDir)) {
        Write-Host "백업 폴더가 없습니다. Steam > 속성 > 설치된 파일 > 파일 무결성 확인 을 쓰세요." -ForegroundColor Yellow
        if (-not $NoPause) { pause }
        exit 1
    }
    $dataDir = Join-Path $GamePath "Schedule I_Data"
    $restored = 0
    foreach ($b in (Get-ChildItem $bakDir -Filter "*.original")) {
        $dst = Join-Path $dataDir ($b.Name -replace '\.original$', '')
        if (Test-Path $dst) {
            Copy-Item $b.FullName $dst -Force
            Write-Host ("  복원: " + (Split-Path $dst -Leaf))
            $restored++
        }
    }
    Write-Host ("원본 " + $restored + "개 파일을 복원했습니다.") -ForegroundColor Green
    if (-not $NoPause) { pause }
    exit 0
}

# ---------- 문자열 스캔·치환기 (PowerShell 루프로는 1GB를 훑을 수 없어 C#으로 넣는다) ----------
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class KrBake
{
    public static Dictionary<string, string> Dict = new Dictionary<string, string>(StringComparer.Ordinal);
    public static List<string> Samples = new List<string>();
    public static int MinLen = 12;

    public static int LoadDict(string[] files, int minLen)
    {
        MinLen = minLen;
        Dict.Clear();
        foreach (string f in files)
        {
            if (!File.Exists(f)) continue;
            foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
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
                if (!Eligible(k)) continue;
                if (!Dict.ContainsKey(k)) Dict[k] = v;
            }
        }
        return Dict.Count;
    }

    private static string Unescape(string s)
    {
        return s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\=", "=").Replace("\\\\", "\\");
    }

    // 오브젝트 이름·키로 쓰일 법한 짧은 토큰은 후보에서 뺀다.
    // 코드가 문자열로 찾아 쓰는 값을 바꾸면 게임이 조용히 깨진다.
    private static bool Eligible(string k)
    {
        if (k.Length < MinLen) return false;
        if (k.IndexOf(' ') < 0 && k.Length < 20) return false;
        if (k.IndexOf('/') >= 0 || k.IndexOf('\\') >= 0) return false;      // 경로·에셋 참조
        if (k.IndexOf('_') >= 0 && k.IndexOf(' ') < 0) return false;       // SOME_KEY 형태의 상수
        string low = k.ToLowerInvariant();
        string[] exts = { ".png", ".jpg", ".mat", ".asset", ".prefab", ".shader", ".wav", ".mp3", ".dll", ".json" };
        foreach (string e in exts) { if (low.EndsWith(e)) return false; }
        bool hasLetter = false;
        for (int i = 0; i < k.Length; i++)
        {
            char c = k[i];
            if (c < 0x20 || c > 0x7e) return false;   // 순수 ASCII만
            if (char.IsLetter(c)) hasLetter = true;
        }
        return hasLetter;
    }

    // Unity 직렬화 문자열은 [int32 길이][UTF-8 바이트][4바이트 정렬 패딩] 형태다.
    // 오브젝트 원본 바이트를 훑어 사전에 있는 문자열만 한글로 바꾼 새 바이트를 만든다.
    public static byte[] Patch(byte[] raw, out int count)
    {
        count = 0;
        List<int> at = new List<int>();
        List<int> lens = new List<int>();
        List<byte[]> reps = new List<byte[]>();

        int i = 0;
        while (i + 8 <= raw.Length)
        {
            int len = BitConverter.ToInt32(raw, i);
            if (len >= MinLen && len <= 4000 && i + 4 + len <= raw.Length)
            {
                bool printable = true;
                for (int j = i + 4; j < i + 4 + len; j++)
                {
                    byte b = raw[j];
                    if (b < 0x20 || b > 0x7e)
                    {
                        if (b != 0x0a && b != 0x0d && b != 0x09) { printable = false; break; }
                    }
                }
                if (printable)
                {
                    string s = Encoding.ASCII.GetString(raw, i + 4, len);
                    string ko;
                    // MonoBehaviour의 m_Name은 항상 오프셋 28이다(GameObject PPtr 12 + m_Enabled 4 + Script PPtr 12).
                    // 에셋 이름은 코드가 이름으로 찾아 쓸 수 있어 건드리지 않는다.
                    if (i != 28 && Dict.TryGetValue(s, out ko) && ko != s)
                    {
                        at.Add(i); lens.Add(len); reps.Add(Encoding.UTF8.GetBytes(ko));
                        if (Samples.Count < 30) Samples.Add(s + "  ->  " + ko);
                        i = (i + 4 + len + 3) & ~3;
                        continue;
                    }
                }
            }
            i += 4;
        }
        if (at.Count == 0) return null;
        count = at.Count;

        MemoryStream ms = new MemoryStream(raw.Length + 8192);
        int cur = 0;
        for (int k = 0; k < at.Count; k++)
        {
            ms.Write(raw, cur, at[k] - cur);
            byte[] ko = reps[k];
            ms.Write(BitConverter.GetBytes(ko.Length), 0, 4);
            ms.Write(ko, 0, ko.Length);
            int pad = (4 - (ko.Length & 3)) & 3;   // 길이+데이터+패딩이 항상 4의 배수가 되어 뒤쪽 정렬이 유지된다
            for (int p = 0; p < pad; p++) ms.WriteByte(0);
            cur = (at[k] + 4 + lens[k] + 3) & ~3;
        }
        ms.Write(raw, cur, raw.Length - cur);
        return ms.ToArray();
    }
}
'@

$dictFiles = @(
    (Join-Path $here "payload\Text\Korean_Base.txt"),
    (Join-Path $here "payload\Text\Korean_Extracted.txt")
)
$n = [KrBake]::LoadDict($dictFiles, $MinLength)
Write-Host ("베이킹 대상 문구: " + $n + "개 (" + $MinLength + "자 이상)") -ForegroundColor Cyan
if ($n -eq 0) { Write-Host "번역 파일을 찾지 못했습니다." -ForegroundColor Red; if (-not $NoPause) { pause }; exit 1 }

# ---------- AssetsTools.NET ----------
$atDll = Join-Path $here "payload\tools\AssetsTools.NET.dll"
try { Add-Type -Path $atDll } catch { [void][System.Reflection.Assembly]::UnsafeLoadFrom($atDll) }
$tpk = Join-Path $here "payload\tools\classdata.tpk"

$dataDir = Join-Path $GamePath "Schedule I_Data"
$targets = @()
# globalgamemanagers.assets 는 URP 렌더러 설정 같은 엔진 자산이 들어 있어 손대지 않는다
foreach ($f in (Get-ChildItem $dataDir -File)) {
    if ($f.Name -eq "globalgamemanagers.assets") { continue }
    if ($f.Name -like "*.assets" -or $f.Name -match '^level\d+$') { $targets += $f.FullName }
}
Write-Host ("검사할 파일: " + $targets.Count + "개") -ForegroundColor Cyan

# IL2CPP 빌드는 MonoScript(115)가 전부 globalgamemanagers.assets 에 모여 있다.
# pathID -> 클래스 이름 지도를 한 번 만들어 두고, 다른 파일의 MonoBehaviour가 어떤
# 스크립트의 데이터인지 판별하는 데 쓴다.
$scriptMap = @{}
$ggmPath = Join-Path $dataDir "globalgamemanagers.assets"
if (Test-Path $ggmPath) {
    $amg = New-Object AssetsTools.NET.Extra.AssetsManager
    $amg.LoadClassPackage($tpk) | Out-Null
    $ig = $amg.LoadAssetsFile($ggmPath, $false)
    $amg.LoadClassDatabaseFromPackage($ig.file.Metadata.UnityVersion) | Out-Null
    foreach ($si in $ig.file.GetAssetsOfType(115)) {
        try { $scriptMap[[long]$si.PathId] = $amg.GetBaseField($ig, $si)["m_ClassName"].AsString } catch {}
    }
    $amg.UnloadAll()
}
Write-Host ("스크립트 이름 " + $scriptMap.Count + "개 확보") -ForegroundColor DarkGray

$bakDir = Join-Path $GamePath "KoreanPatch_backup"
$totalStrings = 0
$totalFiles = 0
$classHits = @{}

# 화면에 글자를 그리는 컴포넌트만 손댄다.
# 게임 로직이 이름·ID로 대조하는 데이터(사업장·아이템·퀘스트 정의 등)를 바꾸면
# 세이브 로딩이 '초기화중'에서 멈추는 것을 확인했다. 그래서 흰 목록 방식으로 좁힌다.
$AllowClass = New-Object System.Collections.Generic.HashSet[string]
foreach ($c in @("TextMeshPro", "TextMeshProUGUI", "TMP_Text", "Text")) { [void]$AllowClass.Add($c) }

foreach ($path in $targets) {
    $name = Split-Path $path -Leaf
    $am = New-Object AssetsTools.NET.Extra.AssetsManager
    $am.LoadClassPackage($tpk) | Out-Null
    $inst = $null
    try { $inst = $am.LoadAssetsFile($path, $false) } catch { $am.UnloadAll(); continue }
    $dataOff = $inst.file.Header.DataOffset
    $bytes = [System.IO.File]::ReadAllBytes($path)

    # 이 파일이 globalgamemanagers.assets 를 몇 번 외부 참조로 두는지 (m_Script의 fileID와 대조용)
    $ggmFileId = -1
    for ($e = 0; $e -lt $inst.file.Metadata.Externals.Count; $e++) {
        if ($inst.file.Metadata.Externals[$e].PathName -eq "globalgamemanagers.assets") { $ggmFileId = $e + 1; break }
    }

    $hits = 0
    $objs = 0
    foreach ($info in $inst.file.AssetInfos) {
        if ($info.TypeId -ne 114) { continue }   # MonoBehaviour만. GameObject 이름(1)은 코드가 찾아 쓰므로 손대지 않는다
        $size = $info.ByteSize
        if ($size -le 32) { continue }
        $raw = New-Object byte[] $size
        [Array]::Copy($bytes, $dataOff + $info.ByteOffset, $raw, 0, $size)

        # m_Script는 항상 오프셋 16(fileID) + 20(pathID).
        # IL2CPP 빌드는 MonoScript가 전부 globalgamemanagers.assets 에 있고 외부 참조로 걸린다.
        $cls = "?"
        if ([BitConverter]::ToInt32($raw, 16) -eq $ggmFileId) {
            $spid = [BitConverter]::ToInt64($raw, 20)
            if ($scriptMap.ContainsKey($spid)) { $cls = $scriptMap[$spid] }
        }
        if (-not $Report -and -not $AllowClass.Contains($cls)) { continue }

        $c = 0
        $new = [KrBake]::Patch($raw, [ref]$c)
        if ($new -ne $null) {
            if ($Report) {
                if (-not $classHits.ContainsKey($cls)) { $classHits[$cls] = 0 }
                $classHits[$cls] += $c
            }
            elseif (-not $DryRun) { $info.SetNewData($new) }
            $hits += $c
            $objs++
        }
    }

    if ($hits -gt 0) {
        Write-Host ("  " + $name + " : " + $hits + "개 문구 / " + $objs + "개 오브젝트") -ForegroundColor Green
        $totalStrings += $hits
        $totalFiles++
        if (-not $DryRun -and -not $Report) {
            New-Item -ItemType Directory -Force $bakDir | Out-Null
            $bak = Join-Path $bakDir ($name + ".original")
            if (-not (Test-Path $bak)) { Copy-Item $path $bak }
            $tmp = $path + ".new"
            $writer = New-Object AssetsTools.NET.AssetsFileWriter($tmp)
            $inst.file.Write($writer, -1)
            $writer.Close()
            $am.UnloadAll()
            # 다시 열어보고 멀쩡할 때만 교체한다
            $ok = $false
            try {
                $am2 = New-Object AssetsTools.NET.Extra.AssetsManager
                $am2.LoadClassPackage($tpk) | Out-Null
                $i2 = $am2.LoadAssetsFile($tmp, $false)
                $ok = ($i2.file.AssetInfos.Count -eq $inst.file.AssetInfos.Count)
                $am2.UnloadAll()
            } catch { $ok = $false }
            if ($ok) { Move-Item $tmp $path -Force }
            else { [System.IO.File]::Delete($tmp); Write-Host ("  " + $name + " 검증 실패 - 원본 유지") -ForegroundColor Red }
            continue
        }
    }
    $am.UnloadAll()
}

Write-Host ""
if ($Report) {
    Write-Host "스크립트(클래스)별 문구 수 - 어떤 데이터에 텍스트가 들어 있는지 확인용" -ForegroundColor Cyan
    foreach ($e in ($classHits.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 40)) {
        $mark = ""
        if ($AllowClass.Contains($e.Key)) { $mark = "  <- 베이킹 대상" }
        Write-Host ("  {0,6}  {1}{2}" -f $e.Value, $e.Key, $mark)
    }
    Write-Host ""
    Write-Host ("합계 " + $totalStrings + "개 문구") -ForegroundColor Yellow
    if (-not $NoPause) { pause }
    exit 0
}
if ([KrBake]::Samples.Count -gt 0) {
    Write-Host "교체 예시:" -ForegroundColor Cyan
    foreach ($s in [KrBake]::Samples) { Write-Host ("  " + $s) }
    Write-Host ""
}
if ($DryRun) {
    Write-Host ("[미리보기] 파일 " + $totalFiles + "개에서 문구 " + $totalStrings + "개를 바꿀 수 있습니다.") -ForegroundColor Yellow
    Write-Host "실제로 적용하려면 -DryRun 없이 다시 실행하세요."
} else {
    Write-Host ("완료: 파일 " + $totalFiles + "개, 문구 " + $totalStrings + "개를 한글로 교체했습니다.") -ForegroundColor Green
    Write-Host ("원본 백업: " + $bakDir) -ForegroundColor DarkGray
    Write-Host "게임을 업데이트하면 원본으로 돌아갑니다. 그때 다시 실행하세요." -ForegroundColor DarkGray
}
if (-not $NoPause) { pause }
