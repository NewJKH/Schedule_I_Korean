param([string]$GamePath = "")
# KoreanTextFixer 플러그인 빌드 (BepInEx판 + MelonLoader판) -> payload 에 복사
# 필요: .NET SDK 6 이상, 그리고 한 번 이상 실행해 참조 DLL이 생성된 게임 폴더
#   - BepInEx판: <게임>\BepInEx\interop
#   - MelonLoader판: <게임>\MelonLoader\Il2CppAssemblies
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

if (-not $GamePath) {
    foreach ($lib in (Get-SteamLibraries)) {
        $c = Join-Path $lib "steamapps\common\Schedule I"
        if (Test-Path (Join-Path $c "Schedule I.exe")) { $GamePath = $c; break }
    }
}
if (-not $GamePath -or -not (Test-Path (Join-Path $GamePath "Schedule I.exe"))) {
    Write-Host "게임 폴더를 찾지 못했습니다. -GamePath 로 직접 지정하세요." -ForegroundColor Red; exit 1
}
Write-Host ("게임 경로: " + $GamePath) -ForegroundColor Green

$built = 0
if (Test-Path (Join-Path $GamePath "BepInEx\interop\UnityEngine.CoreModule.dll")) {
    dotnet build (Join-Path $here "src\KoreanTextFixer\KoreanTextFixer.csproj") -c Release -p:GameDir="$GamePath"
    if ($LASTEXITCODE -ne 0) { exit 1 }
    Copy-Item (Join-Path $here "src\KoreanTextFixer\bin\Release\KoreanTextFixer.dll") (Join-Path $here "payload\KoreanTextFixer.dll") -Force
    Write-Host "  payload\KoreanTextFixer.dll 갱신" -ForegroundColor Green
    $built++
} else {
    Write-Host "BepInEx interop 폴더가 없어 BepInEx판은 건너뜁니다 (BepInEx로 게임을 한 번 실행하면 생성됨)" -ForegroundColor Yellow
}
if (Test-Path (Join-Path $GamePath "MelonLoader\Il2CppAssemblies\UnityEngine.CoreModule.dll")) {
    dotnet build (Join-Path $here "src\KoreanTextFixerML\KoreanTextFixerML.csproj") -c Release -p:GameDir="$GamePath"
    if ($LASTEXITCODE -ne 0) { exit 1 }
    Copy-Item (Join-Path $here "src\KoreanTextFixerML\bin\Release\KoreanTextFixerML.dll") (Join-Path $here "payload\KoreanTextFixerML.dll") -Force
    Write-Host "  payload\KoreanTextFixerML.dll 갱신" -ForegroundColor Green
    $built++
} else {
    Write-Host "MelonLoader Il2CppAssemblies 폴더가 없어 MelonLoader판은 건너뜁니다 (MelonLoader로 게임을 한 번 실행하면 생성됨)" -ForegroundColor Yellow
}
if ($built -eq 0) { exit 1 }
