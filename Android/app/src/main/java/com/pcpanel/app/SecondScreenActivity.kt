package com.pcpanel.app

import android.annotation.SuppressLint
import android.content.pm.ActivityInfo
import android.graphics.Color as AColor
import android.os.Build
import android.os.Bundle
import android.util.DisplayMetrics
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.DesktopWindows
import androidx.compose.material.icons.rounded.ErrorOutline
import androidx.compose.material.icons.rounded.Usb
import androidx.compose.material.icons.rounded.Wifi
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.pcpanel.app.net.ConnState
import com.pcpanel.app.net.Connection
import com.pcpanel.app.net.ScreenLink
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.DialogButton
import com.pcpanel.app.ui.components.bounceClick
import com.pcpanel.app.ui.theme.C
import com.pcpanel.app.ui.theme.PcPanelTheme
import kotlinx.coroutines.delay

/**
 * Full-screen "second monitor" view: the PC's extra screen, live, at 60 fps.
 * Back (gesture or button) ends it and the extra monitor disappears from Windows again.
 */
class SecondScreenActivity : ComponentActivity() {

    private var surfaceView: SurfaceView? = null
    private var holderReady = false
    private var closing = false

    @SuppressLint("ClickableViewAccessibility")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        if (Build.VERSION.SDK_INT >= 28) {
            window.attributes = window.attributes.apply {
                layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
            }
        }
        window.statusBarColor = AColor.BLACK
        window.navigationBarColor = AColor.BLACK
        WindowCompat.setDecorFitsSystemWindows(window, false)
        immersive()
        applyOrientation()

        val sv = SurfaceView(this)
        sv.holder.addCallback(object : SurfaceHolder.Callback {
            override fun surfaceCreated(h: SurfaceHolder) { holderReady = true; tryStart() }
            override fun surfaceChanged(h: SurfaceHolder, format: Int, w: Int, hgt: Int) { }
            override fun surfaceDestroyed(h: SurfaceHolder) { holderReady = false; ScreenLink.surfaceChanged(null) }
        })
        sv.setOnTouchListener { v, e ->
            if (Connection.pcSettings.ssTouch) ScreenLink.touch(e, v.width, v.height)
            true
        }
        surfaceView = sv

        setContent {
            PcPanelTheme {
                SecondScreenUi(sv, onClose = { finishScreen() })
            }
        }
        requestFromPc()
    }

    override fun onNewIntent(intent: android.content.Intent) {
        super.onNewIntent(intent)
        // the PC asked again (e.g. orientation changed there): answer with a fresh request
        applyOrientation()
        requestFromPc()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) immersive()
    }

    private fun immersive() {
        val c = WindowInsetsControllerCompat(window, window.decorView)
        c.hide(WindowInsetsCompat.Type.systemBars())
        c.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
    }

    private fun applyOrientation() {
        requestedOrientation = if (Connection.pcSettings.ssOrient == "portrait") ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT
        else ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
    }

    /** Tell the PC this phone's real screen size so the virtual monitor matches it pixel for pixel. */
    private fun requestFromPc() {
        val (w, h) = if (Build.VERSION.SDK_INT >= 30) {
            val b = windowManager.maximumWindowMetrics.bounds
            b.width() to b.height()
        } else {
            val dm = DisplayMetrics()
            @Suppress("DEPRECATION") windowManager.defaultDisplay.getRealMetrics(dm)
            dm.widthPixels to dm.heightPixels
        }
        Connection.whenConnected { Connection.ssRequest(w, h) }
    }

    fun tryStart() {
        val o = Connection.ssOpen ?: return
        val sv = surfaceView ?: return
        if (!holderReady) return
        ScreenLink.start(o, sv.holder.surface)
    }

    fun onOrientationSetting() {
        applyOrientation()
    }

    fun finishScreen() {
        if (closing) return
        closing = true
        ScreenLink.stop()
        Connection.ssStop()
        finish()
    }

    override fun onDestroy() {
        super.onDestroy()
        if (isFinishing && !closing) {
            closing = true
            ScreenLink.stop()
            Connection.ssStop()
        }
    }
}

// ============================================================================ UI
@Composable
private fun SecondScreenUi(sv: SurfaceView, onClose: () -> Unit) {
    val act = androidx.compose.ui.platform.LocalContext.current as SecondScreenActivity
    val open = Connection.ssOpen
    val pcState = Connection.ssState
    val phase = ScreenLink.phase
    val settings = Connection.pcSettings

    // start the stream once the PC told us where to connect
    LaunchedEffect(open?.id) { if (open != null) act.tryStart() }
    // PC ended it (Stop button on the PC, PC Panel closed…)
    val closeTick = Connection.ssCloseTick
    val firstTick = remember { closeTick }
    LaunchedEffect(closeTick) { if (closeTick != firstTick) onClose() }
    LaunchedEffect(settings.ssOrient) { act.onOrientationSetting() }

    BackHandler { onClose() }

    Box(Modifier.fillMaxSize().background(Color.Black)) {
        AndroidView(factory = { sv }, modifier = Modifier.fillMaxSize())

        val live = phase == ScreenLink.Phase.Live
        // ---------- connecting / error overlay
        AnimatedVisibility(!live, enter = fadeIn(tween(200)), exit = fadeOut(tween(450))) {
            Overlay(pcState.state, pcState.step, pcState.progress, pcState.error, phase, onClose)
        }

        // ---------- small status pill (FPS) and a one-time hint how to close
        var hint by remember { mutableStateOf(false) }
        LaunchedEffect(live) {
            if (live) { hint = true; delay(3500); hint = false }
        }
        AnimatedVisibility(
            live && (settings.ssFps || hint),
            modifier = Modifier.align(Alignment.TopCenter).padding(top = 14.dp),
            enter = slideInVertically(spring(dampingRatio = 0.7f, stiffness = 380f)) { -it * 2 } + fadeIn(),
            exit = slideOutVertically(tween(260)) { -it * 2 } + fadeOut(tween(260))
        ) {
            Row(
                Modifier.clip(RoundedCornerShape(50)).background(Color(0xCC1C1C1E)).padding(horizontal = 14.dp, vertical = 7.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(if (ScreenLink.usb) Icons.Rounded.Usb else Icons.Rounded.Wifi, null, tint = C.Green, modifier = Modifier.size(15.dp))
                Text(
                    if (hint && !settings.ssFps) Str.t("ss.backHint") else "${ScreenLink.fps} FPS" + (if (pcState.latency > 0) " · ${pcState.latency} ms" else ""),
                    color = C.Text, fontSize = 12.5.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 7.dp)
                )
            }
        }
    }
}

@Composable
private fun Overlay(state: String, step: String, progress: Double, error: String, phase: ScreenLink.Phase, onClose: () -> Unit) {
    val failed = state == "error" || phase == ScreenLink.Phase.Lost && state != "waiting" && state != "preparing"
    val noPc = Connection.state != ConnState.Connected && Connection.ssOpen == null
    Box(
        Modifier.fillMaxSize().background(Brush.radialGradient(listOf(Color(0xFF0B1A33), Color.Black), radius = 1400f)),
        contentAlignment = Alignment.Center
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(32.dp)) {
            Pulse(failed)
            Spacer(Modifier.height(26.dp))
            val title = when {
                failed -> Str.t(if (error.isNotBlank()) error else "ss.err.lost")
                noPc -> Str.t("conn.connecting")
                phase == ScreenLink.Phase.Lost -> Str.t("ss.reconnecting")
                Connection.ssOpen != null -> Str.t("ss.step.waiting")
                step.isNotBlank() -> Str.t(step)
                else -> Str.t("ss.step.asking")
            }
            Text(title, color = C.Text, fontSize = 21.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center)
            Text(
                if (failed) Str.t("ss.failedSub") else Str.t("ss.preparingSub"),
                color = C.Text2, fontSize = 14.sp, textAlign = TextAlign.Center, lineHeight = 19.sp,
                modifier = Modifier.padding(top = 8.dp).width(380.dp)
            )
            if (!failed && progress in 0.0..1.0) {
                val p by animateFloatAsState(progress.toFloat(), spring(stiffness = 120f), label = "p")
                Box(Modifier.padding(top = 18.dp).width(260.dp).height(6.dp).clip(RoundedCornerShape(3.dp)).background(C.Card2)) {
                    Box(Modifier.fillMaxWidth(p).height(6.dp).clip(RoundedCornerShape(3.dp)).background(C.Blue))
                }
                Text("${(progress * 100).toInt()}%", color = C.Text2, fontSize = 12.sp, modifier = Modifier.padding(top = 6.dp))
            }
            Spacer(Modifier.height(24.dp))
            DialogButton(Str.t(if (failed) "ss.close" else "act.cancel"), color = if (failed) C.Blue else C.Text2, filled = failed, modifier = Modifier.width(220.dp)) { onClose() }
        }
    }
}

/** Monitor icon with expanding rings while connecting (red and still on error). */
@Composable
private fun Pulse(failed: Boolean) {
    val t = rememberInfiniteTransition(label = "pulse")
    val r by t.animateFloat(0f, 1f, infiniteRepeatable(tween(1600, easing = LinearEasing), RepeatMode.Restart), label = "r")
    val bob by t.animateFloat(0.96f, 1.04f, infiniteRepeatable(tween(900), RepeatMode.Reverse), label = "b")
    val color = if (failed) C.Red else C.Blue
    Box(Modifier.size(150.dp), contentAlignment = Alignment.Center) {
        if (!failed) Canvas(Modifier.fillMaxSize()) {
            for (k in 0..1) {
                val f = (r + k * 0.5f) % 1f
                drawCircle(color.copy(alpha = (1f - f) * 0.5f), radius = size.minDimension / 2 * (0.45f + 0.55f * f), style = Stroke(width = 3.dp.toPx(), cap = StrokeCap.Round))
            }
        }
        Box(
            Modifier.size(78.dp).scale(if (failed) 1f else bob).clip(RoundedCornerShape(24.dp))
                .background(Brush.linearGradient(listOf(color, color.copy(alpha = 0.6f)), Offset.Zero, Offset(200f, 200f))),
            contentAlignment = Alignment.Center
        ) {
            Icon(if (failed) Icons.Rounded.ErrorOutline else Icons.Rounded.DesktopWindows, null, tint = Color.White, modifier = Modifier.size(40.dp))
        }
    }
}
