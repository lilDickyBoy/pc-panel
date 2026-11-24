@file:OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)

package com.pcpanel.app.ui.screens

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.basicMarquee
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.MusicNote
import androidx.compose.material.icons.rounded.Pause
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.SkipNext
import androidx.compose.material.icons.rounded.SkipPrevious
import androidx.compose.material.icons.rounded.VolumeOff
import androidx.compose.material.icons.rounded.VolumeUp
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.net.Connection
import com.pcpanel.app.net.fmtTime
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.FatSlider
import com.pcpanel.app.ui.components.bounceClick
import com.pcpanel.app.ui.theme.C
import kotlinx.coroutines.delay

@Composable
fun MediaScreen() {
    val m = Connection.media
    val accent by animateColorAsState(m.color, tween(700), label = "accent")

    // tick the progress bar only while playing and visible
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(m.playing, m.receivedAt) {
        while (m.playing) { delay(500); now = System.currentTimeMillis() }
    }

    Box(Modifier.fillMaxSize()) {
        Box(
            Modifier
                .fillMaxSize()
                .background(Brush.verticalGradient(0f to accent.copy(alpha = 0.55f), 0.55f to accent.copy(alpha = 0.12f), 1f to Color.Black))
        )
        Column(
            Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .statusBarsPadding()
                .padding(horizontal = 26.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Spacer(Modifier.height(18.dp))
            if (!m.has) {
                EmptyMedia()
            } else {

                // ------------- artwork: shrinks when paused, like Apple Music
                val artScale by animateFloatAsState(if (m.playing) 1f else 0.8f, spring(dampingRatio = 0.55f, stiffness = 200f), label = "art")
                val art = Connection.art
                Box(
                    Modifier
                        .widthIn(max = 420.dp)
                        .fillMaxWidth()
                        .aspectRatio(1f)
                        .graphicsLayer {
                            scaleX = artScale; scaleY = artScale
                            shadowElevation = 28.dp.toPx() * artScale
                            shape = RoundedCornerShape(22.dp)
                            clip = true
                        }
                        .background(C.Card2),
                    contentAlignment = Alignment.Center
                ) {
                    AnimatedContent(art, transitionSpec = { fadeIn(tween(400)) togetherWith fadeOut(tween(400)) }, label = "artFade") { a ->
                        if (a != null) Image(a, null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
                        else Box(Modifier.fillMaxSize().background(Brush.linearGradient(listOf(C.Indigo, C.Blue))), contentAlignment = Alignment.Center) {
                            Icon(Icons.Rounded.MusicNote, null, tint = Color.White.copy(alpha = 0.85f), modifier = Modifier.size(96.dp))
                        }
                    }
                }

                Spacer(Modifier.height(30.dp))
                Column(Modifier.fillMaxWidth()) {
                    Text(m.title, color = C.Text, fontSize = 23.sp, fontWeight = FontWeight.Bold, maxLines = 1, modifier = Modifier.basicMarquee())
                    Text(m.artist.ifBlank { Str.t("media.unknown") }, color = Color.White.copy(alpha = 0.6f), fontSize = 18.sp, maxLines = 1, modifier = Modifier.basicMarquee().padding(top = 2.dp))
                    if (m.app.isNotBlank()) Text(m.app, color = accent, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 6.dp))
                }

                Spacer(Modifier.height(22.dp))
                @Suppress("UNUSED_VARIABLE") val n = now
                SeekBar(m.positionNow(), m.dur) { Connection.seek(it) }

                Spacer(Modifier.height(16.dp))
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.CenterVertically) {
                    TransportButton(Icons.Rounded.SkipPrevious, 46) { Connection.mediaAction("prev") }
                    Box(
                        Modifier.size(84.dp).bounceClick(pressedScale = 0.86f) { Connection.mediaAction("toggle") }.clip(CircleShape).background(Color.White),
                        contentAlignment = Alignment.Center
                    ) {
                        AnimatedContent(
                            m.playing,
                            transitionSpec = { (scaleIn(spring(dampingRatio = 0.5f, stiffness = 500f), initialScale = 0.4f) + fadeIn()) togetherWith (scaleOut(tween(120), targetScale = 0.4f) + fadeOut(tween(120))) },
                            label = "pp"
                        ) { playing ->
                            Icon(if (playing) Icons.Rounded.Pause else Icons.Rounded.PlayArrow, null, tint = Color.Black, modifier = Modifier.size(48.dp))
                        }
                    }
                    TransportButton(Icons.Rounded.SkipNext, 46) { Connection.mediaAction("next") }
                }
                Spacer(Modifier.height(30.dp))
                VolumeRow()
            }
            Spacer(Modifier.height(26.dp))
            MusicBrowser()
            Spacer(Modifier.height(30.dp))
        }
        MusicListSheet()
    }
}

@Composable
fun VolumeRow() {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(
            Modifier.size(48.dp).bounceClick { Connection.setMute(!Connection.muted) }.clip(RoundedCornerShape(14.dp))
                .background(if (Connection.muted) C.Red.copy(alpha = 0.2f) else C.Card2),
            contentAlignment = Alignment.Center
        ) {
            Icon(if (Connection.muted) Icons.Rounded.VolumeOff else Icons.Rounded.VolumeUp, null, tint = if (Connection.muted) C.Red else C.Text)
        }
        Spacer(Modifier.size(10.dp))
        FatSlider(
            value = if (Connection.muted) 0f else Connection.volume,
            onValueChange = { },
            height = 48.dp,
            trailingText = "${((if (Connection.muted) 0f else Connection.volume) * 100).toInt()}%",
            onCommit = { Connection.setVolume(it) }
        )
    }
}

@Composable
private fun TransportButton(icon: androidx.compose.ui.graphics.vector.ImageVector, size: Int, onClick: () -> Unit) {
    Box(Modifier.size((size + 20).dp).bounceClick(pressedScale = 0.82f, onClick = onClick), contentAlignment = Alignment.Center) {
        Icon(icon, null, tint = Color.White, modifier = Modifier.size(size.dp))
    }
}

@Composable
private fun SeekBar(pos: Double, dur: Double, onSeek: (Double) -> Unit) {
    var dragging by remember { mutableStateOf(false) }
    var frac by remember { mutableFloatStateOf(0f) }
    val live = if (dur > 0) (pos / dur).toFloat().coerceIn(0f, 1f) else 0f
    val shown = if (dragging) frac else live
    val h by animateDpAsState(if (dragging) 12.dp else 6.dp, spring(dampingRatio = 0.6f, stiffness = 500f), label = "seekH")

    Column(Modifier.fillMaxWidth()) {
        BoxWithConstraints(
            Modifier
                .fillMaxWidth()
                .height(28.dp)
                .pointerInput(dur) {
                    detectHorizontalDragGestures(
                        onDragStart = { o -> dragging = true; frac = (o.x / size.width).coerceIn(0f, 1f) },
                        onDragEnd = { dragging = false; if (dur > 0) onSeek(frac * dur) },
                        onDragCancel = { dragging = false }
                    ) { ch, d -> ch.consume(); frac = (frac + d / size.width).coerceIn(0f, 1f) }
                }
                .pointerInput(dur) {
                    detectTapGestures { o -> if (dur > 0) onSeek((o.x / size.width).coerceIn(0f, 1f) * dur) }
                },
            contentAlignment = Alignment.CenterStart
        ) {
            Box(Modifier.fillMaxWidth().height(h).clip(RoundedCornerShape(50)).background(Color.White.copy(alpha = 0.22f))) {
                Box(Modifier.fillMaxHeight().fillMaxWidth(shown).background(Color.White, RectangleShape))
            }
        }
        Row(Modifier.fillMaxWidth()) {
            Text(fmtTime(if (dragging) frac * dur else pos), color = Color.White.copy(alpha = 0.55f), fontSize = 12.sp, modifier = Modifier.weight(1f))
            Text(if (dur > 0) "-" + fmtTime(dur - (if (dragging) frac * dur else pos)) else "--:--", color = Color.White.copy(alpha = 0.55f), fontSize = 12.sp, textAlign = TextAlign.End)
        }
    }
}

@Composable
private fun EmptyMedia() {
    Column(Modifier.fillMaxWidth().padding(top = 60.dp), horizontalAlignment = Alignment.CenterHorizontally) {
        Box(Modifier.size(140.dp).clip(RoundedCornerShape(36.dp)).background(C.Card), contentAlignment = Alignment.Center) {
            Icon(Icons.Rounded.MusicNote, null, tint = C.Text3, modifier = Modifier.size(70.dp))
        }
        Text(Str.t("media.nothing"), color = C.Text, fontSize = 24.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 24.dp))
        Text(Str.t("media.nothingSub"), color = C.Text2, fontSize = 15.sp, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 8.dp, start = 12.dp, end = 12.dp))
        Spacer(Modifier.height(34.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(18.dp)) {
            TransportButton(Icons.Rounded.SkipPrevious, 34) { Connection.keys("prev") }
            TransportButton(Icons.Rounded.PlayArrow, 40) { Connection.keys("playpause") }
            TransportButton(Icons.Rounded.SkipNext, 34) { Connection.keys("next") }
        }
        Spacer(Modifier.height(34.dp))
        VolumeRow()
    }
}
