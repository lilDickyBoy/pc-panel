package com.pcpanel.app.ui.screens

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.PowerManager
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.AddPhotoAlternate
import androidx.compose.material.icons.rounded.AutoAwesome
import androidx.compose.material.icons.rounded.Lyrics
import androidx.compose.material.icons.rounded.Screenshot
import androidx.compose.material.icons.rounded.Shield
import androidx.compose.material.icons.rounded.Download
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.BatteryChargingFull
import androidx.compose.material.icons.rounded.BatterySaver
import androidx.compose.material.icons.rounded.Call
import androidx.compose.material.icons.rounded.CameraAlt
import androidx.compose.material.icons.rounded.Computer
import androidx.compose.material.icons.rounded.ContentPaste
import androidx.compose.material.icons.rounded.Dashboard
import androidx.compose.material.icons.rounded.Edit
import androidx.compose.material.icons.rounded.ExpandMore
import androidx.compose.material.icons.rounded.Fullscreen
import androidx.compose.material.icons.rounded.Lock
import androidx.compose.material.icons.rounded.MusicNote
import androidx.compose.material.icons.rounded.Notifications
import androidx.compose.material.icons.rounded.NotificationsActive
import androidx.compose.material.icons.rounded.PowerSettingsNew
import androidx.compose.material.icons.rounded.Speed
import androidx.compose.material.icons.rounded.SportsEsports
import androidx.compose.material.icons.rounded.Thermostat
import androidx.compose.material.icons.rounded.Timer
import androidx.compose.material.icons.rounded.Toys
import androidx.compose.material.icons.rounded.TouchApp
import androidx.compose.material.icons.rounded.Vibration
import androidx.compose.material.icons.rounded.VolumeUp
import androidx.compose.material.icons.rounded.WifiOff
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import com.pcpanel.app.PcPanelApp
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.BannerKind
import com.pcpanel.app.net.ConnState
import com.pcpanel.app.net.Connection
import com.pcpanel.app.service.NotifListener
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.Chip
import com.pcpanel.app.ui.components.BrandChip
import com.pcpanel.app.ui.components.BrandRes
import com.pcpanel.app.ui.components.BrandRow
import com.pcpanel.app.ui.components.BrandSwitchRow
import com.pcpanel.app.net.PcSettings
import androidx.compose.ui.graphics.Brush
import androidx.compose.foundation.border
import com.pcpanel.app.ui.components.DialogButton
import com.pcpanel.app.ui.components.Divider
import com.pcpanel.app.ui.components.FatSlider
import com.pcpanel.app.ui.components.GroupCard
import com.pcpanel.app.ui.components.Hint
import com.pcpanel.app.ui.components.IconRow
import com.pcpanel.app.ui.components.IconSwitchRow
import com.pcpanel.app.ui.components.IconTile
import com.pcpanel.app.ui.components.NavRow
import com.pcpanel.app.ui.components.Pill
import com.pcpanel.app.ui.components.SectionTitle
import com.pcpanel.app.ui.components.Segmented
import com.pcpanel.app.ui.components.bounceClick
import com.pcpanel.app.ui.theme.C

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun SettingsScreen() {
    val ctx = LocalContext.current
    val pc = Prefs.pc
    val ps = Connection.pcSettings
    val connected = Connection.state == ConnState.Connected
    val notifPerm = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { }

    // re-check permissions when coming back from Android settings
    var resumeTick by remember { mutableIntStateOf(0) }
    val owner = LocalLifecycleOwner.current
    DisposableEffect(owner) {
        val obs = LifecycleEventObserver { _, e -> if (e == Lifecycle.Event.ON_RESUME) resumeTick++ }
        owner.lifecycle.addObserver(obs)
        onDispose { owner.lifecycle.removeObserver(obs) }
    }
    @Suppress("UNUSED_VARIABLE") val tick = resumeTick
    val hasNotifAccess = NotifListener.hasAccess(ctx)
    val pm = ctx.getSystemService(PowerManager::class.java)
    val ignoringBatteryOpt = pm?.isIgnoringBatteryOptimizations(ctx.packageName) == true

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).statusBarsPadding().padding(horizontal = 16.dp)) {
        LargeHeader(Str.t("tab.settings"))

        // ------------------------------------------------ PC
        SectionTitle(Str.t("set.pc"))
        GroupCard {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(52.dp).clip(RoundedCornerShape(14.dp)).background(C.Blue), contentAlignment = Alignment.Center) {
                    Icon(Icons.Rounded.Computer, null, tint = Color.White, modifier = Modifier.size(30.dp))
                }
                Column(Modifier.weight(1f).padding(start = 14.dp)) {
                    Text(pc?.name ?: "PC", color = C.Text, fontSize = 18.sp, fontWeight = FontWeight.Bold)
                    Text(listOfNotNull(pc?.host, Connection.pcVersion.takeIf { it.isNotBlank() }?.let { "PC Panel $it" }).joinToString("  ·  "), color = C.Text2, fontSize = 12.5.sp)
                }
                Pill(if (connected) Str.t("conn.connected") else Str.t("conn.idle"), if (connected) C.Green else C.Text2)
            }
            Spacer(Modifier.height(14.dp))
            Row {
                Box(
                    Modifier.weight(1f).height(46.dp).bounceClick { if (connected) Connection.disconnect() else Connection.connect(manual = true) }
                        .clip(RoundedCornerShape(13.dp)).background(if (connected) C.Red.copy(alpha = 0.15f) else C.Blue.copy(alpha = 0.18f)),
                    contentAlignment = Alignment.Center
                ) { Text(if (connected) Str.t("conn.disconnect") else Str.t("conn.connect"), color = if (connected) C.Red else C.Blue, fontSize = 15.sp, fontWeight = FontWeight.SemiBold) }
                Spacer(Modifier.width(10.dp))
                Box(
                    Modifier.weight(1f).height(46.dp).bounceClick { UiState.forgetConfirm = true }.clip(RoundedCornerShape(13.dp)).background(C.Card2),
                    contentAlignment = Alignment.Center
                ) { Text(Str.t("set.forget"), color = C.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold) }
            }
        }

        // ------------------------------------------------ language
        SectionTitle(Str.t("set.lang"))
        GroupCard {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Str.languages.forEach { (code, name) ->
                    Chip(name, Prefs.lang == code) {
                        if (Prefs.lang != code) {
                            Prefs.updateLang(code, fromUser = true, connected = connected)
                            Str.load(code)
                            if (connected) Connection.setShared("lang", code)
                        }
                    }
                }
            }
            Hint(Str.t("set.langSync"), Modifier.padding(top = 6.dp))
        }

        if (!connected) {
            Row(
                Modifier.fillMaxWidth().padding(top = 18.dp).clip(RoundedCornerShape(16.dp)).background(C.Orange.copy(alpha = 0.12f)).padding(14.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(Icons.Rounded.WifiOff, null, tint = C.Orange, modifier = Modifier.size(20.dp))
                Text(Str.t("set.offlineHint"), color = C.Orange, fontSize = 13.5.sp, lineHeight = 18.sp, modifier = Modifier.padding(start = 10.dp))
            }
        }

        // ------------------------------------------------ PC notch
        SectionTitle(Str.t("set.notch"))
        GroupCard {
            IconSwitchRow(Icons.Rounded.Fullscreen, C.Blue, Str.t("set.notchOn"), ps.notchOn) { Connection.setShared("notchOn", it) }
            Divider()
            IconRow(Icons.Rounded.Edit, C.Indigo, Str.t("set.notchStyle"), below = true) {
                Segmented(listOf(Str.t("set.styleNotch"), Str.t("set.styleIsland")), if (ps.notchStyle == "island") 1 else 0, Modifier.fillMaxWidth()) {
                    Connection.setShared("notchStyle", if (it == 1) "island" else "notch")
                }
            }
            Divider()
            IconRow(Icons.Rounded.Timer, C.Purple, Str.t("set.notchMode"), if (ps.notchMode == "always") Str.t("set.alwaysSub") else Str.t("set.autoSub"), below = true) {
                Segmented(listOf(Str.t("set.auto"), Str.t("set.always")), if (ps.notchMode == "always") 1 else 0, Modifier.fillMaxWidth()) {
                    Connection.setShared("notchMode", if (it == 1) "always" else "auto")
                }
            }
            Divider()
            IconSwitchRow(Icons.Rounded.SportsEsports, C.Purple, Str.t("set.notchFs"), ps.notchFs) { Connection.setShared("notchFs", it) }
            Divider()
            IconSwitchRow(Icons.Rounded.VolumeUp, C.Orange, Str.t("set.notchVol"), ps.notchVol) { Connection.setShared("notchVol", it) }
            Divider()
            IconSwitchRow(Icons.Rounded.Computer, C.Blue, Str.t("set.notchConn"), ps.notchConn) { Connection.setShared("notchConn", it) }
            Divider()
            IconSwitchRow(Icons.Rounded.BatteryChargingFull, C.Green, Str.t("set.notchCharge"), ps.notchCharge, Str.t("set.notchChargeSub")) { Connection.setShared("notchCharge", it) }
            Divider()
            IconSwitchRow(Icons.Rounded.AddPhotoAlternate, C.Indigo, Str.t("set.notchDrop"), ps.notchDrop, Str.t("set.notchDropSub")) { Connection.setShared("notchDrop", it) }
            Divider()
            IconSwitchRow(Icons.Rounded.Lyrics, C.Pink, Str.t("set.lyrics"), ps.lyrics, Str.t("set.lyricsSub")) { Connection.setShared("lyrics", it) }
            Divider()
            BrandSwitchRow(BrandRes.bluetooth, BrandRes.Dark, Str.t("set.notchBt"), ps.notchBt, Str.t("set.notchBtSub")) { Connection.setShared("notchBt", it) }
        }

        // ------------------------------------------------ notch edge light
        SectionTitle(Str.t("set.glow"))
        GroupCard { GlowSettings(ps) }

        // ------------------------------------------------ phone → PC
        SectionTitle(Str.t("set.phoneToPc"))
        GroupCard {
            IconSwitchRow(Icons.Rounded.Notifications, C.Red, Str.t("set.mirrorNotifs"), Prefs.mirrorNotifs && hasNotifAccess && ps.notchNotif, Str.t("set.mirrorNotifsSub")) { on ->
                Prefs.updateMirrorNotifs(on)
                if (on) {
                    Connection.setShared("notchNotif", true)
                    if (!NotifListener.hasAccess(ctx)) {
                        try { ctx.startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) } catch (_: Exception) { }
                        Connection.showBanner(Str.t("set.grantNotif"), BannerKind.Info)
                    }
                }
            }
            Divider()
            IconSwitchRow(Icons.Rounded.Call, C.Green, Str.t("set.mirrorCalls"), Prefs.mirrorCalls && hasNotifAccess && ps.notchCall, Str.t("set.mirrorCallsSub")) { on ->
                Prefs.updateMirrorCalls(on)
                if (on) {
                    Connection.setShared("notchCall", true)
                    if (!NotifListener.hasAccess(ctx)) {
                        try { ctx.startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) } catch (_: Exception) { }
                    }
                }
            }
            Divider()
            IconSwitchRow(Icons.Rounded.ContentPaste, C.Teal, Str.t("set.clip"), ps.clipSync, Str.t("set.clipSub")) { Connection.setShared("clipSync", it) }
            Divider()
            IconSwitchRow(Icons.Rounded.Lock, C.Indigo, Str.t("set.autoLock"), ps.autoLock, Str.t("set.autoLockSub")) { Connection.setShared("autoLock", it) }
        }

        // ------------------------------------------------ PC → phone
        SectionTitle(Str.t("set.pcToPhone"))
        GroupCard {
            IconSwitchRow(Icons.Rounded.Screenshot, C.Pink, Str.t("set.shotToPhone"), ps.shotToPhone, Str.t("set.shotToPhoneSub")) { Connection.setShared("shotToPhone", it) }
            Divider()
            IconSwitchRow(Icons.Rounded.Shield, C.Indigo, Str.t("set.secAlerts"), ps.secAlerts, Str.t("set.secAlertsSub")) { Connection.setShared("secAlerts", it) }
            Divider()
            IconSwitchRow(Icons.Rounded.Download, C.Green, Str.t("set.dlAlerts"), ps.dlAlerts, Str.t("set.dlAlertsSub")) { Connection.setShared("dlAlerts", it) }
        }

        // ------------------------------------------------ sounds & screen
        SectionTitle(Str.t("set.soundsScreen"))
        GroupCard {
            IconSwitchRow(Icons.Rounded.NotificationsActive, C.Pink, Str.t("set.sounds"), ps.sounds, Str.t("set.soundsSub")) { Connection.setShared("sounds", it) }
            if (ps.sounds) {
                Spacer(Modifier.height(4.dp))
                FatSlider(
                    value = ps.soundVol / 100f, onValueChange = { }, height = 44.dp, icon = Icons.Rounded.VolumeUp,
                    trailingText = "${ps.soundVol}%",
                    onCommit = { Connection.setShared("soundVol", (it * 100).toInt()) }
                )
                Spacer(Modifier.height(8.dp))
            }
            Divider()
            IconSwitchRow(Icons.Rounded.CameraAlt, C.Blue, Str.t("set.shotCursor"), ps.shotCursor, Str.t("set.shotCursorSub")) { Connection.setShared("shotCursor", it) }
        }

        // ------------------------------------------------ music
        SectionTitle(Str.t("set.music"))
        GroupCard {
            IconRow(Icons.Rounded.MusicNote, C.Pink, Str.t("set.musicSrc"), below = true) {
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    BrandChip(BrandRes.spotify, "Spotify", ps.musicSrc != "youtube", BrandRes.SpotifyGreen, Modifier.weight(1f)) { Connection.setShared("musicSrc", "spotify") }
                    BrandChip(BrandRes.youtube, "YouTube", ps.musicSrc == "youtube", Color(0xFFFF0033), Modifier.weight(1f)) { Connection.setShared("musicSrc", "youtube") }
                }
            }
            Divider()
            BrandRow(BrandRes.spotify, BrandRes.Dark, if (ps.spotify) Str.f("set.spotifyAs", ps.spotifyUser.ifBlank { "Spotify" }) else Str.t("set.spotifyOff"),
                if (ps.spotify) null else if (ps.spotifyClient) Str.t("set.spotifyConnectSub") else Str.t("music.needSetup")) {
                Text(
                    if (ps.spotify) Str.t("conn.disconnect") else Str.t("conn.connect"),
                    color = if (ps.spotify) C.Red else Color(0xFF1DB954), fontSize = 15.sp, fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.bounceClick { if (ps.spotify) Connection.spotifyLogout() else { Connection.spotifyLogin(); Connection.showBanner(Str.t("music.loginOnPc")) } }.padding(6.dp)
                )
            }
            Divider()
            BrandSwitchRow(BrandRes.spotifyWhite, BrandRes.SpotifyGreen, Str.t("set.spBg"), ps.spBg, Str.t("set.spBgSub")) { Connection.setShared("spBg", it) }
            Divider()
            BrandSwitchRow(BrandRes.youtube, BrandRes.Dark, Str.t("set.ytBg"), ps.ytBg, Str.t("set.ytBgSub")) { Connection.setShared("ytBg", it) }
        }

        // ------------------------------------------------ battery & connection
        SectionTitle(Str.t("set.battery"))
        GroupCard {
            IconSwitchRow(Icons.Rounded.BatterySaver, C.Green, Str.t("set.powerSave"), Prefs.powerSave, Str.t("set.powerSaveSub")) { on ->
                Prefs.updatePowerSave(on)
                if (!on && Prefs.pc != null) PcPanelApp.startAlertService(ctx)
                else if (on && !Prefs.bgAlerts) PcPanelApp.stopAlertService(ctx)
            }
            androidx.compose.animation.AnimatedVisibility(Prefs.powerSave, enter = expandVertically() + fadeIn(), exit = shrinkVertically() + fadeOut()) {
                Column {
                    Text(Str.t("set.disconnectAfter"), color = C.Text, fontSize = 15.sp, modifier = Modifier.padding(top = 6.dp, bottom = 8.dp))
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        listOf(0 to Str.t("set.now"), 6 to "6 s", 30 to "30 s", 60 to "1 min", 300 to "5 min").forEach { (v, label) ->
                            Chip(label, Prefs.disconnectDelay == v) { Prefs.updateDisconnectDelay(v) }
                        }
                    }
                    Spacer(Modifier.height(10.dp))
                }
            }
            Divider()
            IconRow(Icons.Rounded.Speed, C.Orange, Str.t("set.refresh"), Str.t("set.refreshSub"), below = true) {
                val opts = listOf(1000, 2000, 5000)
                Segmented(listOf(Str.t("set.refreshFast"), Str.t("set.refreshNormal"), Str.t("set.refreshSaver")), opts.indexOf(Prefs.refreshMs).coerceAtLeast(0), Modifier.fillMaxWidth()) {
                    Prefs.updateRefreshMs(opts[it]); Connection.sendRate()
                }
            }
            Divider()
            IconSwitchRow(Icons.Rounded.Thermostat, C.Red, Str.t("set.bgAlerts"), Prefs.bgAlerts, Str.t("set.bgAlertsSub")) { on ->
                Prefs.updateBgAlerts(on)
                if (on) {
                    if (Build.VERSION.SDK_INT >= 33 &&
                        ContextCompat.checkSelfPermission(ctx, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
                    ) notifPerm.launch(Manifest.permission.POST_NOTIFICATIONS)
                    PcPanelApp.startAlertService(ctx)
                } else if (Prefs.powerSave) PcPanelApp.stopAlertService(ctx)
            }
            Divider()
            IconRow(Icons.Rounded.BatteryChargingFull, if (ignoringBatteryOpt) C.Green else C.Orange, Str.t("set.androidBattery"),
                if (ignoringBatteryOpt) Str.t("set.androidBatteryOk") else Str.t("set.androidBatterySub")) {
                if (!ignoringBatteryOpt) Text(Str.t("set.fix"), color = C.Blue, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.bounceClick {
                    try {
                        @Suppress("BatteryLife")
                        ctx.startActivity(Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:" + ctx.packageName)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                    } catch (_: Exception) {
                        try { ctx.startActivity(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) } catch (_: Exception) { }
                    }
                }.padding(6.dp))
            }
        }

        // ------------------------------------------------ PC system
        SectionTitle(Str.t("set.pcSystem"))
        GroupCard {
            var name by remember(ps.pcName) { mutableStateOf(ps.pcName.ifBlank { pc?.name ?: "" }) }
            IconRow(Icons.Rounded.Edit, C.Blue, Str.t("set.pcName"), below = true) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.weight(1f)) { Field("", name) { name = it.take(40) } }
                    Spacer(Modifier.width(10.dp))
                    Text(Str.t("set.save"), color = C.Blue, fontSize = 16.sp, fontWeight = FontWeight.SemiBold,
                        modifier = Modifier.padding(top = 18.dp).bounceClick { if (name.isNotBlank()) { Connection.setShared("pcName", name.trim()); Connection.showBanner(Str.t("set.saved"), BannerKind.Success) } }.padding(6.dp))
                }
            }
            Divider()
            IconSwitchRow(Icons.Rounded.PowerSettingsNew, C.Green, Str.t("set.autostart"), ps.autostart) { Connection.setShared("autostart", it) }
            Divider()
            IconSwitchRow(Icons.Rounded.SportsEsports, C.Purple, Str.t("set.gameMode"), ps.gameMode, Str.t("set.gameModeSub")) { Connection.setShared("gameMode", it) }
            Divider()
            IconSwitchRow(Icons.Rounded.Thermostat, C.Red, Str.t("set.tempHistory"), ps.tempHistory) { Connection.setShared("tempHistory", it) }
            Divider()
            IconSwitchRow(Icons.Rounded.Toys, C.Teal, Str.t("fans.allow"), ps.fanCtl) { Connection.setShared("fanCtl", it) }
        }

        // ------------------------------------------------ temperature alerts
        SectionTitle(Str.t("set.alerts"))
        GroupCard {
            Stepper(Str.t("set.cpuAlert"), ps.cpuAlert.takeIf { ps.known } ?: Prefs.cpuAlert) { Prefs.updateAlerts(it, Prefs.gpuAlert); Connection.setShared("cpuAlert", it) }
            Divider()
            Stepper(Str.t("set.gpuAlert"), ps.gpuAlert.takeIf { ps.known } ?: Prefs.gpuAlert) { Prefs.updateAlerts(Prefs.cpuAlert, it); Connection.setShared("gpuAlert", it) }
        }

        // ------------------------------------------------ touch & feel
        SectionTitle(Str.t("set.general"))
        GroupCard {
            IconSwitchRow(Icons.Rounded.Vibration, C.Pink, Str.t("set.haptics"), Prefs.haptics) { Prefs.updateHaptics(it) }
            Divider()
            IconRow(Icons.Rounded.TouchApp, C.Blue, Str.t("set.touch"), below = true) {
                FatSlider(
                    value = ((Prefs.touchSpeed - 0.4f) / 2.1f).coerceIn(0f, 1f), onValueChange = { }, height = 44.dp,
                    trailingText = String.format("%.1f×", Prefs.touchSpeed),
                    onCommit = { Prefs.updateTouchSpeed(0.4f + it * 2.1f) }
                )
            }
        }

        // ------------------------------------------------ dashboard
        SectionTitle(Str.t("set.dashboard"))
        GroupCard {
            IconSwitchRow(Icons.Rounded.BatteryChargingFull, C.Green, Str.t("set.autoDash"), Prefs.autoDashboard) { Prefs.updateAutoDashboard(it) }
            Divider()
            IconSwitchRow(Icons.Rounded.Speed, C.Indigo, Str.t("set.dimDash"), Prefs.dimDashboard) { Prefs.updateDimDashboard(it) }
            Divider()
            NavRow(Icons.Rounded.Dashboard, C.Indigo, Str.t("set.openDash")) { UiState.dashboard = true }
        }

        // ------------------------------------------------ wake on lan
        SectionTitle(Str.t("set.wol"))
        var open by remember { mutableStateOf(false) }
        val arrow by animateFloatAsState(if (open) 180f else 0f, label = "arrow")
        GroupCard {
            Row(Modifier.fillMaxWidth().bounceClick(pressedScale = 0.98f) { open = !open }.padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                IconTile(Icons.Rounded.PowerSettingsNew, C.Green)
                Text(Str.t("set.wol"), color = C.Text, fontSize = 16.sp, modifier = Modifier.weight(1f).padding(start = 12.dp))
                Icon(Icons.Rounded.ExpandMore, null, tint = C.Text2, modifier = Modifier.rotate(arrow))
            }
            androidx.compose.animation.AnimatedVisibility(open, enter = expandVertically() + fadeIn(), exit = shrinkVertically() + fadeOut()) {
                Text(Str.t("set.wolText"), color = C.Text2, fontSize = 13.5.sp, lineHeight = 20.sp, modifier = Modifier.padding(top = 10.dp))
            }
        }

        // ------------------------------------------------ about
        SectionTitle(Str.t("set.about"))
        GroupCard {
            Row(verticalAlignment = Alignment.CenterVertically) {
                AppLogo(56.dp)
                Column(Modifier.padding(start = 14.dp).weight(1f)) {
                    Text("PC Panel", color = C.Text, fontSize = 18.sp, fontWeight = FontWeight.Bold)
                    Text("${Str.t("set.version")} ${appVersion(ctx)}", color = C.Text2, fontSize = 13.sp)
                    Text(Str.t("set.openSource"), color = C.Text2, fontSize = 13.sp)
                }
            }
            Spacer(Modifier.height(12.dp))
            DialogButton("GitHub", color = C.Blue) {
                try { ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(GITHUB_URL)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) } catch (_: Exception) { }
            }
        }
        Text("Made with ♥", color = C.Text3, fontSize = 12.sp, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth().padding(top = 18.dp, bottom = 30.dp))
    }
}

/** filled in by the GitHub publish script */
const val GITHUB_URL = "https://github.com/lilDickyBoy/pc-panel"

private fun appVersion(ctx: android.content.Context): String = try {
    ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName ?: ""
} catch (_: Exception) { "" }

@Composable
private fun Stepper(title: String, value: Int, onChange: (Int) -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(title, color = C.Text, fontSize = 15.sp, modifier = Modifier.weight(1f))
        Box(Modifier.size(34.dp).bounceClick { onChange((value - 5).coerceAtLeast(60)) }.clip(CircleShape).background(C.Card2), contentAlignment = Alignment.Center) {
            Text("−", color = C.Text, fontSize = 20.sp)
        }
        Text("$value°C", color = C.temp(value.toFloat()), fontSize = 16.sp, fontWeight = FontWeight.Bold, modifier = Modifier.width(64.dp).padding(horizontal = 6.dp), textAlign = TextAlign.Center)
        Box(Modifier.size(34.dp).bounceClick { onChange((value + 5).coerceAtMost(105)) }.clip(CircleShape).background(C.Card2), contentAlignment = Alignment.Center) {
            Text("+", color = C.Text, fontSize = 20.sp)
        }
    }
}


// ====================================================================== notch edge light
private val GlowPalette = listOf(
    "#0A84FF", "#64D2FF", "#FFFFFF", "#5E5CE6", "#BF5AF2", "#FF375F",
    "#FF453A", "#FF9F0A", "#FFD60A", "#30D158", "#40C8E0", "#8E8E93"
)

private fun hexColor(h: String): Color = try { Color(android.graphics.Color.parseColor(h)) } catch (_: Exception) { Color.White }

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun GlowSettings(ps: PcSettings) {
    val modes = listOf("off" to Str.t("set.glowOff"), "solid" to Str.t("set.glowSolid"), "flow" to Str.t("set.glowFlow"), "rgb" to "RGB", "album" to Str.t("set.glowAlbum"))
    IconRow(Icons.Rounded.AutoAwesome, C.Blue, Str.t("set.glow"), Str.t("set.glowSub"), below = true) {
        Column {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                modes.forEach { (id, label) -> Chip(label, ps.notchGlow == id) { Connection.setShared("notchGlow", id) } }
            }
            val cols = ps.glowColors.split(",").filter { it.length == 7 }.ifEmpty { listOf("#0A84FF", "#FFFFFF") }
            if (ps.notchGlow == "solid" || ps.notchGlow == "flow") {
                val current = if (ps.notchGlow == "solid") cols.take(1) else cols
                Text(Str.t("set.glowColors"), color = C.Text, fontSize = 15.sp, modifier = Modifier.padding(top = 14.dp, bottom = 8.dp))
                // current colours as a preview bar + swatches (tap a swatch to remove it)
                Box(
                    Modifier.fillMaxWidth().height(8.dp).clip(RoundedCornerShape(4.dp))
                        .background(if (current.size > 1) Brush.horizontalGradient(current.map(::hexColor)) else Brush.horizontalGradient(listOf(hexColor(current[0]), hexColor(current[0]))))
                )
                Row(Modifier.padding(top = 10.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                    current.forEachIndexed { i, h ->
                        Box(
                            Modifier.size(34.dp).clip(CircleShape).background(hexColor(h)).border(2.dp, Color.White.copy(alpha = 0.35f), CircleShape)
                                .bounceClick { if (ps.notchGlow == "flow" && current.size > 2) Connection.setShared("glowColors", current.filterIndexed { j, _ -> j != i }.joinToString(",")) },
                            contentAlignment = Alignment.Center
                        ) {
                            if (ps.notchGlow == "flow" && current.size > 2) Icon(Icons.Rounded.Close, null, tint = Color.Black.copy(alpha = 0.55f), modifier = Modifier.size(14.dp))
                        }
                    }
                }
                Hint(if (ps.notchGlow == "flow") Str.t("set.glowPickFlow") else Str.t("set.glowPickSolid"), Modifier.padding(top = 4.dp))
                FlowRow(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.padding(top = 2.dp)) {
                    GlowPalette.forEach { h ->
                        Box(
                            Modifier.size(32.dp).clip(CircleShape).background(hexColor(h))
                                .border(1.dp, Color.White.copy(alpha = 0.15f), CircleShape)
                                .bounceClick(pressedScale = 0.85f) {
                                    val next = if (ps.notchGlow == "solid") listOf(h)
                                               else if (current.size >= 4) current.drop(1) + h else current + h
                                    Connection.setShared("glowColors", next.joinToString(","))
                                }
                        )
                    }
                }
            }
            if (ps.notchGlow != "off") {
                if (ps.notchGlow != "solid") {
                    Text(Str.t("set.glowSpeed"), color = C.Text, fontSize = 15.sp, modifier = Modifier.padding(top = 14.dp, bottom = 8.dp))
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        listOf(10 to Str.t("set.glowSlow"), 6 to Str.t("set.glowNormal"), 3 to Str.t("set.glowFast")).forEach { (v, l) ->
                            val sel = when { ps.glowSpeed >= 9 -> v == 10; ps.glowSpeed <= 4 -> v == 3; else -> v == 6 }
                            Chip(l, sel) { Connection.setShared("glowSpeed", v) }
                        }
                    }
                }
                Text(Str.t("set.glowStrength"), color = C.Text, fontSize = 15.sp, modifier = Modifier.padding(top = 14.dp, bottom = 8.dp))
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    listOf("low" to Str.t("set.glowLow"), "mid" to Str.t("set.glowNormal"), "high" to Str.t("set.glowHigh")).forEach { (v, l) ->
                        Chip(l, ps.glowStrength == v) { Connection.setShared("glowStrength", v) }
                    }
                }
            }
        }
    }
}
