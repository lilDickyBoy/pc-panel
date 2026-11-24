package com.pcpanel.app.ui.screens

import android.app.Activity
import android.content.res.Configuration
import android.view.WindowManager
import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ArrowDownward
import androidx.compose.material.icons.rounded.ArrowUpward
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.MusicNote
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.Connection
import com.pcpanel.app.net.fmtSpeed
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.RingGauge
import com.pcpanel.app.ui.theme.C
import kotlinx.coroutines.delay
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlin.math.roundToInt
import kotlin.random.Random

/** Always-on desk screen. Pure black + slow pixel shift to protect the AMOLED panel. */
@Composable
fun DashboardScreen(onClose: () -> Unit) {
    val view = LocalView.current
    val ctx = LocalContext.current
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE

    DisposableEffect(Prefs.dimDashboard) {
        view.keepScreenOn = true
        val w = (ctx as? Activity)?.window
        val old = w?.attributes?.screenBrightness ?: WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE
        if (Prefs.dimDashboard && w != null) {
            w.attributes = w.attributes.apply { screenBrightness = 0.12f }
        }
        onDispose {
            view.keepScreenOn = false
            if (w != null) w.attributes = w.attributes.apply { screenBrightness = old }
        }
    }
    BackHandler { onClose() }

    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(Unit) { while (true) { delay(1000); now = System.currentTimeMillis() } }

    // burn-in protection
    var shiftX by remember { mutableStateOf(0) }
    var shiftY by remember { mutableStateOf(0) }
    LaunchedEffect(Unit) { while (true) { delay(60_000); shiftX = Random.nextInt(-10, 11); shiftY = Random.nextInt(-8, 9) } }
    val sx by animateDpAsState(shiftX.dp, tween(4000), label = "sx")
    val sy by animateDpAsState(shiftY.dp, tween(4000), label = "sy")

    var showClose by remember { mutableStateOf(false) }
    LaunchedEffect(showClose) { if (showClose) { delay(3500); showClose = false } }

    val s = Connection.stats
    val m = Connection.media
    val time = SimpleDateFormat("HH:mm", Locale.getDefault()).format(Date(now))
    val date = SimpleDateFormat("EEEE, d MMMM", if (Prefs.lang == "tr") Locale("tr") else Locale.ENGLISH).format(Date(now))

    Box(
        Modifier
            .fillMaxSize()
            .background(Color.Black)
            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { showClose = !showClose }
    ) {
        val content: @Composable () -> Unit = {
            Clock(time, date, if (landscape) 96 else 84)
            Gauges(s.cpuLoad, s.cpuTemp, s.gpuLoad, s.gpuTemp, s.ramPct, if (landscape) 118.dp else 104.dp)
            Column(horizontalAlignment = Alignment.Start) {
                if (m.has) NowPlaying(m.title, m.artist, m.playing)
                Spacer(Modifier.height(16.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Rounded.ArrowDownward, null, tint = C.Blue, modifier = Modifier.size(18.dp))
                    Text(fmtSpeed(s.netDown), color = C.Text2, fontSize = 15.sp, modifier = Modifier.padding(start = 4.dp, end = 16.dp))
                    Icon(Icons.Rounded.ArrowUpward, null, tint = C.Green, modifier = Modifier.size(18.dp))
                    Text(fmtSpeed(s.netUp), color = C.Text2, fontSize = 15.sp, modifier = Modifier.padding(start = 4.dp))
                }
            }
        }
        if (landscape) {
            Row(
                Modifier.fillMaxSize().offset(sx, sy).padding(horizontal = 40.dp, vertical = 24.dp),
                horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.CenterVertically
            ) { content() }
        } else {
            Column(
                Modifier.fillMaxSize().offset(sx, sy).padding(horizontal = 28.dp, vertical = 48.dp),
                verticalArrangement = Arrangement.SpaceEvenly, horizontalAlignment = Alignment.CenterHorizontally
            ) { content() }
        }

        androidx.compose.animation.AnimatedVisibility(showClose, enter = fadeIn(), exit = fadeOut(), modifier = Modifier.align(Alignment.TopEnd).padding(24.dp)) {
            Row(
                Modifier.clip(RoundedCornerShape(50)).background(C.Card2).clickable { onClose() }.padding(horizontal = 16.dp, vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(Icons.Rounded.Close, null, tint = C.Text, modifier = Modifier.size(18.dp))
                Text(Str.t("dash.close"), color = C.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 6.dp))
            }
        }
    }
}

@Composable
private fun Clock(time: String, date: String, size: Int) {
    Column {
        Text(time, color = Color(0xFFE5E5EA), fontSize = size.sp, fontWeight = FontWeight.Thin, letterSpacing = (-2).sp)
        Text(date, color = C.Text2, fontSize = 17.sp)
    }
}

@Composable
private fun Gauges(cpu: Float, cpuT: Float, gpu: Float, gpuT: Float, ram: Float, size: Dp) {
    Row(horizontalArrangement = Arrangement.spacedBy(18.dp)) {
        DashGauge("CPU", cpu, cpuT, C.Blue, size)
        DashGauge("GPU", gpu, gpuT, C.Green, size)
        DashGauge("RAM", ram, -1f, C.Purple, size)
    }
}

@Composable
private fun DashGauge(label: String, v: Float, t: Float, color: Color, size: Dp) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        RingGauge(v, color, size, stroke = 9.dp) {
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text("${v.roundToInt()}%", color = C.Text, fontSize = 22.sp, fontWeight = FontWeight.Bold)
                if (t >= 0) Text("${t.roundToInt()}°", color = C.temp(t), fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
            }
        }
        Text(label, color = C.Text2, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 4.dp))
    }
}

@Composable
private fun NowPlaying(title: String, artist: String, playing: Boolean) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.width(280.dp)) {
        val art = Connection.art
        Box(Modifier.size(58.dp).clip(RoundedCornerShape(12.dp)).background(C.Card2), contentAlignment = Alignment.Center) {
            if (art != null) Image(art, null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
            else Icon(Icons.Rounded.MusicNote, null, tint = C.Text3)
        }
        Column(Modifier.padding(start = 12.dp).fillMaxWidth()) {
            Text(title, color = if (playing) C.Text else C.Text2, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(artist, color = C.Text2, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}
