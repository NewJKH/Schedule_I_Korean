# 번역기 설정 파일 잠금 해제 (설치기가 걸어둔 쓰기 거부 권한을 되돌림)
$ErrorActionPreference = "Continue"

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

$gamePath = $null
foreach ($lib in (Get-SteamLibraries)) {
    $c = Join-Path $lib "steamapps\common\Schedule I"
    if (Test-Path (Join-Path $c "Schedule I.exe")) { $gamePath = $c; break }
}
if (-not $gamePath) {
    Write-Host "게임 폴더를 자동으로 찾지 못했습니다." -ForegroundColor Yellow
    $gamePath = Read-Host "Schedule I 게임 폴더 경로"
    if ($gamePath) { $gamePath = $gamePath.Trim('"').Trim() }
}
if (-not $gamePath -or -not (Test-Path $gamePath)) { Write-Host "잘못된 경로입니다." -ForegroundColor Red; pause; exit 1 }

$me = "$env:USERDOMAIN\$env:USERNAME"
$found = $false
foreach ($cfg in @((Join-Path $gamePath "AutoTranslator\Config.ini"),
                   (Join-Path $gamePath "BepInEx\config\AutoTranslatorConfig.ini"))) {
    if (Test-Path $cfg) {
        & icacls "$cfg" /remove:d "$me" | Out-Null
        Write-Host ("잠금 해제: " + $cfg) -ForegroundColor Green
        $found = $true
    }
}
if (-not $found) { Write-Host "잠긴 설정 파일을 찾지 못했습니다 (이미 해제되었거나 미설치)." -ForegroundColor DarkGray }
pause
