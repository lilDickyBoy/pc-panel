@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package com.pcpanel.app.ui.screens

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.BrightnessHigh
import androidx.compose.material.icons.rounded.DesktopAccessDisabled
import androidx.compose.material.icons.rounded.Keyboard
import androidx.compose.material.icons.rounded.NightsStay
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.SkipNext
import androidx.compose.material.icons.rounded.SkipPrevious
import androidx.compose.material.icons.rounded.VolumeDown
import androidx.compose.material.icons.rounded.VolumeOff
import androidx.compose.material.icons.rounded.VolumeUp
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.Connection
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.FatSlider
import com.pcpanel.app.ui.components.GroupCard
import com.pcpanel.app.ui.components.SectionTitle
import com.pcpanel.app.ui.components.Segmented
import com.pcpanel.app.ui.components.bounceClick
import com.pcpanel.app.ui.components.haptic
import com.pcpanel.app.ui.theme.C
import kotlinx.coroutines.launch
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.sqrt

@Composable
fun ControlScreen() {
    var mode by rememberSaveable { mutableIntStateOf(0) }
    Column(Modifier.fillMaxSize().statusBarsPadding().padding(horizontal = 16.dp)) {
        LargeHeader(Str.t("tab.control"))
        Spacer(Modifier.height(10.dp))
        Segmented(listOf(Str.t("ctl.touchpad"), Str.t("ctl.keyboard"), Str.t("ctl.screen"), Str.t("ctl.display")), mode, Modifier.fillMaxWidth()) { mode = it }
        Spacer(Modifier.height(14.dp))
        AnimatedContent(mode, transitionSpec = { fadeIn(tween(220)) togetherWith fadeOut(tween(120)) }, label = "ctl", modifier = Modifier.weight(1f)) { m ->
            when (m) {
                0 -> Touchpad()
                1 -> KeyboardPanel()
                2 -> ScreenPanel()
                else -> DisplayPanel()
            }
        }
    }
}

// ================================================================== touchpad
@Composable
private fun Touchpad() {
    val view = LocalView.current
    var touch by remember { mutableStateOf<Offset?>(null) }
    var fingers by remember { mutableIntStateOf(0) }
    val glow = remember { Animatable(0f) }
    val scope = rememberCoroutineScope()
    var lastTapUp by remember { mutableLongStateOf(0L) }

    Column(Modifier.fillMaxSize().padding(bottom = 12.dp)) {
        Box(
            Modifier
                .fillMaxWidth()
                .weight(1f)
                .clip(RoundedCornerShape(26.dp))
                .background(Brush.verticalGradient(listOf(Color(0xFF1E1E21), Color(0xFF141416))))
                .border(1.dp, Color(0xFF2C2C2E), RoundedCornerShape(26.dp))
                .pointerInput(Unit) {
                    awaitEachGesture {
                        val first = awaitFirstDown(requireUnconsumed = false)
                        val downAt = first.uptimeMillis
                        var maxPointers = 1
                        var moved = 0f
                        var dragging = false
                        touch = first.position
                        fingers = 1
                        scope.launch { glow.animateTo(1f, tween(120)) }
                        if (downAt - lastTapUp < 260) {
                            // double-tap and hold -> drag
                            dragging = true
                            Connection.button("left", true)
                            haptic(view)
                        }
                        var upAt = downAt
                        while (true) {
                            val ev = awaitPointerEvent()
                            val pressed = ev.changes.filter { it.pressed }
                            if (pressed.isEmpty()) { upAt = ev.changes.maxOf { it.uptimeMillis }; break }
                            maxPointers = max(maxPointers, pressed.size)
                            fingers = pressed.size
                            if (pressed.size == 1 && maxPointers == 1) {
                                val c = pressed[0]
                                val d = c.positionChange()
                                moved += abs(d.x) + abs(d.y)
                                val speed = sqrt(d.x * d.x + d.y * d.y)
                                val accel = (0.9f + speed / 14f).coerceAtMost(3.4f) * Prefs.touchSpeed * 1.15f
                                Connection.mouseMove(d.x * accel, d.y * accel)
                                touch = c.position
                                c.consume()
                            } else if (pressed.size >= 2) {
                                var sx = 0f; var sy = 0f
                                pressed.forEach { val d = it.positionChange(); sx += d.x; sy += d.y }
                                sx /= pressed.size; sy /= pressed.size
                                moved += abs(sx) + abs(sy)
                                Connection.scroll(sy * 2.4f, -sx * 2.4f)
                                touch = Offset(pressed.map { it.position.x }.average().toFloat(), pressed.map { it.position.y }.average().toFloat())
                                pressed.forEach { it.consume() }
                            }
                        }
                        val dur = upAt - downAt
                        if (dragging) {
                            Connection.button("left", false)
                        } else if (moved < 22f && dur < 260) {
                            if (maxPointers >= 2) { Connection.click("right"); haptic(view, true) }
                            else { Connection.click("left"); haptic(view); lastTapUp = upAt }
                        }
                        fingers = 0
                        scope.launch { glow.animateTo(0f, tween(350)) }
                    }
                }
        ) {
            Canvas(Modifier.fillMaxSize()) {
                val p = touch
                if (p != null && glow.value > 0f) {
                    drawCircle(
                        Brush.radialGradient(listOf(C.Blue.copy(alpha = 0.45f * glow.value), Color.Transparent), center = p, radius = 120f),
                        radius = 120f, center = p
                    )
                    drawCircle(Color.White.copy(alpha = 0.6f * glow.value), radius = if (fingers >= 2) 26f else 18f, center = p)
                }
            }
            Text(
                Str.t("ctl.padHint"), color = C.Text3, fontSize = 12.5.sp, textAlign = TextAlign.Center, lineHeight = 18.sp,
                modifier = Modifier.align(Alignment.BottomCenter).padding(16.dp)
            )
        }
        Spacer(Modifier.height(12.dp))
        Row(Modifier.fillMaxWidth().height(58.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            MouseButton(Modifier.weight(1f), Str.t("ctl.left")) { Connection.click("left") }
            MouseButton(Modifier.weight(0.6f), Str.t("ctl.middle")) { Connection.click("middle") }
            MouseButton(Modifier.weight(1f), Str.t("ctl.right")) { Connection.click("right") }
        }
    }
}

@Composable
private fun MouseButton(modifier: Modifier, label: String, onClick: () -> Unit) {
    Box(
        modifier.fillMaxSize().bounceClick(pressedScale = 0.94f, onClick = onClick).clip(RoundedCornerShape(18.dp)).background(C.Card),
        contentAlignment = Alignment.Center
    ) { Text(label, color = C.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold) }
}

// ================================================================== keyboard
private data class K(val label: String, val keys: List<String>, val wide: Boolean = false)

private val keyGroups = listOf(
    K("Esc", listOf("esc")), K("Tab", listOf("tab")), K("Enter ⏎", listOf("enter"), true), K("⌫", listOf("backspace")),
    K("Del", listOf("delete")), K("Space", listOf("space"), true),
    K("←", listOf("left")), K("↑", listOf("up")), K("↓", listOf("down")), K("→", listOf("right")),
    K("Home", listOf("home")), K("End", listOf("end")), K("PgUp", listOf("pgup")), K("PgDn", listOf("pgdn")),
)

private val shortcutGroups = listOf(
    K("⊞ Win", listOf("win")), K("Alt+Tab", listOf("alt", "tab")), K("Win+D", listOf("win", "d")), K("Win+E", listOf("win", "e")),
    K("Ctrl+C", listOf("ctrl", "c")), K("Ctrl+V", listOf("ctrl", "v")), K("Ctrl+X", listOf("ctrl", "x")), K("Ctrl+Z", listOf("ctrl", "z")),
    K("Ctrl+A", listOf("ctrl", "a")), K("Ctrl+S", listOf("ctrl", "s")), K("Ctrl+F", listOf("ctrl", "f")), K("Alt+F4", listOf("alt", "f4")),
    K("F5", listOf("f5")), K("F11", listOf("f11")), K("Task Mgr", listOf("ctrl", "shift", "esc")), K("PrtSc", listOf("printscreen")),
    K("Ctrl+T", listOf("ctrl", "t")), K("Ctrl+W", listOf("ctrl", "w")), K("Ctrl+Shift+T", listOf("ctrl", "shift", "t")), K("Win+L", listOf("win", "l")),
)

@Composable
private fun KeyboardPanel() {
    var tf by remember { mutableStateOf(TextFieldValue("")) }
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).imePadding()) {
        Row(
            Modifier.fillMaxWidth().clip(RoundedCornerShape(18.dp)).background(C.Card).padding(horizontal = 16.dp, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Icon(Icons.Rounded.Keyboard, null, tint = C.Blue, modifier = Modifier.size(22.dp))
            Box(Modifier.weight(1f).padding(start = 12.dp)) {
                if (tf.text.isEmpty()) Text(Str.t("ctl.typeHere"), color = C.Text3, fontSize = 15.sp)
                BasicTextField(
                    value = tf,
                    onValueChange = { nv ->
                        val old = tf.text
                        val new = nv.text
                        val common = old.commonPrefixWith(new).length
                        repeat(old.length - common) { Connection.keys("backspace") }
                        val add = new.substring(common)
                        if (add.isNotEmpty()) Connection.text(add)
                        tf = if (new.length > 60) TextFieldValue("") else nv.copy(selection = TextRange(new.length))
                    },
                    textStyle = TextStyle(color = C.Text, fontSize = 16.sp),
                    cursorBrush = SolidColor(C.Blue),
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send),
                    keyboardActions = KeyboardActions(onSend = { Connection.keys("enter"); tf = TextFieldValue("") }),
                    modifier = Modifier.fillMaxWidth()
                )
            }
        }
        SectionTitle(Str.t("ctl.keys"))
        KeyFlow(keyGroups)
        SectionTitle(Str.t("ctl.shortcuts"))
        KeyFlow(shortcutGroups)
        Spacer(Modifier.height(20.dp))
    }
}

@Composable
private fun KeyFlow(list: List<K>) {
    FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        list.forEach { k ->
            Box(
                Modifier
                    .height(46.dp)
                    .bounceClick(pressedScale = 0.9f) { Connection.keys(*k.keys.toTypedArray()) }
                    .clip(RoundedCornerShape(12.dp))
                    .background(C.Card2)
                    .padding(horizontal = if (k.wide) 26.dp else 16.dp),
                contentAlignment = Alignment.Center
            ) { Text(k.label, color = C.Text, fontSize = 14.5.sp, fontWeight = FontWeight.SemiBold) }
        }
    }
}

// ================================================================== display
@Composable
private fun DisplayPanel() {
    LaunchedEffect(Unit) { Connection.displayGet() }
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        GroupCard {
            Text(Str.t("ctl.brightness"), color = C.Text2, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(bottom = 10.dp))
            val b = Connection.brightness
            if (b == -1) {
                Text(Str.t("ctl.brightNo"), color = C.Orange, fontSize = 14.sp, lineHeight = 19.sp)
            } else {
                FatSlider(
                    value = (if (b < 0) 50 else b) / 100f, onValueChange = { }, icon = Icons.Rounded.BrightnessHigh,
                    trailingText = if (b < 0) "…" else "$b%",
                    onCommit = { Connection.setBrightness((it * 100).toInt()) }
                )
            }
            Spacer(Modifier.height(18.dp))
            Text(Str.t("ctl.night"), color = C.Text2, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(bottom = 10.dp))
            FatSlider(
                value = Connection.night / 100f, onValueChange = { }, color = C.Orange, icon = Icons.Rounded.NightsStay,
                trailingText = "${Connection.night}%",
                onCommit = { Connection.setNight((it * 100).toInt()) }
            )
        }
        Spacer(Modifier.height(12.dp))
        Row(
            Modifier.fillMaxWidth().height(58.dp).bounceClick { Connection.power("displayoff") }.clip(RoundedCornerShape(18.dp)).background(C.Card),
            horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically
        ) {
            Icon(Icons.Rounded.DesktopAccessDisabled, null, tint = C.Purple)
            Text(Str.t("act.displayOff"), color = C.Text, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 10.dp))
        }
        SectionTitle(Str.t("ctl.mediaKeys"))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            MediaKey(Modifier.weight(1f), Icons.Rounded.SkipPrevious) { Connection.keys("prev") }
            MediaKey(Modifier.weight(1f), Icons.Rounded.PlayArrow) { Connection.keys("playpause") }
            MediaKey(Modifier.weight(1f), Icons.Rounded.SkipNext) { Connection.keys("next") }
        }
        Spacer(Modifier.height(10.dp))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            MediaKey(Modifier.weight(1f), Icons.Rounded.VolumeOff) { Connection.keys("volmute") }
            MediaKey(Modifier.weight(1f), Icons.Rounded.VolumeDown) { Connection.keys("voldown") }
            MediaKey(Modifier.weight(1f), Icons.Rounded.VolumeUp) { Connection.keys("volup") }
        }
        Spacer(Modifier.height(20.dp))
    }
}

@Composable
private fun MediaKey(modifier: Modifier, icon: ImageVector, onClick: () -> Unit) {
    Box(
        modifier.height(58.dp).bounceClick(pressedScale = 0.9f, onClick = onClick).clip(RoundedCornerShape(18.dp)).background(C.Card),
        contentAlignment = Alignment.Center
    ) { Icon(icon, null, tint = C.Text, modifier = Modifier.size(26.dp)) }
}



// ================================================================== live screen
@Composable
private fun ScreenPanel() {
    var quality by androidx.compose.runtime.saveable.rememberSaveable { mutableIntStateOf(1) }
    val presets = listOf(10 to 40, 6 to 55, 3 to 75)
    androidx.compose.runtime.DisposableEffect(quality) {
        val (fps, q) = presets[quality]
        Connection.startScreen(fps, q)
        onDispose { Connection.stopScreen() }
    }
    val frame = Connection.frame
    val (w, h) = Connection.frameSize
    Column(Modifier.fillMaxSize()) {
        Box(
            Modifier.fillMaxWidth().aspectRatio(w.toFloat() / h.coerceAtLeast(1)).clip(RoundedCornerShape(16.dp)).background(C.Card)
                .pointerInput(Unit) {
                    detectTapGestures(
                        onTap = { o -> Connection.clickAt(o.x / size.width, o.y / size.height) },
                        onDoubleTap = { o -> Connection.clickAt(o.x / size.width, o.y / size.height, "left", true) },
                        onLongPress = { o -> Connection.clickAt(o.x / size.width, o.y / size.height, "right") }
                    )
                },
            contentAlignment = Alignment.Center
        ) {
            if (frame != null) androidx.compose.foundation.Image(frame, null, contentScale = androidx.compose.ui.layout.ContentScale.Fit, modifier = Modifier.fillMaxSize())
            else androidx.compose.material3.CircularProgressIndicator(color = C.Blue, strokeWidth = 3.dp, modifier = Modifier.size(34.dp))
        }
        Spacer(Modifier.height(12.dp))
        Segmented(listOf(Str.t("ctl.smooth"), Str.t("ctl.balanced"), Str.t("ctl.sharp")), quality, Modifier.fillMaxWidth()) { quality = it }
        Text(Str.t("ctl.screenHint"), color = C.Text2, fontSize = 13.sp, lineHeight = 18.sp, modifier = Modifier.padding(top = 10.dp, start = 4.dp))
    }
}
