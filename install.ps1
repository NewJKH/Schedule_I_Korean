# Schedule I 한글패치 통합 설치기
# 모드 로더(MelonLoader / BepInEx)와 XUnity.AutoTranslator가 없으면 공식 저장소에서 자동 설치합니다.
param([string]$Loader = "")
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
try { Get-ChildItem $here -Recurse -File | Unblock-File -ErrorAction SilentlyContinue } catch {}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "   Schedule I 한글패치 설치기" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan

# ---------- 1) 게임 경로 찾기 (레지스트리로 Steam 위치 파악) ----------
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

$gamePath = $null
foreach ($lib in (Get-SteamLibraries)) {
    $c = Join-Path $lib "steamapps\common\Schedule I"
    if (Test-Path (Join-Path $c "Schedule I.exe")) { $gamePath = $c; break }
}
if (-not $gamePath) {
    Write-Host "게임 폴더를 자동으로 찾지 못했습니다." -ForegroundColor Yellow
    Write-Host "Steam 라이브러리에서 Schedule I 우클릭 > 관리 > 로컬 파일 보기 로 열리는 폴더의 경로를 복사해 붙여넣으세요."
    $gamePath = Read-Host "Schedule I 게임 폴더 경로"
    if ($gamePath) { $gamePath = $gamePath.Trim('"').Trim() }
    if (-not $gamePath -or -not (Test-Path (Join-Path $gamePath "Schedule I.exe"))) { Write-Host "잘못된 경로입니다." -ForegroundColor Red; pause; exit 1 }
}
Write-Host ("게임 경로: " + $gamePath) -ForegroundColor Green

if (Get-Process -Name "Schedule I" -ErrorAction SilentlyContinue) {
    Write-Host "게임이 실행 중입니다. 완전히 종료한 뒤 다시 실행해주세요." -ForegroundColor Red
    pause; exit 1
}

$tmp = Join-Path $env:TEMP "s1kr_setup"
New-Item -ItemType Directory -Force $tmp | Out-Null

# ---------- 2) 모드 로더 선택 ----------
# MelonLoader = version.dll 로 주입, BepInEx(IL2CPP) = winhttp.dll 로 주입.
# 둘 다 IL2CPP 런타임을 각자 띄우기 때문에 동시에 켜두면 충돌할 수 있어 하나만 쓴다.
$mlCore    = Join-Path $gamePath "MelonLoader\net6\MelonLoader.dll"
$bepCore   = Join-Path $gamePath "BepInEx\core\BepInEx.Core.dll"
$mlInject  = Join-Path $gamePath "version.dll"
$bepInject = Join-Path $gamePath "winhttp.dll"
$modsDir   = Join-Path $gamePath "Mods"
$hasML     = Test-Path $mlCore
$hasBep    = Test-Path $bepCore
$otherMods = @()
if (Test-Path $modsDir) {
    $otherMods = @(Get-ChildItem $modsDir -Filter "*.dll" -ErrorAction SilentlyContinue |
                   Where-Object { $_.Name -ne "KoreanTextFixerML.dll" -and $_.Name -notlike "XUnity.AutoTranslator*" })
}

if ($Loader) {
    $useML = ($Loader -match '^(?i)(m|ml|melon|melonloader)$')
    if (-not $useML -and -not ($Loader -match '^(?i)(b|bep|bepinex)$')) {
        Write-Host "알 수 없는 -Loader 값입니다: $Loader (melon 또는 bepinex)" -ForegroundColor Red; pause; exit 1
    }
} else {
    # 이미 MelonLoader를 쓰고 있거나 다른 모드가 깔려 있으면 MelonLoader를 기본값으로 제안
    $defML = ($hasML -or $otherMods.Count -gt 0)
    Write-Host ""
    Write-Host "----------------------------------------------" -ForegroundColor Yellow
    Write-Host " 어떤 모드 로더에 설치할까요?" -ForegroundColor Yellow
    Write-Host ""
    if ($hasML)  { Write-Host " * MelonLoader 설치됨" -ForegroundColor DarkGray }
    if ($hasBep) { Write-Host " * BepInEx 설치됨" -ForegroundColor DarkGray }
    if ($otherMods.Count -gt 0) { Write-Host (" * Mods 폴더에 다른 모드 " + $otherMods.Count + "개 있음: " + (($otherMods | Select-Object -First 4 | ForEach-Object { $_.Name }) -join ", ")) -ForegroundColor DarkGray }
    Write-Host ""
    Write-Host "   [1] MelonLoader  - 다른 모드도 같이 쓰는 경우"
    Write-Host "   [2] BepInEx      - 한글패치만 쓰는 경우"
    Write-Host "----------------------------------------------" -ForegroundColor Yellow
    if ($defML) { $ans = Read-Host " 번호 입력 [기본: 1 MelonLoader]" } else { $ans = Read-Host " 번호 입력 [기본: 2 BepInEx]" }
    if ($ans -match '^\s*1\s*$') { $useML = $true }
    elseif ($ans -match '^\s*2\s*$') { $useML = $false }
    else { $useML = $defML }
}

if ($useML) {
    $loaderName = "MelonLoader"
    $textDir    = Join-Path $gamePath "AutoTranslator\Translation\ko\Text"
    $cfg        = Join-Path $gamePath "AutoTranslator\Config.ini"
    $pluginSrc  = Join-Path $here "payload\KoreanTextFixerML.dll"
    $pluginDst  = Join-Path $gamePath "Mods\KoreanTextFixerML.dll"
    $xuatDll    = Join-Path $gamePath "UserLibs\XUnity.AutoTranslator.Plugin.Core.dll"
    $xuUrl      = "https://github.com/bbepis/XUnity.AutoTranslator/releases/download/v5.6.1/XUnity.AutoTranslator-MelonMod-IL2CPP-5.6.1.zip"
} else {
    $loaderName = "BepInEx"
    $textDir    = Join-Path $gamePath "BepInEx\Translation\ko\Text"
    $cfg        = Join-Path $gamePath "BepInEx\config\AutoTranslatorConfig.ini"
    $pluginSrc  = Join-Path $here "payload\KoreanTextFixer.dll"
    $pluginDst  = Join-Path $gamePath "BepInEx\plugins\KoreanTextFixer.dll"
    $xuatDll    = Join-Path $gamePath "BepInEx\plugins\XUnity.AutoTranslator\XUnity.AutoTranslator.Plugin.Core.dll"
    $xuUrl      = "https://github.com/bbepis/XUnity.AutoTranslator/releases/download/v5.6.1/XUnity.AutoTranslator-BepInEx-IL2CPP-5.6.1.zip"
}
Write-Host ("사용할 로더: " + $loaderName) -ForegroundColor Green

# 반대쪽 로더가 살아있으면 충돌하므로 주입 DLL을 비활성화(이름 변경)한다
if ($useML -and (Test-Path $bepInject)) {
    Write-Host ""
    Write-Host " BepInEx도 함께 켜져 있습니다. IL2CPP에서는 두 로더를 동시에 쓰면 충돌·크래시가 납니다." -ForegroundColor Yellow
    $d = Read-Host " BepInEx를 잠시 꺼둘까요? (winhttp.dll -> winhttp.dll.disabled) [Y] 예 (권장) / [N] 아니오"
    if ($d -eq "" -or $d -match '^[YyㅛJj]') {
        Move-Item $bepInject ($bepInject + ".disabled") -Force
        Write-Host "  BepInEx 비활성화 완료 (되돌리려면 .disabled 확장자를 지우면 됩니다)" -ForegroundColor Green
    }
}
if (-not $useML -and (Test-Path $mlInject) -and $hasML) {
    Write-Host ""
    Write-Host " MelonLoader가 설치되어 있습니다. BepInEx와 동시에 켜두면 충돌할 수 있습니다." -ForegroundColor Yellow
    Write-Host " 다른 모드를 쓰신다면 설치를 취소하고 [1] MelonLoader 로 다시 설치하는 편이 좋습니다." -ForegroundColor Yellow
    $d = Read-Host " 그래도 BepInEx로 계속할까요? [Y] 예 / [N] 아니오"
    if ($d -match '^[NnㅜKk]') { Write-Host "설치를 취소했습니다." -ForegroundColor DarkGray; pause; exit 0 }
}

# ---------- 3) 모드 로더 자동 설치 ----------
if ($useML) {
    if ($hasML -and -not (Test-Path $mlInject) -and (Test-Path ($mlInject + ".disabled"))) {
        Move-Item ($mlInject + ".disabled") $mlInject -Force
        Write-Host "[1/4] 꺼져 있던 MelonLoader를 다시 켰습니다" -ForegroundColor Green
    }
    elseif (-not $hasML -or -not (Test-Path $mlInject)) {
        # MelonLoader 0.7은 시스템에 설치된 .NET 6 이상 런타임을 사용한다
        $netOk = $false
        $shared = Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.NETCore.App"
        if (Test-Path $shared) {
            foreach ($d in (Get-ChildItem $shared -Directory -ErrorAction SilentlyContinue)) {
                $maj = 0
                if ([int]::TryParse($d.Name.Split('.')[0], [ref]$maj) -and $maj -ge 6) { $netOk = $true }
            }
        }
        if (-not $netOk) {
            Write-Host "[1/4] MelonLoader 설치에 필요한 .NET 런타임(6.0 이상)이 없습니다." -ForegroundColor Red
            Write-Host "  아래에서 '.NET Desktop Runtime x64'를 설치한 뒤 다시 실행해주세요."
            Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0"
            Write-Host "  (또는 MelonLoader 공식 설치기를 쓰면 런타임까지 알아서 잡아줍니다: https://github.com/LavaGang/MelonLoader/releases)"
            pause; exit 1
        }
        Write-Host "[1/4] MelonLoader 다운로드 중... (GitHub 공식 릴리스, 약 20MB)" -ForegroundColor Cyan
        $mlUrl = "https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/MelonLoader.x64.zip"
        $mlZip = Join-Path $tmp "melonloader.zip"
        Invoke-WebRequest -Uri $mlUrl -OutFile $mlZip -UseBasicParsing
        Expand-Archive $mlZip (Join-Path $tmp "melonloader") -Force
        Copy-Item (Join-Path $tmp "melonloader\*") $gamePath -Recurse -Force
        Write-Host "  MelonLoader 설치 완료 (첫 실행 때 게임 분석에 2~5분 걸립니다)" -ForegroundColor Green
    } else {
        Write-Host "[1/4] MelonLoader 이미 설치됨 - 건너뜀" -ForegroundColor DarkGray
    }
} else {
    if ($hasBep -and -not (Test-Path $bepInject) -and (Test-Path ($bepInject + ".disabled"))) {
        Move-Item ($bepInject + ".disabled") $bepInject -Force
        Write-Host "[1/4] 꺼져 있던 BepInEx를 다시 켰습니다" -ForegroundColor Green
    }
    elseif (-not $hasBep -or -not (Test-Path $bepInject)) {
        Write-Host "[1/4] BepInEx 다운로드 중... (공식 빌드 서버)" -ForegroundColor Cyan
        $bepUrl = "https://builds.bepinex.dev/projects/bepinex_be/733/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.733%2B995f049.zip"
        $bepZip = Join-Path $tmp "bepinex.zip"
        Invoke-WebRequest -Uri $bepUrl -OutFile $bepZip -UseBasicParsing
        Expand-Archive $bepZip (Join-Path $tmp "bepinex") -Force
        Copy-Item (Join-Path $tmp "bepinex\*") $gamePath -Recurse -Force
        Write-Host "  BepInEx 설치 완료" -ForegroundColor Green
    } else {
        Write-Host "[1/4] BepInEx 이미 설치됨 - 건너뜀" -ForegroundColor DarkGray
    }
}

# ---------- 4) XUnity.AutoTranslator 자동 설치 ----------
$needXuat = $true
if (Test-Path $xuatDll) {
    $v = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($xuatDll).FileVersion
    if ($v -and ([version]$v -ge [version]"5.6.1")) { $needXuat = $false }
}
if ($needXuat) {
    Write-Host ("[2/4] XUnity.AutoTranslator 5.6.1 다운로드 중... (GitHub 공식, " + $loaderName + "판)") -ForegroundColor Cyan
    $xuZip = Join-Path $tmp "xuat.zip"
    $xuDir = Join-Path $tmp "xuat"
    if (Test-Path $xuDir) { Remove-Item $xuDir -Recurse -Force }
    Invoke-WebRequest -Uri $xuUrl -OutFile $xuZip -UseBasicParsing
    Expand-Archive $xuZip $xuDir -Force
    Copy-Item (Join-Path $xuDir "*") $gamePath -Recurse -Force
    Write-Host "  번역기 설치 완료" -ForegroundColor Green
} else {
    Write-Host "[2/4] 번역기 5.6.1+ 이미 설치됨 - 건너뜀" -ForegroundColor DarkGray
}

# ---------- 5) 번역 파일 + 보조 플러그인 ----------
Write-Host "[3/4] 한글 번역 파일 설치 중..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $textDir | Out-Null
foreach ($f in (Get-ChildItem (Join-Path $here "payload\Text") -Filter "*.txt")) {
    Copy-Item $f.FullName (Join-Path $textDir $f.Name) -Force
}
New-Item -ItemType Directory -Force (Split-Path $pluginDst) | Out-Null
Copy-Item $pluginSrc $pluginDst -Force
$stale = Join-Path $textDir "_AutoGeneratedTranslations.txt"
if (Test-Path $stale) { Remove-Item $stale -Force }
# 번역기가 시작할 때 정리하려 드는 파일. 오프라인이라 생길 일이 없어서 빈 파일로 만들어 둔다 (로그 에러 방지)
$outFile = Join-Path (Split-Path $textDir) "_AutoGeneratedOutput.txt"
if (-not (Test-Path $outFile)) { New-Item -ItemType File -Path $outFile | Out-Null }
Write-Host "  번역 58,000여 개 + 보조 플러그인 설치 완료" -ForegroundColor Green

# ---------- 6) 설정 적용 + 잠금 ----------
Write-Host "[4/4] 번역기 설정 적용 중..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force (Split-Path $cfg) | Out-Null
$me = "$env:USERDOMAIN\$env:USERNAME"
if (Test-Path $cfg) { & icacls "$cfg" /remove:d "$me" 2>&1 | Out-Null }
Copy-Item (Join-Path $here "payload\config\AutoTranslatorConfig.ini") $cfg -Force
& icacls "$cfg" /deny "${me}:(WD,AD,WEA,WA)" 2>&1 | Out-Null
Write-Host "  설정 적용 및 잠금 완료 (게임이 설정을 되돌리는 것 방지)" -ForegroundColor Green

# ---------- 7) 폰트 적용 여부 확인 ----------
Write-Host ""
Write-Host "----------------------------------------------" -ForegroundColor Yellow
Write-Host " 한글 폰트(을지로체)도 적용할까요?" -ForegroundColor Yellow
Write-Host ""
Write-Host " 게임 기본 폰트에는 한글이 없어서, 적용하지 않으면"
Write-Host " 번역된 글자가 네모(ㅁ)로 깨져 보일 수 있습니다."
Write-Host ""
Write-Host " * 게임 원본 파일 1개를 수정합니다 (원본은 자동 백업)"
Write-Host " * 되돌리려면: Steam > 속성 > 설치된 파일 > 파일 무결성 확인"
Write-Host "----------------------------------------------" -ForegroundColor Yellow
$ans = Read-Host " 적용하시겠습니까? [Y] 예 (권장) / [N] 아니오"
if ($ans -eq "" -or $ans -match '^[YyㅛJj]') {
    Write-Host ""
    Write-Host "[폰트] 을지로체 적용 중..." -ForegroundColor Cyan
    $fontScript = Join-Path $here "font_patch.ps1"
    if (Test-Path $fontScript) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $fontScript -NoPause -GamePath "$gamePath"
        if ($LASTEXITCODE -eq 0) { Write-Host "  폰트 적용 완료" -ForegroundColor Green }
        else { Write-Host "  폰트 적용에 실패했습니다. 나중에 '폰트적용.bat'을 실행해보세요." -ForegroundColor Yellow }
    } else {
        Write-Host "  font_patch.ps1 을 찾을 수 없습니다." -ForegroundColor Yellow
    }
} else {
    Write-Host " 폰트는 건너뜁니다. 나중에 '폰트적용.bat'으로 적용할 수 있습니다." -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "==============================================" -ForegroundColor Green
Write-Host ("   설치 완료! (" + $loaderName + ") 게임을 실행하세요.") -ForegroundColor Green
Write-Host "==============================================" -ForegroundColor Green
if ($useML) {
    Write-Host " 번역 파일 위치: AutoTranslator\Translation\ko\Text" -ForegroundColor DarkGray
    Write-Host " 로그 확인: MelonLoader\Latest.log" -ForegroundColor DarkGray
} else {
    Write-Host " 번역 파일 위치: BepInEx\Translation\ko\Text" -ForegroundColor DarkGray
    Write-Host " 로그 확인: BepInEx\LogOutput.log" -ForegroundColor DarkGray
}
pause
