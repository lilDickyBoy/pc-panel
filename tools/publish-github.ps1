# PC Panel - publish to GitHub (source + release with APK and Windows zip)
# Uses the GitHub REST API, so Git does not need to be installed.
# The token is only kept in memory for this run and is never written to disk.

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Root    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Repo    = 'pc-panel'
$Version = '2.0.1'
$Tag     = "v$Version"
$Rel     = Join-Path $Root '_release'

function Say($t, $c = 'Gray') { Write-Host "  $t" -ForegroundColor $c }
function Fail($t) { Write-Host ""; Say "HATA / ERROR: $t" Red; Write-Host ""; Read-Host "  Kapatmak icin Enter"; exit 1 }

Write-Host ""
Say "PC Panel -> GitHub" Cyan
Say "----------------------------------------------"
Say "1) Tarayicida token sayfasi acilacak (GitHub hesabin acik olmali)."
Say "2) En alttaki yesil 'Generate token' butonuna bas."
Say "3) 'ghp_' ile baslayan kodu kopyala, buraya yapistir (sag tik) ve Enter'a bas."
Say "   (Yazdigin sey ekranda gorunmez, bu normal.)"
Write-Host ""
Start-Process 'https://github.com/settings/tokens/new?scopes=repo&description=PC%20Panel%20publish'

$sec = Read-Host "  Token" -AsSecureString
$Token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
$Token = $Token.Trim()
if (-not $Token) { Fail "Token bos." }

$H = @{ Authorization = "token $Token"; 'User-Agent' = 'pc-panel-publish'; Accept = 'application/vnd.github+json' }

function Api($method, $url, $body = $null) {
    if ($url -notmatch '^https://') { $url = "https://api.github.com$url" }
    $req = @{ Method = $method; Uri = $url; Headers = $H }
    if ($body -ne $null) {
        $json = $body | ConvertTo-Json -Depth 20 -Compress
        $req.Body = [Text.Encoding]::UTF8.GetBytes($json)
        $req.ContentType = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @req
}

# --- who am I -------------------------------------------------------------
try { $me = Api GET '/user' } catch { Fail "Token gecersiz ya da 'repo' izni yok. ($($_.Exception.Message))" }
$User = $me.login
Say "Giris yapildi: $User" Green

# --- point the in-app GitHub links at the real repo ------------------------
$cs = Join-Path $Root 'PC\PcPanel\UI\MainWindow.xaml.cs'
$kt = Join-Path $Root 'Android\app\src\main\java\com\pcpanel\app\ui\screens\Settings.kt'
foreach ($f in @($cs, $kt)) {
    $t = [IO.File]::ReadAllText($f)
    $n = [regex]::Replace($t, '"pc-panel/pc-panel"', "`"$User/$Repo`"")
    $n = [regex]::Replace($n, 'https://github\.com/[A-Za-z0-9-]+/pc-panel"', "https://github.com/$User/$Repo`"")
    if ($n -ne $t) { [IO.File]::WriteAllText($f, $n, (New-Object Text.UTF8Encoding($false))) }
}
Say "Uygulama icindeki GitHub linkleri guncellendi -> github.com/$User/$Repo"

# --- build release files ---------------------------------------------------
New-Item -ItemType Directory -Force $Rel | Out-Null
Say "Android APK derleniyor (birkac dakika surebilir)..." Cyan
& cmd /c "`"$Root\Android\Build-APK.bat`" < nul" | Out-Null
$apk = Join-Path $Root 'PC-Panel.apk'
if (-not (Test-Path $apk) -or ((Get-Item $apk).LastWriteTime -lt (Get-Date).AddMinutes(-30))) { Fail "APK derlenemedi. Android\apk-build-log.txt dosyasina bak." }
Copy-Item $apk (Join-Path $Rel 'PC-Panel.apk') -Force
Say "APK hazir." Green

Say "Windows surumu derleniyor..." Cyan
$pcOut = Join-Path $Rel 'PC Panel'
if (Test-Path $pcOut) { Remove-Item $pcOut -Recurse -Force }
& dotnet publish "$Root\PC\PcPanel\PcPanel.csproj" -c Release -r win-x64 --self-contained false -o $pcOut 2>&1 | Out-File (Join-Path $Rel 'pc-build-log.txt')
if (-not (Test-Path (Join-Path $pcOut 'PCPanel.exe'))) { Fail "PC surumu derlenemedi. _release\pc-build-log.txt dosyasina bak." }
$zip = Join-Path $Rel 'PC-Panel-Windows.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Get-ChildItem $pcOut -Filter *.pdb -Recurse | Remove-Item -Force
Compress-Archive -Path $pcOut -DestinationPath $zip
Say "Windows zip hazir." Green

# --- create the repo -------------------------------------------------------
try {
    Api GET "/repos/$User/$Repo" | Out-Null
    Say "Depo zaten var: $User/$Repo"
} catch {
    Say "Depo olusturuluyor: $User/$Repo" Cyan
    Api POST '/user/repos' @{ name = $Repo; description = 'Control your Windows PC from your Android phone - live stats, music, notch / Dynamic Island, files, clipboard, fans and more.'; auto_init = $true; has_wiki = $false } | Out-Null
}

$ref = $null
for ($i = 0; $i -lt 15 -and -not $ref; $i++) {
    try { $ref = Api GET "/repos/$User/$Repo/git/ref/heads/main" } catch { Start-Sleep 2 }
}
if (-not $ref) { Fail "Depo hazir olmadi, birazdan tekrar dene." }

# --- upload the source -----------------------------------------------------
$skipDirs  = @('bin', 'obj', '_check', '.gradle', 'build', '.idea', '_release', '.kotlin', '.vs', 'captures')
$skipFiles = @('local.properties')
$files = Get-ChildItem $Root -Recurse -File -Force | Where-Object {
    $rel = $_.FullName.Substring($Root.Length + 1)
    $dirs = @($rel -split '[\\/]' | Select-Object -SkipLast 1)
    -not ($dirs | Where-Object { $skipDirs -contains $_ }) -and
    -not ($skipFiles -contains $_.Name) -and
    $_.Extension -notin @('.apk', '.zip', '.iml') -and
    $_.Name -notlike '*-log.txt'
}
Say "Kaynak kod yukleniyor ($($files.Count) dosya)..." Cyan
$tree = @()
$n = 0
foreach ($f in $files) {
    $n++
    $rel = $f.FullName.Substring($Root.Length + 1).Replace('\', '/')
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f.FullName))
    $blob = Api POST "/repos/$User/$Repo/git/blobs" @{ content = $b64; encoding = 'base64' }
    $mode = if ($f.Name -eq 'gradlew') { '100755' } else { '100644' }
    $tree += @{ path = $rel; mode = $mode; type = 'blob'; sha = $blob.sha }
    if ($n % 20 -eq 0) { Say "  $n / $($files.Count)" }
}
$t = Api POST "/repos/$User/$Repo/git/trees" @{ tree = $tree }
$c = Api POST "/repos/$User/$Repo/git/commits" @{ message = "PC Panel $Version"; tree = $t.sha; parents = @($ref.object.sha) }
Api PATCH "/repos/$User/$Repo/git/refs/heads/main" @{ sha = $c.sha; force = $true } | Out-Null
try { Api PUT "/repos/$User/$Repo/topics" @{ names = @('android', 'windows', 'remote-control', 'wpf', 'jetpack-compose', 'dynamic-island', 'spotify', 'hardware-monitor') } | Out-Null } catch { }
Say "Kaynak kod yuklendi." Green

# --- release ---------------------------------------------------------------
$notes = @"
## PC Panel $Version

**What's new**
- Fixed Spotify login (``redirect_uri: Not present`` error)
- Links with parameters now open correctly in every browser

**Download**
- ``PC-Panel.apk`` - Android phone app (installs over the old MobiLink app as an update)
- ``PC-Panel-Windows.zip`` - Windows app; extract and run ``PCPanel.exe`` (needs the .NET 8 Desktop Runtime)

See the README for Spotify setup and notification access.
"@
try {
    $release = Api GET "/repos/$User/$Repo/releases/tags/$Tag"
    foreach ($a in $release.assets) { Api DELETE "/repos/$User/$Repo/releases/assets/$($a.id)" | Out-Null }
} catch {
    $release = Api POST "/repos/$User/$Repo/releases" @{ tag_name = $Tag; target_commitish = 'main'; name = "PC Panel $Version"; body = $notes }
}
foreach ($asset in @('PC-Panel.apk', 'PC-Panel-Windows.zip')) {
    Say "Yukleniyor: $asset" Cyan
    $p = Join-Path $Rel $asset
    $ct = if ($asset -like '*.apk') { 'application/vnd.android.package-archive' } else { 'application/zip' }
    Invoke-RestMethod -Method Post -Uri "https://uploads.github.com/repos/$User/$Repo/releases/$($release.id)/assets?name=$asset" -Headers $H -ContentType $ct -InFile $p | Out-Null
}

$Token = $null
Write-Host ""
Say "TAMAMLANDI!  https://github.com/$User/$Repo" Green
Say "(Istersen token'i github.com/settings/tokens adresinden silebilirsin.)"
Start-Process "https://github.com/$User/$Repo"
Write-Host ""
Read-Host "  Kapatmak icin Enter"
