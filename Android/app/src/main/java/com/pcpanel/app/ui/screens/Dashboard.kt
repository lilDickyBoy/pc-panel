package com.pcpanel.app.ui.screens

import android.app.Activity
import android.content.res.Configuration
import android.view.WindowManager
import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.DeveloperBoard
import androidx.compose.material.icons.rounded.Memory
import androidx.compose.material.icons.rounded.MusicNote
import androidx.compose.material.icons.rounded.Pause
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.SkipNext
import androidx.compose.material.icons.rounded.SkipPrevious
import androidx.compose.material.icons.rounded.Storage
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
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.Connection
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.bounceClick
import com.pcpanel.app.ui.theme.C
import kotlinx.coroutines.delay
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlin.math.min
import kotlin.math.roundToInt
import kotlin.random.Random

// neon palette (same feel as the PC notch)
private val CpuColors = listOf(Color(0xFF6FD3FF), Color(0xFF2F8BFF), Color(0xFF1E5BFF))
private val GpuColors = listOf(Color(0xFFE2A6FF), Color(0xFFA35CFF), Color(0xFF6A3BFF))
private val RamColors = listOf(Color(0xFF8CFFE0), Color(0xFF2FE0B4), Color(0xFF12B88F))
private val GlassTop = Color(0xFF15172A)
private val GlassBottom = Color(0xFF07080F)
private val EdgeBrush = Brush.linearGradient(listOf(Color(0xFF6FB5FF), Color(0xFF3B5BFF), Color(0xFF2A2F66), Color(0xFF9B5CFF)))
private val TextHi = Color(0xFFF2F4FF)
private val TextMid = Color(0xFFAEB3CC)

/** Always-on desk screen (opens while charging in landscape). Pure black + slow pixel shift against burn-in. */
@Composable
fun DashboardScreen(onClose: () -> Unit) {
    val view = LocalView.current
    val ctx = LocalContext.current
    val landscape = LocalConfiguration.current.orientation == Configuration.ORIENTATION_LANDSCAPE

    DisposableEffect(Prefs.dimDashboard) {
        view.keepScreenOn = true
        val w = (ctx as? Activity)?.window
        val old = w?.attributes?.screenBrightness ?: WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE
        if (Prefs.dimDashboard && w != null) w.attributes = w.attributes.apply { screenBrightness = 0.12f }
        onDispose {
            view.keepScreenOn = false
            if (w != null) w.attributes = w.attributes.apply { screenBrightness = old }
        }
    }
    BackHandler { onClose() }

    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(Unit) { while (true) { delay(500); now = System.currentTimeMillis() } }

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

    // music bar: slides in when something starts playing, stays ~10 s after pausing
    var lastPlaying by remember { mutableLongStateOf(0L) }
    LaunchedEffect(now, m.playing) { if (m.playing) lastPlaying = now }
    val showMusic = m.has && (m.playing || now - lastPlaying < 10_000)

    val locale = Locale(Prefs.lang.ifEmpty { "en" })
    val time = SimpleDateFormat("HH:mm", locale).format(Date(now))
    val date = SimpleDateFormat("d MMMM EEE", locale).format(Date(now)).replaceFirstChar { it.titlecase(locale) }

    val cpu = GaugeData(s.cpuLoad, s.cpuTemp, "CPU", Icons.Rounded.Memory, CpuColors)
    val gpu = GaugeData(s.gpuLoad, s.gpuTemp, "GPU", Icons.Rounded.DeveloperBoard, GpuColors)
    val ramGb = s.ramUsed / 1073741824f
    val ram = GaugeData(s.ramPct, -1f, "RAM", Icons.Rounded.Storage, RamColors, alt = if (ramGb > 0) String.format(locale, "%.1f", ramGb) else "–", altUnit = "GB")

    Box(
        Modifier
            .fillMaxSize()
            .background(Color.Black)
            .drawBehind {
                // very faint ambient glows so the black isn't flat
                drawCircle(Brush.radialGradient(listOf(Color(0x142F6BFF), Color.Transparent), center = Offset(size.width * 0.18f, size.height * 0.2f), radius = size.minDimension * 0.9f))
                drawCircle(Brush.radialGradient(listOf(Color(0x109B5CFF), Color.Transparent), center = Offset(size.width * 0.85f, size.height * 0.95f), radius = size.minDimension * 0.9f))
            }
            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { showClose = !showClose }
    ) {
        BoxWithConstraints(Modifier.fillMaxSize().offset(sx, sy)) {
            if (landscape) {
                val k = min(maxWidth.value / 860f, maxHeight.value / 400f).coerceIn(0.6f, 1.6f)
                Column(
                    Modifier.fillMaxSize().padding(horizontal = (28 * k).dp, vertical = (14 * k).dp),
                    verticalArrangement = Arrangement.Center,
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Row(
                        Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceEvenly,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        NeonGauge(cpu, (132 * k).dp, k)
                        NeonGauge(gpu, (132 * k).dp, k)
                        ClockPill(time, date, k)
                        NeonGauge(ram, (132 * k).dp, k)
                    }
                    MusicBarHost(showMusic, k, wide = true)
                }
            } else {
                val k = min(maxWidth.value / 400f, maxHeight.value / 860f).coerceIn(0.6f, 1.6f)
                Column(
                    Modifier.fillMaxSize().padding(horizontal = (18 * k).dp, vertical = (40 * k).dp),
                    verticalArrangement = Arrangement.SpaceEvenly,
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    ClockPill(time, date, k * 0.86f)
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
                        NeonGauge(cpu, (106 * k).dp, k * 0.82f)
                        NeonGauge(gpu, (106 * k).dp, k * 0.82f)
                        NeonGauge(ram, (106 * k).dp, k * 0.82f)
                    }
                    MusicBarHost(showMusic, k, wide = false)
                }
            }
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

// ====================================================================== glass + glow helpers
/** Soft neon halo drawn outside the shape: a few widening, fading strokes. */
private fun Modifier.neonHalo(radius: Dp, colors: List<Color>, strength: Float = 1f) = drawBehind {
    val r = min(radius.toPx(), size.minDimension / 2f)
    val brush = Brush.linearGradient(colors)
    val layers = listOf(18.dp to 0.05f, 11.dp to 0.08f, 6.dp to 0.13f, 2.5.dp to 0.22f)
    for ((w, a) in layers) {
        val sw = w.toPx()
        drawRoundRect(
            brush, topLeft = Offset.Zero, size = size, cornerRadius = CornerRadius(r, r),
            alpha = (a * strength).coerceAtMost(1f), style = Stroke(sw)
        )
    }
}

private fun Modifier.glass(shape: Shape, radius: Dp, glow: List<Color>, glowStrength: Float = 1f) = this
    .neonHalo(radius, glow, glowStrength)
    .clip(shape)
    .background(Brush.verticalGradient(listOf(GlassTop, GlassBottom)))
    .drawBehind {
        // glossy highlight along the top edge
        drawRect(Brush.verticalGradient(listOf(Color(0x14FFFFFF), Color.Transparent), endY = size.height * 0.45f))
    }
    .border(1.4.dp, EdgeBrush, shape)

// ====================================================================== clock
@Composable
private fun ClockPill(time: String, date: String, k: Float) {
    val shape = RoundedCornerShape(50)
    Column(
        Modifier
            .glass(shape, (90 * k).dp, listOf(Color(0xFF4F9BFF), Color(0xFF6A5CFF), Color(0xFFA35CFF)), 1.2f)
            .padding(horizontal = (58 * k).dp, vertical = (14 * k).dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Text(
            time,
            style = TextStyle(
                brush = Brush.verticalGradient(listOf(Color.White, Color(0xFFD5DCFF), Color(0xFFAFC0FF))),
                fontSize = (96 * k).sp, fontWeight = FontWeight.ExtraBold, letterSpacing = (-1.5).sp
            )
        )
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = (0 * k).dp, bottom = (4 * k).dp)) {
            Box(Modifier.width((34 * k).dp).height(1.dp).background(Brush.horizontalGradient(listOf(Color.Transparent, Color(0x66AEB3CC)))))
            Text(date, color = TextMid, fontSize = (19 * k).sp, fontWeight = FontWeight.Medium, modifier = Modifier.padding(horizontal = (14 * k).dp))
            Box(Modifier.width((34 * k).dp).height(1.dp).background(Brush.horizontalGradient(listOf(Color(0x66AEB3CC), Color.Transparent))))
        }
    }
}

// ====================================================================== gauges
private class GaugeData(
    val load: Float, val temp: Float, val label: String, val icon: ImageVector, val colors: List<Color>,
    val alt: String? = null, val altUnit: String = "",
)

@Composable
private fun NeonGauge(d: GaugeData, size: Dp, k: Float) {
    val v by animateFloatAsState(d.load.coerceIn(0f, 100f), spring(dampingRatio = 0.9f, stiffness = Spring.StiffnessVeryLow), label = "g")
    val accent = d.colors[1]
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Box(Modifier.size(size), contentAlignment = Alignment.Center) {
            Canvas(Modifier.fillMaxSize()) {
                val sw = (10 * k).dp.toPx()
                val inset = sw * 1.6f
                val arc = Size(this.size.width - inset * 2, this.size.height - inset * 2)
                val tl = Offset(inset, inset)
                drawArc(Color(0xFF1A1C27), 135f, 270f, false, tl, arc, style = Stroke(sw, cap = StrokeCap.Round))
                val sweep = 270f * v / 100f
                if (sweep > 0.6f) {
                    val brush = Brush.linearGradient(
                        listOf(d.colors[2], d.colors[1], d.colors[0]),
                        start = Offset(0f, this.size.height), end = Offset(this.size.width * 0.7f, 0f)
                    )
                    // glow
                    for ((wMul, a) in listOf(3.4f to 0.06f, 2.4f to 0.10f, 1.6f to 0.18f)) {
                        drawArc(brush, 135f, sweep, false, tl, arc, alpha = a, style = Stroke(sw * wMul, cap = StrokeCap.Round))
                    }
                    drawArc(brush, 135f, sweep, false, tl, arc, style = Stroke(sw, cap = StrokeCap.Round))
                }
            }
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                val big = (30 * k).sp
                val top = if (d.temp >= 0) buildAnnotatedString {
                    append("${d.temp.roundToInt()}")
                    withStyle(SpanStyle(fontWeight = FontWeight.Normal, color = TextMid, fontSize = big * 0.8f)) { append("°C") }
                } else buildAnnotatedString {
                    append(d.alt ?: "${d.load.roundToInt()}")
                    withStyle(SpanStyle(fontWeight = FontWeight.Normal, color = TextMid, fontSize = big * 0.55f)) { append(" " + d.altUnit) }
                }
                Text(top, color = TextHi, fontSize = big, fontWeight = FontWeight.Bold, maxLines = 1)
                Box(Modifier.padding(vertical = (5 * k).dp).width(size * 0.5f).height(1.dp).background(Color(0x33FFFFFF)))
                Text("${d.load.roundToInt()}%", color = Color(0xFFC9CDE0), fontSize = (21 * k).sp, fontWeight = FontWeight.SemiBold)
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = (2 * k).dp)) {
            Icon(d.icon, null, tint = accent, modifier = Modifier.size((22 * k).dp))
            Text(d.label, color = accent, fontSize = (17 * k).sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = (8 * k).dp))
        }
    }
}

// ====================================================================== music
@Composable
private fun MusicBarHost(visible: Boolean, k: Float, wide: Boolean) {
    AnimatedVisibility(
        visible,
        enter = fadeIn(tween(380)) +
            expandVertically(spring(dampingRatio = 0.82f, stiffness = Spring.StiffnessLow), expandFrom = Alignment.Top) +
            slideInVertically(spring(dampingRatio = 0.72f, stiffness = Spring.StiffnessLow)) { it / 2 } +
            scaleIn(spring(dampingRatio = 0.7f, stiffness = Spring.StiffnessLow), initialScale = 0.9f),
        exit = fadeOut(tween(260)) +
            slideOutVertically(tween(320)) { it / 3 } +
            scaleOut(tween(320), targetScale = 0.94f) +
            shrinkVertically(tween(360), shrinkTowards = Alignment.Top)
    ) {
        MusicBar(k, wide)
    }
}

@Composable
private fun MusicBar(k: Float, wide: Boolean) {
    val m = Connection.media
    val art = Connection.art
    val shape = RoundedCornerShape((40 * k).dp)
    var pos by remember { mutableStateOf(m.positionNow()) }
    LaunchedEffect(m) { while (true) { pos = m.positionNow(); delay(500) } }
    val frac = if (m.dur > 0) (pos / m.dur).toFloat().coerceIn(0f, 1f) else 0f
    val f by animateFloatAsState(frac, tween(500), label = "p")

    Row(
        Modifier
            .padding(top = (22 * k).dp)
            .fillMaxWidth(if (wide) 0.86f else 1f)
            .widthIn(max = 980.dp)
            .glass(shape, (40 * k).dp, listOf(Color(0xFF2F6BFF), Color(0xFF3B3BFF), Color(0xFF9B5CFF)), 0.9f)
            .padding((14 * k).dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(
            Modifier.size((if (wide) 84 else 64).times(k).dp).clip(RoundedCornerShape((16 * k).dp)).background(Color(0xFF1C1E2B)),
            contentAlignment = Alignment.Center
        ) {
            if (art != null) Image(art, null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
            else Icon(Icons.Rounded.MusicNote, null, tint = TextMid, modifier = Modifier.size((30 * k).dp))
        }
        Column(Modifier.padding(start = (22 * k).dp).weight(1f)) {
            Text(
                m.title.ifBlank { "—" }, color = TextHi, fontSize = ((if (wide) 25 else 19) * k).sp, fontWeight = FontWeight.Bold,
                maxLines = 1, overflow = TextOverflow.Ellipsis
            )
            Text(
                m.artist, color = TextMid, fontSize = ((if (wide) 18 else 15) * k).sp, fontWeight = FontWeight.Medium,
                maxLines = 1, overflow = TextOverflow.Ellipsis, letterSpacing = 0.4.sp, modifier = Modifier.padding(top = (2 * k).dp)
            )
            // the line being sung right now (from the PC's lyrics), sliding in like on the notch
            val line = if (m.playing) Connection.lyricLine else ""
            androidx.compose.animation.AnimatedContent(
                targetState = line,
                transitionSpec = {
                    (androidx.compose.animation.slideInVertically(spring(dampingRatio = 0.8f, stiffness = Spring.StiffnessMediumLow)) { it / 2 } + fadeIn(tween(260))) togetherWith
                        (androidx.compose.animation.slideOutVertically(tween(200)) { -it / 2 } + fadeOut(tween(160)))
                },
                label = "lyric"
            ) { l ->
                if (l.isNotBlank()) Text(
                    l, color = Color(0xFFDCE3FF), fontSize = ((if (wide) 16 else 13) * k).sp, fontWeight = FontWeight.SemiBold,
                    maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = (6 * k).dp)
                )
            }
            if (!wide) ProgressLine(f, k, Modifier.padding(top = (10 * k).dp).fillMaxWidth())
        }
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(horizontal = (if (wide) 18 else 6).times(k).dp)) {
            CtrlIcon(Icons.Rounded.SkipPrevious, (40 * k).dp) { Connection.mediaAction("prev") }
            PlayButton(m.playing, (if (wide) 72 else 58).times(k).dp, Modifier.padding(horizontal = (if (wide) 22 else 8).times(k).dp))
            CtrlIcon(Icons.Rounded.SkipNext, (40 * k).dp) { Connection.mediaAction("next") }
        }
        if (wide) ProgressLine(f, k, Modifier.padding(start = (12 * k).dp, end = (14 * k).dp).width((230 * k).dp))
    }
}

@Composable
private fun ProgressLine(f: Float, k: Float, modifier: Modifier) {
    Canvas(modifier.height((10 * k).dp)) {
        val h = (6 * k).dp.toPx()
        val y = size.height / 2
        drawLine(Color(0xFF2A2D3D), Offset(h / 2, y), Offset(size.width - h / 2, y), strokeWidth = h, cap = StrokeCap.Round)
        if (f > 0.005f) {
            val end = Offset(h / 2 + (size.width - h) * f, y)
            val brush = Brush.horizontalGradient(listOf(Color(0xFF2F6BFF), Color(0xFF4FA3FF)), endX = end.x)
            drawLine(brush, Offset(h / 2, y), end, strokeWidth = h * 2.6f, cap = StrokeCap.Round, alpha = 0.16f)
            drawLine(brush, Offset(h / 2, y), end, strokeWidth = h, cap = StrokeCap.Round)
        }
    }
}

@Composable
private fun CtrlIcon(icon: ImageVector, size: Dp, onClick: () -> Unit) {
    Icon(icon, null, tint = Color.White, modifier = Modifier.size(size).bounceClick(pressedScale = 0.82f) { onClick() })
}

@Composable
private fun PlayButton(playing: Boolean, size: Dp, modifier: Modifier) {
    val pulse by animateFloatAsState(if (playing) 1f else 0.94f, spring(dampingRatio = 0.5f, stiffness = Spring.StiffnessLow), label = "pp")
    Box(
        modifier
            .size(size)
            .scale(pulse)
            .drawBehind {
                drawCircle(Brush.radialGradient(listOf(Color(0x552F6BFF), Color.Transparent)), radius = this.size.minDimension * 0.75f)
            }
            .clip(CircleShape)
            .background(Brush.radialGradient(listOf(Color(0xFF15193A), Color(0xFF080A16))))
            .border(2.dp, Brush.linearGradient(listOf(Color(0xFF4F9BFF), Color(0xFF3B3BFF), Color(0xFF7B5CFF))), CircleShape)
            .bounceClick(pressedScale = 0.86f) { Connection.mediaAction("toggle") },
        contentAlignment = Alignment.Center
    ) {
        Icon(if (playing) Icons.Rounded.Pause else Icons.Rounded.PlayArrow, null, tint = Color.White, modifier = Modifier.size(size * 0.5f))
    }
}

