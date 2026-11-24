package com.pcpanel.app.ui.screens

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.PowerSettingsNew
import androidx.compose.material.icons.rounded.QrCodeScanner
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.R
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.ConnState
import com.pcpanel.app.net.Connection
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.bounceClick
import com.pcpanel.app.ui.theme.C

@Composable
fun AppLogo(size: androidx.compose.ui.unit.Dp, modifier: Modifier = Modifier) {
    Image(
        painterResource(R.drawable.app_logo), null,
        modifier = modifier.size(size).clip(RoundedCornerShape(size * 0.2237f))
    )
}

@Composable
fun BigButton(text: String, icon: ImageVector?, color: Color = C.Blue, filled: Boolean = true, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Row(
        modifier
            .fillMaxWidth()
            .height(56.dp)
            .bounceClick(onClick = onClick)
            .clip(RoundedCornerShape(16.dp))
            .background(if (filled) color else C.Card),
        horizontalArrangement = Arrangement.Center,
        verticalAlignment = Alignment.CenterVertically
    ) {
        if (icon != null) Icon(icon, null, tint = if (filled) Color.White else color, modifier = Modifier.size(22.dp).padding(end = 0.dp))
        if (icon != null) Spacer(Modifier.size(10.dp))
        Text(text, color = if (filled) Color.White else color, fontSize = 17.sp, fontWeight = FontWeight.SemiBold)
    }
}

// ================================================================== first run / pairing
@Composable
fun PairScreen() {
    val scan = rememberQrScanner()
    val logoIn = remember { Animatable(0.6f) }
    LaunchedEffect(Unit) { logoIn.animateTo(1f, spring(dampingRatio = 0.5f, stiffness = 160f)) }
    val float = rememberInfiniteTransition(label = "float")
    val dy by float.animateFloat(-6f, 6f, infiniteRepeatable(tween(2400), RepeatMode.Reverse), label = "dy")

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .statusBarsPadding()
            .navigationBarsPadding()
            .padding(horizontal = 28.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Spacer(Modifier.height(56.dp))
        AppLogo(124.dp, Modifier.graphicsLayer { scaleX = logoIn.value; scaleY = logoIn.value; translationY = dy })
        Spacer(Modifier.height(28.dp))
        Text(Str.t("pair.welcome"), color = C.Text, fontSize = 30.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center, lineHeight = 36.sp)
        Text(Str.t("pair.sub"), color = C.Text2, fontSize = 17.sp, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 8.dp))
        Spacer(Modifier.height(36.dp))
        Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(C.Card).padding(18.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Step(1, Str.t("pair.step1"))
            Step(2, Str.t("pair.step2"))
            Step(3, Str.t("pair.step3"))
        }
        Spacer(Modifier.height(36.dp))
        BigButton(Str.t("pair.scan"), Icons.Rounded.QrCodeScanner, onClick = scan)
        Spacer(Modifier.height(24.dp))
    }
}

@Composable
private fun Step(n: Int, text: String) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(30.dp).clip(CircleShape).background(C.Blue.copy(alpha = 0.18f)), contentAlignment = Alignment.Center) {
            Text("$n", color = C.Blue, fontSize = 15.sp, fontWeight = FontWeight.Bold)
        }
        Text(text, color = C.Text, fontSize = 15.5.sp, modifier = Modifier.padding(start = 14.dp))
    }
}

// ================================================================== not connected
@Composable
fun ConnectionScreen() {
    val state = Connection.state
    val scan = rememberQrScanner()
    val busy = state == ConnState.Connecting || state == ConnState.Waking
    val rings = rememberInfiniteTransition(label = "rings")
    val r by rings.animateFloat(0f, 1f, infiniteRepeatable(tween(1800, easing = LinearEasing)), label = "r")

    val title = when (state) {
        ConnState.Connecting -> Str.t("conn.connecting")
        ConnState.Waking -> Str.t("conn.waking")
        ConnState.Offline -> Str.t("conn.offline")
        ConnState.AuthFailed -> Str.t("conn.repair")
        else -> Str.t("conn.idle")
    }
    val color = when (state) {
        ConnState.Connecting, ConnState.Waking -> C.Blue
        ConnState.AuthFailed -> C.Orange
        ConnState.Offline -> C.Red
        else -> C.Text2
    }

    Column(
        Modifier.fillMaxSize().statusBarsPadding().navigationBarsPadding().padding(horizontal = 28.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Spacer(Modifier.weight(1f))
        Box(Modifier.size(240.dp), contentAlignment = Alignment.Center) {
            if (busy) {
                Canvas(Modifier.fillMaxSize()) {
                    for (k in 0..2) {
                        val p = (r + k / 3f) % 1f
                        drawCircle(color.copy(alpha = (1f - p) * 0.45f), radius = size.minDimension / 2 * (0.35f + p * 0.65f), style = Stroke(2.dp.toPx()))
                    }
                }
            }
            AppLogo(110.dp, Modifier.graphicsLayer { alpha = if (busy || state == ConnState.Connected) 1f else 0.55f })
        }
        Text(title, color = C.Text, fontSize = 26.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center)
        Text(Prefs.pc?.name ?: "", color = color, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 6.dp))
        if (Connection.detail.isNotBlank()) {
            Text(Connection.detail, color = C.Text2, fontSize = 15.sp, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 10.dp))
        }
        Spacer(Modifier.weight(1f))
        if (state == ConnState.AuthFailed) {
            BigButton(Str.t("pair.scan"), Icons.Rounded.QrCodeScanner, onClick = scan)
        } else {
            BigButton(Str.t("conn.connect"), Icons.Rounded.Refresh, onClick = { Connection.connect(manual = true) })
            Spacer(Modifier.height(12.dp))
            BigButton(Str.t("conn.wake"), Icons.Rounded.PowerSettingsNew, color = C.Green, filled = false, onClick = { Connection.wake() })
            Spacer(Modifier.height(12.dp))
            BigButton(Str.t("conn.repair"), Icons.Rounded.QrCodeScanner, color = C.Text2, filled = false, onClick = scan)
        }
        Spacer(Modifier.height(24.dp))
    }
}

