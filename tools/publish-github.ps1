# PC Panel - publish to GitHub.
# Uploads the source, creates a release, and lets GitHub Actions build the APK + Windows zip in the cloud
# (so no Android tools are needed on this PC). Git does not need to be installed.

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Root    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Repo    = 'pc-panel'
$Version = '2.2.0'
$Tag     = "v$Version"
$TokenFile = Join-Path $env:APPDATA 'PC Panel\github-token.bin'
Add-Type -AssemblyName System.Security

function Say($t, $c = 'Gray') { Write-Host "  $t" -ForegroundColor $c }
function Fail($t) { Write-Host ""; Say "HATA / ERROR: $t" Red; Write-Host ""; Read-Host "  Kapatmak icin Enter"; exit 1 }

Write-Host ""
Say "PC Panel $Version -> GitHub" Cyan
Say "----------------------------------------------"

# ---------------------------------------------------------------- token
$H = $null; $User = $null
function Try-Token($tok) {
    $h = @{ Authorization = "token $tok"; 'User-Agent' = 'pc-panel-publish'; Accept = 'application/vnd.github+json' }
    try {
        $r = Invoke-WebRequest -UseBasicParsing -Uri 'https://api.github.com/user' -Headers $h
        $scopes = "$($r.Headers['X-OAuth-Scopes'])"
        if ($scopes -notmatch '\brepo\b' -or $scopes -notmatch '\bworkflow\b') { return $null }
        $script:User = ($r.Content | ConvertFrom-Json).login
        return $h
    } catch { return $null }
}

if (Test-Path $TokenFile) {
    try {
        $plain = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($TokenFile), $null, 'CurrentUser'))
        $H = Try-Token $plain
        if ($H) { Say "Kayitli token kullaniliyor." Green } else { Remove-Item $TokenFile -Force }
    } catch { }
}
if (-not $H) {
    Say "1) Tarayicida token sayfasi acilacak. 'repo' ve 'workflow' kutulari isaretli gelir, oyle kalsin."
    Say "2) Expiration: istedigin sure. En alttaki yesil 'Generate token' butonuna bas."
    Say "3) 'ghp_' ile baslayan kodu kopyala, buraya sag tikla yapistir ve Enter'a bas (gorunmez, normal)."
    Write-Host ""
    Start-Process 'https://github.com/settings/tokens/new?scopes=repo,workflow&description=PC%20Panel%20publish'
    $sec = Read-Host "  Token" -AsSecureString
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)).Trim()
    $H = Try-Token $plain
    if (-not $H) { Fail "Token gecersiz ya da 'repo' + 'workflow' izinleri yok." }
    Write-Host ""
    $ans = Read-Host "  Token bu PC'de sifreli saklansin mi? Bir dahakine sormam. (E/H)"
    if ($ans -match '^[eEyY]') {
        New-Item -ItemType Directory -Force (Split-Path $TokenFile) | Out-Null
        [IO.File]::WriteAllBytes($TokenFile, [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($plain), $null, 'CurrentUser'))
        Say "Saklandi (sadece senin Windows hesabin acabilir)." Green
    }
}
$plain = $null
Say "Giris yapildi: $User" Green

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

# ---------------------------------------------------------------- in-app links
$cs = Join-Path $Root 'PC\PcPanel\UI\MainWindow.xaml.cs'
$kt = Join-Path $Root 'Android\app\src\main\java\com\pcpanel\app\ui\screens\Settings.kt'
foreach ($f in @($cs, $kt)) {
    $t = [IO.File]::ReadAllText($f)
    $n = [regex]::Replace($t, '"[A-Za-z0-9-]+/pc-panel"', "`"$User/$Repo`"")
    $n = [regex]::Replace($n, 'https://github\.com/[A-Za-z0-9-]+/pc-panel"', "https://github.com/$User/$Repo`"")
    if ($n -ne $t) { [IO.File]::WriteAllText($f, $n, (New-Object Text.UTF8Encoding($false))) }
}

# ---------------------------------------------------------------- repo
try { Api GET "/repos/$User/$Repo" | Out-Null }
catch {
    Say "Depo olusturuluyor: $User/$Repo" Cyan
    Api POST '/user/repos' @{ name = $Repo; description = 'Control your Windows PC from your Android phone - live stats, music, notch / Dynamic Island, files, clipboard, fans and more.'; auto_init = $true; has_wiki = $false } | Out-Null
}
$ref = $null
for ($i = 0; $i -lt 15 -and -not $ref; $i++) { try { $ref = Api GET "/repos/$User/$Repo/git/ref/heads/main" } catch { Start-Sleep 2 } }
if (-not $ref) { Fail "Depo hazir olmadi, birazdan tekrar dene." }

# ---------------------------------------------------------------- source
$skipDirs  = @('bin', 'obj', '_check', '.gradle', 'build', '.idea', '_release', '.kotlin', '.vs', 'captures')
$files = Get-ChildItem $Root -Recurse -File -Force | Where-Object {
    $rel = $_.FullName.Substring($Root.Length + 1)
    $dirs = @($rel -split '[\\/]' | Select-Object -SkipLast 1)
    -not ($dirs | Where-Object { $skipDirs -contains $_ }) -and
    $_.Name -ne 'local.properties' -and
    $_.Extension -notin @('.apk', '.zip', '.iml') -and
    $_.Name -notlike '*-log.txt'
}
Say "Kaynak kod yukleniyor ($($files.Count) dosya)..." Cyan
$tree = @(); $n = 0
foreach ($f in $files) {
    $n++
    $rel = $f.FullName.Substring($Root.Length + 1).Replace('\', '/')
    if ($rel -eq 'tools/ci/build.yml') { $rel = '.github/workflows/build.yml' }   # the cloud build
    $blob = Api POST "/repos/$User/$Repo/git/blobs" @{ content = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f.FullName)); encoding = 'base64' }
    $tree += @{ path = $rel; mode = $(if ($f.Name -eq 'gradlew') { '100755' } else { '100644' }); type = 'blob'; sha = $blob.sha }
    if ($n % 25 -eq 0) { Say "  $n / $($files.Count)" }
}
$t = Api POST "/repos/$User/$Repo/git/trees" @{ tree = $tree }
$c = Api POST "/repos/$User/$Repo/git/commits" @{ message = "PC Panel $Version"; tree = $t.sha; parents = @($ref.object.sha) }
try { Api PATCH "/repos/$User/$Repo/git/refs/heads/main" @{ sha = $c.sha; force = $true } | Out-Null }
catch { Fail "Kod yuklenemedi. Token'da 'workflow' izni olmayabilir. ($($_.Exception.Message))" }
try { Api PUT "/repos/$User/$Repo/topics" @{ names = @('android', 'windows', 'remote-control', 'wpf', 'jetpack-compose', 'dynamic-island', 'spotify', 'hardware-monitor') } | Out-Null } catch { }
Say "Kaynak kod yuklendi." Green

# ---------------------------------------------------------------- signing key secret (one time)
function Has-Secret { try { Api GET "/repos/$User/$Repo/actions/secrets/DEBUG_KEYSTORE_B64" | Out-Null; return $true } catch { return $false } }
if (-not (Has-Secret)) {
    $ks = Join-Path $env:USERPROFILE '.android\debug.keystore'
    if (-not (Test-Path $ks)) { $ks = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PC Panel imza anahtari\debug.keystore' }
    if (-not (Test-Path $ks)) { Fail "APK imza anahtari bulunamadi (Belgeler\PC Panel imza anahtari\debug.keystore)." }
    Set-Clipboard -Value ([Convert]::ToBase64String([IO.File]::ReadAllBytes($ks)))
    Write-Host ""
    Say "TEK SEFERLIK: APK imza anahtarini GitHub'a gizli olarak ekleyelim." Yellow
    Say "Acilan sayfada:"
    Say "  Name   ->  DEBUG_KEYSTORE_B64        (aynen boyle yaz)"
    Say "  Secret ->  Ctrl+V  (anahtar panoya kopyalandi)"
    Say "  sonra yesil 'Add secret' butonuna bas."
    Start-Process "https://github.com/$User/$Repo/settings/secrets/actions/new"
    for ($i = 0; $i -lt 5 -and -not (Has-Secret); $i++) {
        Read-Host "  Ekledikten sonra burada Enter'a bas"
        if (-not (Has-Secret)) { Say "Henuz gorunmuyor. Name tam olarak DEBUG_KEYSTORE_B64 mi?" Yellow }
    }
    Set-Clipboard -Value ' '
    if (-not (Has-Secret)) { Fail "Gizli anahtar eklenmedi." }
    Say "Imza anahtari eklendi." Green
}

# ---------------------------------------------------------------- release -> cloud build
$notes = @"
## PC Panel $Version

**What's new**
- The notch now spins the real album cover like a vinyl record
- Edge light: a glowing line around the notch (solid, flowing gradient like blue → white → blue, or album colour), set from the PC or the phone
- Synced lyrics: while music plays the notch widens and the lyrics slide in line by line (no account or API key needed)
- Bluetooth: when headphones connect, the notch shows the Bluetooth icon and the device name
- Screenshots you take on the PC go straight to the phone's gallery
- Security alerts + activity log: see exactly when (hour:minute) your PC was turned on, unlocked, or a wrong password was typed
- Download finished alerts for browsers, Steam and Epic Games (no setup)
- Open Spotify / YouTube in the background, real brand logos, notch hides 0.2 s after you pause
- Drag-to-notch fixed (and can be turned off); no more admin prompt every time you open PC Panel
- Phone: default disconnect delay is now 1 minute

**If Play Protect blocks the install:** tap **More details → Install anyway**. If there's no such button: Play Store → profile picture → **Play Protect** → ⚙ → turn off *Scan apps with Play Protect*, install, then turn it back on.

**Download**
- ``PC-Panel.apk`` - Android phone app (installs over the old version as an update)
- ``PC-Panel-Windows.zip`` - Windows app; extract and run ``PCPanel.exe`` (needs the .NET 8 Desktop Runtime)

See the README for setup.
"@
try {
    $old = Api GET "/repos/$User/$Repo/releases/tags/$Tag"
    Api DELETE "/repos/$User/$Repo/releases/$($old.id)" | Out-Null
    try { Api DELETE "/repos/$User/$Repo/git/refs/tags/$Tag" | Out-Null } catch { }
    Start-Sleep 2
} catch { }
$started = (Get-Date).ToUniversalTime().AddSeconds(-30)
$release = Api POST "/repos/$User/$Repo/releases" @{ tag_name = $Tag; target_commitish = 'main'; name = "PC Panel $Version"; body = $notes }
Say "Surum olusturuldu: $Tag. GitHub simdi APK ve Windows surumunu derliyor (3-6 dk)..." Cyan

$run = $null
for ($i = 0; $i -lt 90; $i++) {
    Start-Sleep 10
    try {
        $runs = Api GET "/repos/$User/$Repo/actions/runs?event=release&per_page=5"
        $run = $runs.workflow_runs | Where-Object { [DateTime]::Parse($_.created_at).ToUniversalTime() -ge $started } | Select-Object -First 1
    } catch { }
    if ($run) {
        Write-Host ("`r  Durum: {0,-12} ({1} sn)   " -f $run.status, (($i + 1) * 10)) -NoNewline
        if ($run.status -eq 'completed') { break }
    } else { Write-Host ("`r  Derleme sirada bekliyor... ({0} sn)   " -f (($i + 1) * 10)) -NoNewline }
}
Write-Host ""
if (-not $run -or $run.status -ne 'completed') { Fail "Derleme zaman asimina ugradi. Bak: https://github.com/$User/$Repo/actions" }
if ($run.conclusion -ne 'success') {
    Start-Process $run.html_url
    Fail "Bulut derlemesi basarisiz oldu ($($run.conclusion)). Ayrinti tarayicida acildi: $($run.html_url)"
}
Say "Derleme basarili!" Green

# grab the new APK for the phone
$rel = Api GET "/repos/$User/$Repo/releases/tags/$Tag"
$apk = $rel.assets | Where-Object { $_.name -eq 'PC-Panel.apk' } | Select-Object -First 1
if ($apk) {
    $dest = Join-Path $env:USERPROFILE 'Downloads\PC-Panel.apk'
    Invoke-WebRequest -UseBasicParsing -Uri $apk.browser_download_url -OutFile $dest
    Say "Yeni APK indirildi: Indirilenler\PC-Panel.apk" Green
}

Write-Host ""
Say "TAMAMLANDI!  https://github.com/$User/$Repo/releases/tag/$Tag" Green
Start-Process "https://github.com/$User/$Repo/releases/tag/$Tag"
Write-Host ""
Read-Host "  Kapatmak icin Enter"
