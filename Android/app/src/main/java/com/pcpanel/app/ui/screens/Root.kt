package com.pcpanel.app.ui.screens

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.res.Configuration
import android.os.BatteryManager
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.slideOutVertically
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Widgets
import androidx.compose.material.icons.rounded.NotificationsActive
import androidx.compose.material.icons.rounded.Bedtime
import androidx.compose.material.icons.rounded.MusicNote
import androidx.compose.material.icons.rounded.PowerSettingsNew
import androidx.compose.material.icons.rounded.RestartAlt
import androidx.compose.material.icons.rounded.Settings
import androidx.compose.material.icons.rounded.Speed
import androidx.compose.material.icons.rounded.TouchApp
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.BannerKind
import com.pcpanel.app.net.ConnState
import com.pcpanel.app.net.Connection
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.BannerHost
import com.pcpanel.app.ui.components.DialogButton
import com.pcpanel.app.ui.components.IosDialog
import com.pcpanel.app.ui.components.SlideToConfirm
import com.pcpanel.app.ui.components.bounceClick
import com.pcpanel.app.ui.theme.C

/** Overlays that must cover the whole screen (above the tab bar). */
object UiState {
    var powerConfirm by mutableStateOf<String?>(null)
    var killTarget by mutableStateOf<String?>(null)
    var appPicker by mutableStateOf(false)
    var forgetConfirm by mutableStateOf(false)
    var dashboard by mutableStateOf(false)
    var tab by mutableIntStateOf(0)
    var toolPage by mutableStateOf<String?>(null)
}

@Composable
fun rememberQrScanner(): () -> Unit {
    val launcher = rememberLauncherForActivityResult(ScanContract()) { r ->
        val txt = r.contents ?: return@rememberLauncherForActivityResult
        if (Connection.pairFromUri(txt)) Connection.showBanner(Str.t("pair.ok"), BannerKind.Success)
        else Connection.showBanner(Str.t("pair.bad"), BannerKind.Error)
    }
    return {
        launcher.launch(
            ScanOptions()
                .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                .setPrompt(Str.t("pair.prompt"))
                .setBeepEnabled(false)
                .setOrientationLocked(false)
        )
    }
}

@Composable
fun RootScreen() {
    val state = Connection.state
    // After a drop, keep the dashboard for a short grace period (quick reconnects),
    // then fall back to the connection screen so Connect / Wake PC are reachable.
    var graceOver by remember { mutableStateOf(false) }
    LaunchedEffect(state == ConnState.Connected) {
        graceOver = false
        if (state != ConnState.Connected) { kotlinx.coroutines.delay(6000); graceOver = true }
    }
    val page = when {
        Prefs.pc == null || state == ConnState.Unpaired -> 0
        state == ConnState.Connected -> 2
        Connection.hasStats && !graceOver && (state == ConnState.Connecting || state == ConnState.Offline) -> 2
        else -> 1
    }

    AutoDashboard()

    Box(Modifier.fillMaxSize().background(C.Bg)) {
        AnimatedContent(
            targetState = page,
            transitionSpec = {
                (fadeIn(tween(320)) + scaleIn(spring(dampingRatio = 0.8f, stiffness = 300f), initialScale = 0.94f)) togetherWith
                    (fadeOut(tween(180)) + scaleOut(tween(180), targetScale = 1.03f))
            },
            label = "root"
        ) { p ->
            when (p) {
                0 -> PairScreen()
                1 -> ConnectionScreen()
                else -> MainTabs()
            }
        }

        // ---------- global overlays ----------
        PowerConfirmDialog()
        KillConfirmDialog()
        ForgetConfirmDialog()

        androidx.compose.animation.AnimatedVisibility(
            UiState.appPicker,
            enter = slideInVertically(spring(dampingRatio = 0.85f, stiffness = 320f)) { it } + fadeIn(),
            exit = slideOutVertically(tween(240)) { it } + fadeOut(tween(240))
        ) { AppPicker(onClose = { UiState.appPicker = false }) }

        androidx.compose.animation.AnimatedVisibility(
            UiState.dashboard,
            enter = fadeIn(tween(350)) + scaleIn(spring(dampingRatio = 0.85f, stiffness = 260f), initialScale = 0.9f),
            exit = fadeOut(tween(250)) + scaleOut(tween(250), targetScale = 0.95f)
        ) { DashboardScreen(onClose = { UiState.dashboard = false }) }

        ScreenshotPreview()
        RingDialog()

        Box(Modifier.align(Alignment.TopCenter)) { BannerHost(Connection.banner) }
    }
}

// ================================================================== auto dashboard (charging + landscape)
@Composable
private fun AutoDashboard() {
    val ctx = LocalContext.current
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE
    var charging by remember { mutableStateOf(isCharging(ctx)) }
    DisposableEffect(Unit) {
        val r = object : BroadcastReceiver() {
            override fun onReceive(c: Context?, i: Intent?) {
                charging = i?.action == Intent.ACTION_POWER_CONNECTED
            }
        }
        val f = IntentFilter().apply { addAction(Intent.ACTION_POWER_CONNECTED); addAction(Intent.ACTION_POWER_DISCONNECTED) }
        ContextCompat.registerReceiver(ctx, r, f, ContextCompat.RECEIVER_NOT_EXPORTED)
        onDispose { try { ctx.unregisterReceiver(r) } catch (_: Exception) { } }
    }
    val connected = Connection.state == ConnState.Connected
    LaunchedEffect(charging, landscape, connected) {
        if (Prefs.autoDashboard && charging && landscape && connected) UiState.dashboard = true
    }
}

private fun isCharging(ctx: Context): Boolean {
    val i = ctx.registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED)) ?: return false
    return i.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) != 0
}

// ================================================================== tabs
private data class TabDef(val icon: androidx.compose.ui.graphics.vector.ImageVector, val key: String)

private val tabs = listOf(
    TabDef(Icons.Rounded.Speed, "tab.home"),
    TabDef(Icons.Rounded.MusicNote, "tab.media"),
    TabDef(Icons.Rounded.TouchApp, "tab.control"),
    TabDef(Icons.Rounded.Widgets, "tab.tools"),
    TabDef(Icons.Rounded.Settings, "tab.settings"),
)

@Composable
fun MainTabs() {
    val tab = UiState.tab
    Column(Modifier.fillMaxSize()) {
        Box(Modifier.weight(1f)) {
            AnimatedContent(
                targetState = tab,
                transitionSpec = {
                    val dir = if (targetState > initialState) 1 else -1
                    (slideInHorizontally(spring(dampingRatio = 0.88f, stiffness = 420f)) { it / 7 * dir } + fadeIn(tween(220))) togetherWith
                        (slideOutHorizontally(tween(200)) { -it / 12 * dir } + fadeOut(tween(140)))
                },
                label = "tabs"
            ) { t ->
                when (t) {
                    0 -> HomeScreen()
                    1 -> MediaScreen()
                    2 -> ControlScreen()
                    3 -> ToolsScreen()
                    else -> SettingsScreen()
                }
            }
            // reconnecting pill
            androidx.compose.animation.AnimatedVisibility(
                Connection.state != ConnState.Connected,
                modifier = Modifier.align(Alignment.TopCenter).statusBarsPadding().padding(top = 6.dp),
                enter = slideInVertically(spring(dampingRatio = 0.7f)) { -it * 2 } + fadeIn(),
                exit = slideOutVertically { -it * 2 } + fadeOut()
            ) {
                Row(
                    Modifier.clip(RoundedCornerShape(50)).background(Color(0xFF2C2C2E)).padding(horizontal = 14.dp, vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    CircularProgressIndicator(Modifier.size(14.dp), color = C.Orange, strokeWidth = 2.dp)
                    Text(Str.t("conn.connecting"), color = C.Text, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 8.dp))
                }
            }
        }
        TabBar(tab) { if (it == UiState.tab && it == 3) UiState.toolPage = null; UiState.tab = it }
    }
}

@Composable
private fun TabBar(selected: Int, onSelect: (Int) -> Unit) {
    Column(Modifier.fillMaxWidth().background(Color(0xFF0C0C0D))) {
        Box(Modifier.fillMaxWidth().height(0.5.dp).background(Color(0xFF2A2A2C)))
        Row(Modifier.fillMaxWidth().navigationBarsPadding().padding(top = 6.dp, bottom = 4.dp)) {
            tabs.forEachIndexed { i, t ->
                val sel = i == selected
                val bounce = remember { Animatable(1f) }
                LaunchedEffect(sel) {
                    if (sel) {
                        bounce.snapTo(0.78f)
                        bounce.animateTo(1f, spring(dampingRatio = 0.38f, stiffness = 520f))
                    }
                }
                Column(
                    Modifier.weight(1f).bounceClick(pressedScale = 0.9f) { onSelect(i) },
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Icon(
                        t.icon, null,
                        tint = if (sel) C.Blue else C.Text2,
                        modifier = Modifier.size(26.dp).graphicsLayer { scaleX = bounce.value; scaleY = bounce.value }
                    )
                    Text(Str.t(t.key), color = if (sel) C.Blue else C.Text2, fontSize = 10.5.sp, fontWeight = FontWeight.Medium, maxLines = 1)
                }
            }
        }
    }
}

// ================================================================== dialogs
@Composable
private fun PowerConfirmDialog() {
    val action = UiState.powerConfirm
    var shown by remember { mutableStateOf("sleep") }
    if (action != null) shown = action
    BackHandler(action != null) { UiState.powerConfirm = null }
    IosDialog(visible = action != null, onDismiss = { UiState.powerConfirm = null }) {
        val (icon, color, title, slide) = when (shown) {
            "restart" -> Quad(Icons.Rounded.RestartAlt, C.Orange, Str.t("act.restart"), Str.t("act.slideRestart"))
            "shutdown" -> Quad(Icons.Rounded.PowerSettingsNew, C.Red, Str.t("act.shutdown"), Str.t("act.slideShutdown"))
            else -> Quad(Icons.Rounded.Bedtime, C.Indigo, Str.t("act.sleep"), Str.t("act.slideSleep"))
        }
        Box(Modifier.size(64.dp).clip(RoundedCornerShape(20.dp)).background(color.copy(alpha = 0.18f)), contentAlignment = Alignment.Center) {
            Icon(icon, null, tint = color, modifier = Modifier.size(34.dp))
        }
        Text(title, color = C.Text, fontSize = 22.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 14.dp, bottom = 4.dp))
        Text(Prefs.pc?.name ?: "PC", color = C.Text2, fontSize = 15.sp)
        Spacer(Modifier.height(22.dp))
        SlideToConfirm(slide, color, icon) {
            Connection.power(shown)
            UiState.powerConfirm = null
        }
        Spacer(Modifier.height(10.dp))
        DialogButton(Str.t("act.cancel")) { UiState.powerConfirm = null }
    }
}

private data class Quad<A, B, C2, D>(val a: A, val b: B, val c: C2, val d: D)

@Composable
private fun KillConfirmDialog() {
    val target = UiState.killTarget
    var shown by remember { mutableStateOf("") }
    if (target != null) shown = target
    BackHandler(target != null) { UiState.killTarget = null }
    IosDialog(visible = target != null, onDismiss = { UiState.killTarget = null }) {
        Text(Str.t("apps.endConfirm"), color = C.Text2, fontSize = 15.sp)
        Text(shown, color = C.Text, fontSize = 22.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(vertical = 6.dp))
        Text(Str.t("apps.endWarn"), color = C.Text2, fontSize = 13.sp)
        Spacer(Modifier.height(20.dp))
        DialogButton(Str.t("apps.end"), color = C.Red, filled = true) { Connection.kill(shown); UiState.killTarget = null }
        Spacer(Modifier.height(10.dp))
        DialogButton(Str.t("act.cancel")) { UiState.killTarget = null }
    }
}

@Composable
private fun ForgetConfirmDialog() {
    BackHandler(UiState.forgetConfirm) { UiState.forgetConfirm = false }
    IosDialog(visible = UiState.forgetConfirm, onDismiss = { UiState.forgetConfirm = false }) {
        Text(Str.t("set.forget"), color = C.Text, fontSize = 21.sp, fontWeight = FontWeight.Bold)
        Text(Str.t("set.forgetConfirm"), color = C.Text2, fontSize = 14.sp, modifier = Modifier.padding(top = 6.dp))
        Spacer(Modifier.height(20.dp))
        DialogButton(Str.t("set.forget"), color = C.Red, filled = true) { UiState.forgetConfirm = false; Connection.forget() }
        Spacer(Modifier.height(10.dp))
        DialogButton(Str.t("act.cancel")) { UiState.forgetConfirm = false }
    }
}

@Composable
private fun RingDialog() {
    val ctx = LocalContext.current
    val ringing = com.pcpanel.app.service.Ringer.ringing
    BackHandler(ringing) { com.pcpanel.app.service.Ringer.stop(ctx) }
    IosDialog(visible = ringing, onDismiss = { com.pcpanel.app.service.Ringer.stop(ctx) }) {
        Box(Modifier.size(70.dp).clip(RoundedCornerShape(22.dp)).background(C.Blue.copy(alpha = 0.18f)), contentAlignment = Alignment.Center) {
            Icon(Icons.Rounded.NotificationsActive, null, tint = C.Blue, modifier = Modifier.size(38.dp))
        }
        Text(Str.t("ring.title"), color = C.Text, fontSize = 22.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 14.dp, bottom = 4.dp))
        Text(Str.t("ring.text"), color = C.Text2, fontSize = 15.sp)
        Spacer(Modifier.height(22.dp))
        DialogButton(Str.t("ring.stop"), color = C.Blue, filled = true) { com.pcpanel.app.service.Ringer.stop(ctx) }
    }
}

@Composable
private fun ScreenshotPreview() {
    val shot = Connection.screenshot
    BackHandler(shot != null) { Connection.screenshot = null }
    IosDialog(visible = shot != null, onDismiss = { Connection.screenshot = null }) {
        if (shot != null) {
            Image(
                shot, null, contentScale = ContentScale.Fit,
                modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp))
            )
        }
        Spacer(Modifier.height(16.dp))
        DialogButton(Str.t("ok")) { Connection.screenshot = null }
    }
}
