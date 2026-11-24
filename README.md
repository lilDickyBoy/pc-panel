# PC Panel

Control your Windows PC from your Android phone — live temperatures, music, a Dynamic-Island-style notch, files, clipboard, fans and more. Free and open source.

*Türkçe açıklama aşağıda.*

## Features

**Phone app (Android 8+)**
- **Home:** live CPU / GPU / RAM / VRAM / disk / network, temperatures, fan speeds, uptime, in-game indicator, temperature history graph
- **Music:** now playing with real album art, controls, and song search — Spotify (Premium) by default, YouTube optional
- **Control:** touchpad, keyboard, media keys, shortcuts, brightness, night light, and a live screen view you can tap on
- **Tools:**
  - Apps launcher
  - Stream Deck
  - File transfer (both ways, plus Android's *Share → PC Panel*)
  - Clipboard sync
  - Per-app volume mixer and output device switch
  - Notch timer
  - Fan control
- **Power:** shut down, restart, sleep, lock, screen off, timers, Wake-on-LAN
- **Find my phone**, a desk dashboard mode, and 12 languages

**PC app (Windows 10/11)**
- **Notch:** a notch or Dynamic Island at the top of the screen showing music, phone charging (*"Phone charging · 80%"*), notifications, calls, volume and timers, with iOS-style transitions
- **Sounds:** effects for charging, notifications, connections and more
- **Settings sync:** settings change on either device and stay in sync
- **Auto-lock:** locks the PC when your phone leaves, plus a game mode and temperature alerts
- **Pairing:** QR code pairing; all traffic is end-to-end encrypted (AES-GCM) on your local network

## Install

1. **PC:** download `PC-Panel-Windows.zip` from [Releases](../../releases), extract it, and run `PCPanel.exe`. It needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). Run it as administrator the first time so it can read sensors and set up the firewall rule.
2. **Phone:** install `PC-Panel.apk` from Releases. It also installs over the old *MobiLink* app as an update.
3. Open both, keep them on the same Wi-Fi, and scan the QR code on the PC.

### Spotify (optional)
Spotify's API needs your own free Client ID:
1. Open the **Music** tab in the PC app and click **Open Spotify dashboard**.
2. Create an app there:
   - Any name works.
   - Set the redirect URI to `http://127.0.0.1:47803/callback`.
   - Tick **Web API**.
3. Paste the Client ID, click **Save**, then **Connect**.

### Phone notifications in the notch (optional)
Phone app → Settings → **Notifications on PC**, then allow PC Panel in Android's notification access list. On MIUI/HyperOS, also set battery saver to *No restrictions* for PC Panel.

## Build from source

- **PC:** install the .NET 8 SDK and run `PC\Build-and-Install.bat`. It builds and installs to `%LOCALAPPDATA%\Programs\PC Panel`.
- **Android:** install Android Studio (JDK 17 or 21) and run `Android\Build-APK.bat`, or open `Android/` in Android Studio.
- **Translations:** these live in `i18n/*.json` and are shared by both apps. To add a language, copy `en.json` and add it to the language lists in `PC/PcPanel/Core/Loc.cs` and `Android/.../ui/Str.kt`.

## License

MIT

---

## Türkçe

PC Panel, Windows bilgisayarını Android telefonundan kontrol etmeni sağlar.

**Telefondan:**
- Sıcaklıkları ve kullanımı canlı izle.
- Spotify / YouTube'dan şarkı ara ve çal.
- Dosya ve pano paylaş.
- Fanları, uygulamaları ve sesi yönet.
- Bilgisayarı aç/kapat.

**PC tarafında:**
- Ekranın üstünde bir **notch** veya **Dynamic Island** var: müzik, telefon şarjı, bildirimler ve aramalar burada iOS tarzı animasyonlarla görünür.

**Kurulum:**
1. Releases'tan `PC-Panel-Windows.zip`'i indir ve `PCPanel.exe`'yi çalıştır. Bunun için .NET 8 Desktop Runtime gerekir.
2. `PC-Panel.apk`'yı telefona kur.
3. Aynı Wi-Fi'da QR kodu okut.
