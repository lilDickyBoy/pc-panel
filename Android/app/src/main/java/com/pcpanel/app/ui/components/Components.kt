package com.pcpanel.app.ui.components

import android.os.Build
import android.view.HapticFeedbackConstants
import android.view.View
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.clickable
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.ChevronRight
import androidx.compose.material.icons.rounded.Error
import androidx.compose.material.icons.rounded.Info
import androidx.compose.material.icons.rounded.Warning
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.Banner
import com.pcpanel.app.net.BannerKind
import com.pcpanel.app.ui.theme.C
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlin.math.roundToInt

// ====================================================================== haptics
fun haptic(view: View, strong: Boolean = false) {
    if (!Prefs.haptics) return
    val c = if (strong) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) HapticFeedbackConstants.CONFIRM else HapticFeedbackConstants.LONG_PRESS
    } else HapticFeedbackConstants.KEYBOARD_TAP
    view.performHapticFeedback(c)
}

// ====================================================================== bouncy press (iOS feel, no ripple)
fun Modifier.bounceClick(
    enabled: Boolean = true,
    pressedScale: Float = 0.93f,
    hapticOn: Boolean = true,
    onLongClick: (() -> Unit)? = null,
    onClick: () -> Unit,
): Modifier = composed {
    var pressed by remember { mutableStateOf(false) }
    val scale by animateFloatAsState(
        targetValue = if (pressed) pressedScale else 1f,
        animationSpec = if (pressed) spring(stiffness = Spring.StiffnessHigh) else spring(dampingRatio = 0.42f, stiffness = 420f),
        label = "bounce"
    )
    val view = LocalView.current
    val click by rememberUpdatedState(onClick)
    val long by rememberUpdatedState(onLongClick)
    this
        .graphicsLayer { scaleX = scale; scaleY = scale; alpha = if (enabled) 1f else 0.4f }
        .pointerInput(enabled) {
            if (!enabled) return@pointerInput
            detectTapGestures(
                onPress = {
                    pressed = true
                    tryAwaitRelease()
                    pressed = false
                },
                onLongPress = if (long != null) { _: Offset -> haptic(view, true); long?.invoke() } else null,
                onTap = { if (hapticOn) haptic(view); click() }
            )
        }
}

// ====================================================================== cards & titles
@Composable
fun GroupCard(modifier: Modifier = Modifier, padding: Dp = 16.dp, content: @Composable ColumnScope.() -> Unit) {
    Column(
        modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(20.dp))
            .background(C.Card)
            .padding(padding),
        content = content
    )
}

@Composable
fun SectionTitle(text: String, modifier: Modifier = Modifier, trailing: (@Composable () -> Unit)? = null) {
    Row(modifier.fillMaxWidth().padding(start = 6.dp, end = 6.dp, top = 22.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(text, color = C.Text, fontSize = 20.sp, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
        trailing?.invoke()
    }
}

@Composable
fun Divider(modifier: Modifier = Modifier) {
    Box(modifier.fillMaxWidth().height(0.6.dp).background(C.Sep))
}

// ====================================================================== ring gauge
@Composable
fun RingGauge(
    value: Float,
    color: Color,
    size: Dp,
    stroke: Dp = 10.dp,
    modifier: Modifier = Modifier,
    center: @Composable () -> Unit,
) {
    val v by animateFloatAsState(value.coerceIn(0f, 100f), spring(dampingRatio = 1f, stiffness = Spring.StiffnessVeryLow), label = "ring")
    Box(modifier.size(size), contentAlignment = Alignment.Center) {
        Canvas(Modifier.fillMaxSize()) {
            val sw = stroke.toPx()
            val inset = sw / 2
            val arcSize = Size(this.size.width - sw, this.size.height - sw)
            drawArc(color.copy(alpha = 0.18f), 135f, 270f, false, Offset(inset, inset), arcSize, style = Stroke(sw, cap = StrokeCap.Round))
            if (v > 0.3f) {
                drawArc(
                    Brush.sweepGradient(listOf(color.copy(alpha = 0.75f), color, color.copy(alpha = 0.75f))),
                    135f, 270f * v / 100f, false, Offset(inset, inset), arcSize, style = Stroke(sw, cap = StrokeCap.Round)
                )
            }
        }
        center()
    }
}

// ====================================================================== sparkline
@Composable
fun Sparkline(values: List<Float>, color: Color, modifier: Modifier = Modifier, max: Float = 100f) {
    Canvas(modifier) {
        if (values.size < 2) return@Canvas
        val w = size.width; val h = size.height
        val step = w / (values.size - 1)
        val line = Path()
        values.forEachIndexed { i, raw ->
            val x = i * step
            val y = h - (raw.coerceIn(0f, max) / max) * h
            if (i == 0) line.moveTo(x, y) else {
                val px = (i - 1) * step
                val py = h - (values[i - 1].coerceIn(0f, max) / max) * h
                line.cubicTo(px + step / 2, py, x - step / 2, y, x, y)
            }
        }
        val fill = Path().apply { addPath(line); lineTo(w, h); lineTo(0f, h); close() }
        drawPath(fill, Brush.verticalGradient(listOf(color.copy(alpha = 0.35f), color.copy(alpha = 0f))))
        drawPath(line, color, style = Stroke(2.2.dp.toPx(), cap = StrokeCap.Round))
    }
}

// ====================================================================== iOS switch
@Composable
fun IosSwitch(checked: Boolean, onChange: (Boolean) -> Unit) {
    val view = LocalView.current
    val bg by animateColorAsState(if (checked) C.Green else Color(0xFF39393D), tween(200), label = "sw")
    val x by animateDpAsState(if (checked) 22.dp else 2.dp, spring(dampingRatio = 0.62f, stiffness = 500f), label = "swx")
    Box(
        Modifier
            .size(51.dp, 31.dp)
            .clip(RoundedCornerShape(16.dp))
            .background(bg)
            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { haptic(view); onChange(!checked) }
    ) {
        Box(Modifier.offset(x = x, y = 2.dp).size(27.dp).clip(CircleShape).background(Color.White))
    }
}

@Composable
fun SwitchRow(title: String, checked: Boolean, subtitle: String? = null, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f).padding(end = 12.dp)) {
            Text(title, color = C.Text, fontSize = 16.sp)
            if (subtitle != null) Text(subtitle, color = C.Text2, fontSize = 12.5.sp, lineHeight = 16.sp, modifier = Modifier.padding(top = 2.dp))
        }
        IosSwitch(checked, onChange)
    }
}

// ====================================================================== segmented control
@Composable
fun Segmented(options: List<String>, selected: Int, modifier: Modifier = Modifier, onSelect: (Int) -> Unit) {
    val view = LocalView.current
    BoxWithConstraints(
        modifier
            .height(36.dp)
            .clip(RoundedCornerShape(10.dp))
            .background(C.Card2)
            .padding(2.dp)
    ) {
        val segW = maxWidth / options.size
        val x by animateDpAsState(segW * selected, spring(dampingRatio = 0.75f, stiffness = 420f), label = "seg")
        Box(
            Modifier
                .offset(x = x)
                .width(segW)
                .fillMaxHeight()
                .clip(RoundedCornerShape(8.dp))
                .background(Color(0xFF636366))
        )
        Row(Modifier.fillMaxSize()) {
            options.forEachIndexed { i, label ->
                Box(
                    Modifier
                        .weight(1f)
                        .fillMaxHeight()
                        .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) {
                            if (i != selected) { haptic(view); onSelect(i) }
                        },
                    contentAlignment = Alignment.Center
                ) {
                    Text(label, color = C.Text, fontSize = 13.5.sp, fontWeight = if (i == selected) FontWeight.SemiBold else FontWeight.Medium, maxLines = 1)
                }
            }
        }
    }
}

// ====================================================================== fat slider (Control Center style)
@Composable
fun FatSlider(
    value: Float,
    onValueChange: (Float) -> Unit,
    modifier: Modifier = Modifier,
    color: Color = Color.White,
    height: Dp = 54.dp,
    icon: ImageVector? = null,
    trailingText: String? = null,
    onCommit: (Float) -> Unit,
) {
    var local by remember { mutableFloatStateOf(value) }
    var dragging by remember { mutableStateOf(false) }
    var lastSent by remember { mutableLongStateOf(0L) }
    LaunchedEffect(value) { if (!dragging) local = value }
    val shown by animateFloatAsState(local, if (dragging) spring(stiffness = Spring.StiffnessHigh) else spring(dampingRatio = 0.9f, stiffness = 300f), label = "fat")
    val grow by animateFloatAsState(if (dragging) 1.06f else 1f, spring(dampingRatio = 0.5f, stiffness = 500f), label = "fatScale")
    val commit by rememberUpdatedState(onCommit)
    val change by rememberUpdatedState(onValueChange)

    BoxWithConstraints(
        modifier
            .fillMaxWidth()
            .height(height)
            .graphicsLayer { scaleY = grow; scaleX = 1f + (grow - 1f) * 0.3f }
            .clip(RoundedCornerShape(16.dp))
            .background(C.Card2)
            .pointerInput(Unit) {
                detectHorizontalDragGestures(
                    onDragStart = { dragging = true },
                    onDragEnd = { dragging = false; commit(local) },
                    onDragCancel = { dragging = false; commit(local) },
                ) { ch, d ->
                    ch.consume()
                    local = (local + d / size.width).coerceIn(0f, 1f)
                    change(local)
                    val now = System.currentTimeMillis()
                    if (now - lastSent > 60) { lastSent = now; commit(local) }
                }
            }
    ) {
        Box(Modifier.fillMaxHeight().width(maxWidth * shown).background(color))
        Row(Modifier.fillMaxSize().padding(horizontal = 16.dp), verticalAlignment = Alignment.CenterVertically) {
            if (icon != null) Icon(icon, null, tint = if (shown > 0.1f) Color.Black.copy(alpha = 0.75f) else C.Text2, modifier = Modifier.size(22.dp))
            Spacer(Modifier.weight(1f))
            if (trailingText != null) Text(trailingText, color = if (shown > 0.88f) Color.Black.copy(alpha = 0.7f) else C.Text2, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
        }
    }
}

// ====================================================================== slide to confirm
@Composable
fun SlideToConfirm(text: String, color: Color, icon: ImageVector, onConfirm: () -> Unit) {
    val view = LocalView.current
    val scope = rememberCoroutineScope()
    val offset = remember { Animatable(0f) }
    var done by remember { mutableStateOf(false) }
    val shimmer = rememberInfiniteTransition(label = "shimmer")
    val sx by shimmer.animateFloat(-300f, 900f, infiniteRepeatable(tween(2200, easing = LinearEasing), RepeatMode.Restart), label = "sx")

    BoxWithConstraints(
        Modifier
            .fillMaxWidth()
            .height(64.dp)
            .clip(RoundedCornerShape(32.dp))
            .background(C.Card2)
    ) {
        val thumb = 56.dp
        val density = LocalDensity.current
        val maxPx = with(density) { (maxWidth - thumb - 8.dp).toPx() }
        val progress = if (maxPx > 0) offset.value / maxPx else 0f
        Text(
            text,
            modifier = Modifier.align(Alignment.Center).padding(start = 48.dp).alpha((1f - progress * 1.6f).coerceIn(0f, 1f)),
            style = TextStyle(
                brush = Brush.linearGradient(
                    listOf(C.Text2, Color.White, C.Text2),
                    start = Offset(sx, 0f), end = Offset(sx + 260f, 0f)
                ),
                fontSize = 17.sp, fontWeight = FontWeight.Medium
            )
        )
        Box(
            Modifier
                .padding(4.dp)
                .width(thumb + with(density) { offset.value.toDp() })
                .fillMaxHeight()
                .clip(RoundedCornerShape(28.dp))
                .background(color.copy(alpha = 0.28f))
        )
        Box(
            Modifier
                .padding(4.dp)
                .offset { IntOffset(offset.value.roundToInt(), 0) }
                .size(thumb)
                .clip(CircleShape)
                .background(color)
                .pointerInput(maxPx) {
                    detectHorizontalDragGestures(
                        onDragEnd = {
                            scope.launch {
                                if (!done && offset.value > maxPx * 0.82f) {
                                    done = true
                                    offset.animateTo(maxPx, spring(stiffness = Spring.StiffnessMedium))
                                    haptic(view, true)
                                    onConfirm()
                                } else offset.animateTo(0f, spring(dampingRatio = 0.55f, stiffness = 300f))
                            }
                        }
                    ) { ch, d ->
                        ch.consume()
                        if (!done) scope.launch { offset.snapTo((offset.value + d).coerceIn(0f, maxPx)) }
                    }
                },
            contentAlignment = Alignment.Center
        ) { Icon(icon, null, tint = Color.White, modifier = Modifier.size(26.dp)) }
    }
}

// ====================================================================== dialog (spring pop)
@Composable
fun IosDialog(visible: Boolean, onDismiss: () -> Unit, content: @Composable ColumnScope.() -> Unit) {
    androidx.compose.animation.AnimatedVisibility(visible, enter = fadeIn(tween(180)), exit = fadeOut(tween(160))) {
        Box(
            Modifier
                .fillMaxSize()
                .background(Color.Black.copy(alpha = 0.6f))
                .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { onDismiss() },
            contentAlignment = Alignment.Center
        ) {
            val pop = remember { Animatable(0.82f) }
            LaunchedEffect(visible) {
                pop.animateTo(if (visible) 1f else 0.92f, if (visible) spring(dampingRatio = 0.62f, stiffness = 380f) else tween(140))
            }
            Column(
                Modifier
                    .graphicsLayer { scaleX = pop.value; scaleY = pop.value }
                    .padding(28.dp)
                    .widthIn(max = 420.dp)
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(28.dp))
                    .background(C.Card)
                    .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { }
                    .padding(20.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                content = content
            )
        }
    }
}

@Composable
fun DialogButton(text: String, color: Color = C.Blue, filled: Boolean = false, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Box(
        modifier
            .fillMaxWidth()
            .height(50.dp)
            .bounceClick(onClick = onClick)
            .clip(RoundedCornerShape(14.dp))
            .background(if (filled) color else C.Card2),
        contentAlignment = Alignment.Center
    ) { Text(text, color = if (filled) Color.White else color, fontSize = 16.sp, fontWeight = FontWeight.SemiBold) }
}

// ====================================================================== banner
@Composable
fun BannerHost(banner: Banner?) {
    var current by remember { mutableStateOf<Banner?>(null) }
    var visible by remember { mutableStateOf(false) }
    LaunchedEffect(banner?.id) {
        if (banner != null) {
            current = banner
            visible = true
            delay(2800)
            visible = false
        }
    }
    androidx.compose.animation.AnimatedVisibility(
        visible,
        enter = slideInVertically(spring(dampingRatio = 0.7f, stiffness = 380f)) { -it * 2 } + fadeIn(),
        exit = slideOutVertically(tween(220)) { -it * 2 } + fadeOut(tween(220)),
        modifier = Modifier.statusBarsPadding()
    ) {
        val b = current ?: return@AnimatedVisibility
        val (icon, tint) = when (b.kind) {
            BannerKind.Success -> Icons.Rounded.CheckCircle to C.Green
            BannerKind.Error -> Icons.Rounded.Error to C.Red
            BannerKind.Warning -> Icons.Rounded.Warning to C.Orange
            BannerKind.Info -> Icons.Rounded.Info to C.Blue
        }
        Box(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), contentAlignment = Alignment.TopCenter) {
            Row(
                Modifier
                    .clip(RoundedCornerShape(22.dp))
                    .background(Color(0xFF242426))
                    .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { visible = false }
                    .padding(horizontal = 16.dp, vertical = 12.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(icon, null, tint = tint, modifier = Modifier.size(22.dp))
                Text(b.text, color = C.Text, fontSize = 14.5.sp, fontWeight = FontWeight.Medium, modifier = Modifier.padding(start = 10.dp), maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

// ====================================================================== list row
@Composable
fun NavRow(icon: ImageVector, iconBg: Color, title: String, value: String? = null, onClick: (() -> Unit)? = null) {
    Row(
        Modifier
            .fillMaxWidth()
            .then(if (onClick != null) Modifier.bounceClick(pressedScale = 0.97f, onClick = onClick) else Modifier)
            .padding(vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(Modifier.size(30.dp).clip(RoundedCornerShape(8.dp)).background(iconBg), contentAlignment = Alignment.Center) {
            Icon(icon, null, tint = Color.White, modifier = Modifier.size(18.dp))
        }
        Text(title, color = C.Text, fontSize = 16.sp, modifier = Modifier.weight(1f).padding(start = 12.dp))
        if (value != null) Text(value, color = C.Text2, fontSize = 15.sp, textAlign = TextAlign.End)
        if (onClick != null) Icon(Icons.Rounded.ChevronRight, null, tint = C.Text3)
    }
}

@Composable
fun Pill(text: String, color: Color, modifier: Modifier = Modifier) {
    Row(
        modifier.clip(RoundedCornerShape(50)).background(color.copy(alpha = 0.16f)).padding(horizontal = 10.dp, vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp)
    ) {
        Box(Modifier.size(7.dp).clip(CircleShape).background(color))
        Text(text, color = color, fontSize = 12.5.sp, fontWeight = FontWeight.SemiBold)
    }
}
