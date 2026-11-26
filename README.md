# PC Panel

Control your Windows PC from your Android phone: live temperatures, music, a Dynamic-Island-style notch, files, clipboard, fans and more. Free and open source.

**[⬇ Download the phone app (APK)](https://github.com/lilDickyBoy/pc-panel/releases/latest/download/PC-Panel.apk)** · **[⬇ Download the Windows app (ZIP)](https://github.com/lilDickyBoy/pc-panel/releases/latest/download/PC-Panel-Windows.zip)** · [All releases](https://github.com/lilDickyBoy/pc-panel/releases)

🇹🇷 *Türkçe kurulum rehberi aşağıda.*

---

## Setup tutorial

### 1. Windows app
1. Install the **[.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)**. On that page, look under *.NET Desktop Runtime* → *Windows x64*. If it's already installed, skip this step.
2. Download **[PC-Panel-Windows.zip](https://github.com/lilDickyBoy/pc-panel/releases/latest/download/PC-Panel-Windows.zip)** and extract it somewhere permanent, for example `C:\Program Files\PC Panel` or `Documents\PC Panel`.
3. Run **PCPanel.exe** and click **Yes** on the administrator prompt. Admin rights let it read CPU temperatures and add the firewall rule. You only need to do this once: after that PC Panel starts with admin rights without asking again.
4. If Windows Firewall asks, allow it on **private networks**.
5. A QR code appears on the **Home** page. PC Panel also starts with Windows; you can turn this off in Settings.

### 2. Phone app
1. On your phone, open **[PC-Panel.apk](https://github.com/lilDickyBoy/pc-panel/releases/latest/download/PC-Panel.apk)** and install it. If your phone asks, allow *Install unknown apps* for your browser.
2. Open PC Panel, tap **Scan QR code**, and scan the code on your PC. Both devices must be on the same Wi-Fi.
3. **If Google Play Protect blocks it** ("App blocked" / "Unsafe app blocked"): this happens because the APK isn't from the Play Store, not because something is wrong with it (the code is all here).
   - Tap **More details → Install anyway**.
   - If there's no such button: open the **Play Store** → your profile picture → **Play Protect** → ⚙ (top right) → turn off **Scan apps with Play Protect**. Install the APK, then turn the scan back on.
4. **Xiaomi / MIUI / HyperOS:**
   - In PC Panel, go to Settings → **Android battery optimization** → Allow.
   - Then in phone Settings → Apps → PC Panel → Battery saver, choose **No restrictions**.

### 3. Optional extras
- **Phone notifications and calls in the PC notch:** in the phone app, go to Settings → **Notifications on PC** and allow PC Panel in the list that opens.
- **Spotify search and play (Premium):**
  1. In the PC app, open the **Music** tab and click **Open Spotify dashboard**.
  2. Click **Create app**:
     - Name: `PC Panel`
     - Redirect URI: `http://127.0.0.1:47803/callback` (then click **Add**)
     - Tick **Web API**
     - Accept the terms and click **Save**.
  3. Open **Settings** and copy the **Client ID** into the PC app.
  4. Click **Save**, then **Connect**, and accept in the browser.
- **YouTube search inside the app:**
  1. In the [Google Cloud console](https://console.cloud.google.com/apis/library/youtube.googleapis.com), enable *YouTube Data API v3*.
  2. Go to **Credentials** → **Create credentials** → **API key** (choose *Public data*).
  3. Paste the key into the PC app's **Music** tab.
- **Turn the PC on from the phone (Wake-on-LAN):** follow the steps in the phone app's Settings → **Set up Wake-on-LAN**. This requires a wired connection.

### 4. Phone screen (second monitor / control the phone)
1. In the PC app open **Phone screen**. Under *Setup*, click **Download** / **Install** for the three helpers (once): the video engine ([FFmpeg](https://ffmpeg.org)), the phone engine ([scrcpy](https://github.com/Genymobile/scrcpy), includes adb) and the [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver).
2. For the smoothest picture and for controlling the phone, turn on **USB debugging** on the phone (steps are shown on that page) and plug in the USB cable. On Xiaomi also turn on **USB debugging (Security settings)**.
3. **Second screen:** click **Start second screen** on the PC, or on the phone go to Tools → **Second screen** → Start. Swipe back on the phone to end it; the extra monitor disappears again.
4. **Control the phone:** click **Open phone**. After the first USB connection you can click **Go wireless** and unplug the cable.

### Updating
Download the new APK and ZIP from [Releases](https://github.com/lilDickyBoy/pc-panel/releases):
- The APK installs over the old one and keeps your pairing.
- For the PC, quit PC Panel from the tray icon, replace the files, and start it again.

---

## Features

**Phone app (Android 8+)**
- **Home:** live CPU / GPU / RAM / VRAM / disk / network stats, temperatures, fan speeds, uptime, an in-game indicator, and a temperature history graph.
- **Music:** now playing with real album art, playback controls, and song search. Spotify (Premium) is the default; YouTube is optional.
- **Control:** touchpad, keyboard, media keys, shortcuts, brightness, night light, and a live screen view you can tap on.
- **Tools:**
  - Apps launcher and Stream Deck.
  - File transfer in both directions, plus Android's *Share → PC Panel*.
  - Clipboard sync.
  - Per-app volume mixer and output device switching.
  - Notch timer and fan control.
- **Power:** shut down, restart, sleep, lock, screen off, timers, and Wake-on-LAN.
- **Second screen:** use the phone as an extra monitor for the PC at 60 FPS, with touch (Tools → Second screen).
- **Also:** Find my phone, a desk dashboard mode, and 12 languages.

**PC app (Windows 10/11)**
- **Notch:** a notch or Dynamic Island at the top of the screen. It shows music (spinning album cover + synced lyrics), phone charging (*"Phone charging · 80%"*), notifications, calls, volume, timers and Bluetooth devices, with iOS-style transitions and an optional glowing edge light.
- **Drop to phone:** drag a file to the top of the screen and it goes to your phone; images land in the gallery. Screenshots can go there automatically too.
- **Security log:** see when the PC was turned on, unlocked or a wrong password was typed, with alerts on your phone. Download-finished alerts for browsers, Steam and Epic.
- **Sound effects:** for charging, notifications, connections and more.
- **Settings sync:** settings change on either device and stay in sync.
- **Phone screen:** the phone as a 60 FPS second monitor (virtual display + GPU capture + NVENC/AMF hardware encoding, USB or Wi-Fi, real touch input), and your phone in a window on the PC that you control with the mouse and keyboard.
- **Auto-lock:** locks the PC when your phone leaves.
- **Game mode and temperature alerts.**
- **Pairing:** by QR code; all traffic is end-to-end encrypted (AES-GCM) and stays on your local network.

## Build from source
- **PC:** install the .NET 8 SDK and run `PC\Build-and-Install.bat`.
- **Android:** install Android Studio (JDK 17/21) and run `Android\Build-APK.bat`.
- **Translations:** these live in `i18n/*.json` and are shared by both apps.
- **Releases:** publishing a GitHub release runs `.github/workflows/build.yml` (kept as `tools/ci/build.yml` in the source and placed there by `tools/publish-github.ps1`), which builds the APK and the Windows zip in the cloud and attaches them to the release. The APK is signed with the key in the `DEBUG_KEYSTORE_B64` repository secret, so updates install over the old version.

## License
MIT

---

## 🇹🇷 Türkçe kurulum rehberi

**İndir:** [Telefon uygulaması (APK)](https://github.com/lilDickyBoy/pc-panel/releases/latest/download/PC-Panel.apk) · [Windows uygulaması (ZIP)](https://github.com/lilDickyBoy/pc-panel/releases/latest/download/PC-Panel-Windows.zip)

### 1. Bilgisayar
1. **[.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)**'ı kur. Sayfada *.NET Desktop Runtime* → *Windows x64* kısmında. Zaten kuruluysa bu adımı geç.
2. **PC-Panel-Windows.zip**'i indir ve kalıcı bir klasöre çıkart, örneğin `Belgeler\PC Panel`.
3. **PCPanel.exe**'yi aç ve yönetici izninde **Evet** de. İzin, sıcaklıkları okuyabilmesi için gerekli. Bunu sadece bir kere yaparsın; sonra bir daha sormaz.
4. Güvenlik duvarı sorarsa **özel ağlar** için izin ver.
5. Ana sayfada bir QR kod çıkar. Uygulama Windows ile birlikte otomatik açılır; istersen Ayarlar'dan kapatabilirsin.

### 2. Telefon
1. **PC-Panel.apk**'yı telefonda aç ve kur. *Bilinmeyen uygulamaları yükle* izni isterse tarayıcına izin ver.
2. PC Panel'i aç, **QR kodu tara**'ya bas ve bilgisayardaki kodu okut. İkisi de aynı Wi-Fi'da olmalı.
3. **Play Protect engellerse** ("Uygulama engellendi"): APK Play Store'dan gelmediği için oluyor, uygulamada sorun yok (kodun hepsi burada).
   - **Diğer ayrıntılar → Yine de yükle**'ye bas.
   - Böyle bir buton yoksa: **Play Store** → profil resmin → **Play Protect** → ⚙ (sağ üst) → **Uygulamaları Play Protect ile tara**'yı kapat. APK'yı kur, sonra taramayı geri aç.
4. **Xiaomi / MIUI / HyperOS:**
   - Uygulamada Ayarlar → **Android pil optimizasyonu** → İzin ver.
   - Telefon Ayarları → Uygulamalar → PC Panel → Pil tasarrufu → **Kısıtlama yok** yap.

### 3. İsteğe bağlı
- **Bildirimler ve aramalar notch'ta görünsün:** telefonda Ayarlar → **PC'de bildirimler**'i aç ve listede PC Panel'e izin ver.
- **Spotify (Premium):**
  1. PC'de **Müzik** sekmesinde **Spotify panelini aç**'a bas.
  2. **Create app**'e bas:
     - Ad: `PC Panel`
     - Redirect URI: `http://127.0.0.1:47803/callback` (sonra **Add**'e bas)
     - **Web API**'yi işaretle
     - Şartları kabul et ve **Save**'e bas.
  3. **Settings**'ten **Client ID**'yi kopyala ve PC uygulamasına yapıştır.
  4. **Kaydet**'e, sonra **Bağla**'ya bas.
- **YouTube araması:**
  1. Google Cloud'da *YouTube Data API v3*'ü etkinleştir.
  2. **Credentials** → **Create credentials** → *Public data* ile bir API anahtarı al.
  3. Anahtarı **Müzik** sekmesine yapıştır.

### 4. Telefon ekranı (ikinci monitör / telefonu PC'den kontrol)
1. PC uygulamasında **Telefon ekranı**'nı aç. *Kurulum* kısmında üç yardımcı için bir kere **İndir** / **Kur**'a bas: görüntü motoru (FFmpeg), telefon motoru (scrcpy, adb dahil) ve Virtual Display Driver.
2. En akıcı görüntü ve telefonu kontrol etmek için telefonda **USB hata ayıklama**'yı aç (adımlar o sayfada yazıyor) ve USB kablosunu tak. Xiaomi'de **USB hata ayıklama (Güvenlik ayarları)**'nı da aç.
3. **İkinci ekran:** PC'de **İkinci ekranı başlat**'a bas ya da telefonda Araçlar → **İkinci ekran** → Başlat. Bitirmek için telefonda geri hareketi yap; ekstra monitör kaybolur.
4. **Telefonu kontrol et:** **Telefonu aç**'a bas. İlk USB bağlantısından sonra **Kablosuz yap** deyip kabloyu çıkarabilirsin.

### Güncelleme
Releases'tan yeni dosyaları indir:
- Yeni APK eskisinin üstüne kurulur, eşleşmen silinmez.
- PC için: tepsi ikonundan **Çıkış** yap, dosyaları değiştir ve tekrar aç.
