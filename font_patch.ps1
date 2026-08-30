param([switch]$NoPause, [string]$GamePath = "")
# Schedule I 폰트 교체 (fs-sevegment, LegacyRuntime 제외 전체를 을지로체로)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

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

$game = $null
if ($GamePath) {
    $GamePath = $GamePath.Trim('"').Trim()
    $c = Join-Path $GamePath "Schedule I_Data\sharedassets0.assets"
    if (Test-Path $c) { $game = $c }
}
if (-not $game) {
    foreach ($lib in (Get-SteamLibraries)) {
        $c = Join-Path $lib "steamapps\common\Schedule I\Schedule I_Data\sharedassets0.assets"
        if (Test-Path $c) { $game = $c; break }
    }
}
if (-not $game) {
    Write-Host "게임 파일을 자동으로 찾지 못했습니다." -ForegroundColor Yellow
    Write-Host "Steam 라이브러리에서 Schedule I 우클릭 > 관리 > 로컬 파일 보기 로 열리는 폴더의 경로를 복사해 붙여넣으세요."
    $inp = Read-Host "Schedule I 게임 폴더 경로"
    if ($inp) { $inp = $inp.Trim('"').Trim() }
    if ($inp -and (Test-Path (Join-Path $inp "Schedule I_Data\sharedassets0.assets"))) {
        $game = Join-Path $inp "Schedule I_Data\sharedassets0.assets"
    } else {
        Write-Host "해당 경로에서 게임 파일을 찾을 수 없습니다." -ForegroundColor Red
        if (-not $NoPause) { pause }
        exit 1
    }
}
if (Get-Process -Name "Schedule I" -ErrorAction SilentlyContinue) { Write-Host "게임을 먼저 종료하세요." -ForegroundColor Red; pause; exit 1 }

$ttfPath = Join-Path $here "payload\BMEULJIROTTF.ttf"
$ttf = [System.IO.File]::ReadAllBytes($ttfPath)
$exclude = @("fs-sevegment","LegacyRuntime")
Write-Host ("대상: " + $game)
Write-Host ("폰트: 을지로체 (" + [math]::Round($ttf.Length/1KB,0) + " KB)")

# 백업
$bakDir = Join-Path (Split-Path (Split-Path $game)) "BepInEx\Translation\_backup"
New-Item -ItemType Directory -Force $bakDir | Out-Null
$bak = Join-Path $bakDir "sharedassets0.assets.original"
if (-not (Test-Path $bak)) { Copy-Item $game $bak; Write-Host "원본 백업 생성" }

# 인터넷에서 받은 ZIP의 파일 차단 해제 (Mark of the Web으로 DLL 로드가 거부되는 것 방지)
try { Get-ChildItem $here -Recurse -File | Unblock-File -ErrorAction SilentlyContinue } catch {}
$atDll = Join-Path $here "payload\tools\AssetsTools.NET.dll"
try { Add-Type -Path $atDll } catch { [void][System.Reflection.Assembly]::UnsafeLoadFrom($atDll) }
$am = New-Object AssetsTools.NET.Extra.AssetsManager
$am.LoadClassPackage((Join-Path $here "payload\tools\classdata.tpk")) | Out-Null
$inst = $am.LoadAssetsFile($game, $false)
$am.LoadClassDatabaseFromPackage($inst.file.Metadata.UnityVersion) | Out-Null
$dataOff = $inst.file.Header.DataOffset
$fileBytes = [System.IO.File]::ReadAllBytes($game)
$patched = 0; $skipped = 0
foreach ($info in $inst.file.GetAssetsOfType(128)) {
    $bf = $am.GetBaseField($inst, $info)
    $name = $bf["m_Name"].AsString
    $isEx = $false
    foreach ($ex in $exclude) { if ($name -like ("*" + $ex + "*")) { $isEx = $true } }
    $oldLen = $bf["m_FontData"]["Array"].Children.Count
    if ($isEx) { Write-Host ("  [제외] " + $name); continue }
    if ($oldLen -eq $ttf.Length) { Write-Host ("  [이미 적용됨] " + $name); $skipped++; continue }
    if ($oldLen -le 0) { Write-Host ("  [데이터 없음] " + $name); continue }
    $start = $dataOff + $info.ByteOffset
    $size = $info.ByteSize
    $raw = New-Object byte[] $size
    [Array]::Copy($fileBytes, $start, $raw, 0, $size)
    $pos = -1
    for ($i=0; $i -lt $size-8; $i++) {
        if ([BitConverter]::ToInt32($raw,$i) -eq $oldLen) {
            $sig = ($raw[$i+4] -eq 0 -and $raw[$i+5] -eq 1) -or ($raw[$i+4] -eq 0x4F -and $raw[$i+5] -eq 0x54)
            if ($sig) { $pos = $i; break }
        }
    }
    if ($pos -lt 0) { Write-Host ("  [건너뜀-위치실패] " + $name) -ForegroundColor Yellow; continue }
    $arrEnd = $pos + 4 + $oldLen
    $arrEndAligned = [int](($arrEnd + 3) -band (-bnot 3))
    $suffix = New-Object byte[] ($size - $arrEndAligned)
    [Array]::Copy($raw, $arrEndAligned, $suffix, 0, $suffix.Length)
    $newArrEnd = $pos + 4 + $ttf.Length
    $newArrEndAligned = [int](($newArrEnd + 3) -band (-bnot 3))
    $newRaw = New-Object byte[] ($newArrEndAligned + $suffix.Length)
    [Array]::Copy($raw, 0, $newRaw, 0, $pos)
    [Array]::Copy([BitConverter]::GetBytes($ttf.Length), 0, $newRaw, $pos, 4)
    [Array]::Copy($ttf, 0, $newRaw, $pos+4, $ttf.Length)
    [Array]::Copy($suffix, 0, $newRaw, $newArrEndAligned, $suffix.Length)
    $info.SetNewData($newRaw)
    Write-Host ("  [교체] " + $name)
    $patched++
}
if ($patched -gt 0) {
    $tmp = $game + ".new"
    $writer = New-Object AssetsTools.NET.AssetsFileWriter($tmp)
    $inst.file.Write($writer, -1)
    $writer.Close()
    $am.UnloadAll()
    # 검증 후 교체
    $am2 = New-Object AssetsTools.NET.Extra.AssetsManager
    $am2.LoadClassPackage((Join-Path $here "payload\tools\classdata.tpk")) | Out-Null
    $inst2 = $am2.LoadAssetsFile($tmp, $false)
    $am2.LoadClassDatabaseFromPackage($inst2.file.Metadata.UnityVersion) | Out-Null
    $ok = $true
    foreach ($info in $inst2.file.GetAssetsOfType(128)) {
        $bf = $am2.GetBaseField($inst2, $info)
        $n = $bf["m_Name"].AsString
        $isEx = $false; foreach ($ex in $exclude) { if ($n -like ("*" + $ex + "*")) { $isEx = $true } }
        if (-not $isEx -and $bf["m_FontData"]["Array"].Children.Count -ne $ttf.Length -and $bf["m_FontData"]["Array"].Children.Count -gt 0) { $ok = $false }
    }
    $am2.UnloadAll()
    if ($ok) { Move-Item $tmp $game -Force; Write-Host ("완료: " + $patched + "개 교체") -ForegroundColor Green }
    else { [System.IO.File]::Delete($tmp); Write-Host "검증 실패 - 원본 유지" -ForegroundColor Red }
} else {
    $am.UnloadAll()
    Write-Host ("교체할 폰트 없음 (이미 적용됨: " + $skipped + "개)") -ForegroundColor Green
}
if (-not $NoPause) { pause }