package com.pcpanel.app.ui.screens

import android.content.Intent
import android.provider.Settings
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.AspectRatio
import androidx.compose.material.icons.rounded.Cable
import androidx.compose.material.icons.rounded.HighQuality
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.ScreenRotation
import androidx.compose.material.icons.rounded.Speed
import androidx.compose.material.icons.rounded.Stop
import androidx.compose.material.icons.rounded.TouchApp
import androidx.compose.material.icons.rounded.DeveloperMode
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.net.Connection
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.Divider
import com.pcpanel.app.ui.components.GroupCard
import com.pcpanel.app.ui.components.Hint
import com.pcpanel.app.ui.components.IconRow
import com.pcpanel.app.ui.components.IconSwitchRow
import com.pcpanel.app.ui.components.SectionTitle
import com.pcpanel.app.ui.components.Segmented
import com.pcpanel.app.ui.components.SubPage
import com.pcpanel.app.ui.components.bounceClick
import com.pcpanel.app.ui.theme.C

/** Tools → Second screen: start it, its settings, and how to let the PC control this phone. */
@Composable
fun SecondScreenPage(onBack: () -> Unit) {
    val s = Connection.pcSettings
    val st = Connection.ssState
    val ctx = LocalContext.current
    SubPage(Str.t("tools.screen"), Str.t("tab.tools"), onBack) {
        // ---------------------------------------------------------------- hero
        GroupCard(padding = 18.dp) {
            Box(Modifier.fillMaxWidth().height(150.dp), contentAlignment = Alignment.Center) { MonitorArt(st.state == "live", st.busy) }
            Text(Str.t("ss.title"), color = C.Text, fontSize = 22.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 10.dp))
            Text(Str.t("ss.subPhone"), color = C.Text2, fontSize = 14.sp, lineHeight = 19.sp, modifier = Modifier.padding(top = 4.dp))
            if (st.state != "off") {
                val (txt, col) = when (st.state) {
                    "live" -> Str.f("ss.liveShort", if (st.transport == "usb") "USB" else "Wi-Fi") to C.Green
                    "error" -> Str.t(st.error) to C.Red
                    else -> Str.t(st.step.ifBlank { "ss.step.waiting" }) to C.Blue
                }
                Row(Modifier.padding(top = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.size(9.dp).clip(CircleShape).background(col))
                    Text(txt, color = col, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 8.dp))
                }
            }
            Spacer(Modifier.height(16.dp))
            val connected = Connection.connected
            val running = st.busy
            Row(
                Modifier.fillMaxWidth().bounceClick(enabled = connected, pressedScale = 0.96f) {
                    if (running && st.state != "live") Connection.ssStop() else Connection.openSecondScreen()
                }
                    .clip(RoundedCornerShape(16.dp))
                    .background(
                        if (running && st.state != "live") Brush.horizontalGradient(listOf(C.Red, C.Pink))
                        else Brush.horizontalGradient(listOf(C.Blue, C.Indigo))
                    )
                    .padding(vertical = 15.dp),
                horizontalArrangement = androidx.compose.foundation.layout.Arrangement.Center,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(if (running && st.state != "live") Icons.Rounded.Stop else Icons.Rounded.PlayArrow, null, tint = Color.White, modifier = Modifier.size(24.dp))
                Text(
                    Str.t(if (running && st.state != "live") "ss.stop" else if (st.state == "live") "ss.open" else "ss.start"),
                    color = Color.White, fontSize = 17.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(start = 8.dp)
                )
            }
            if (!connected) Hint(Str.t("ss.needPc"), Modifier.padding(top = 6.dp))
        }

        // ---------------------------------------------------------------- settings
        SectionTitle(Str.t("ss.settings"))
        GroupCard {
            IconSwitchRow(Icons.Rounded.TouchApp, C.Blue, Str.t("ss.touch"), s.ssTouch, Str.t("ss.touchSub")) { Connection.setShared("ssTouch", it) }
            Divider()
            val qs = listOf("low", "mid", "high", "ultra")
            IconRow(Icons.Rounded.HighQuality, C.Green, Str.t("ss.quality"), Str.t("ss.qualitySub.${s.ssQuality}"), below = true) {
                Segmented(qs.map { Str.t("ss.q.$it") }, qs.indexOf(s.ssQuality).coerceAtLeast(0), Modifier.fillMaxWidth()) { Connection.setShared("ssQuality", qs[it]) }
            }
            Divider()
            IconRow(Icons.Rounded.ScreenRotation, C.Orange, Str.t("ss.orient"), below = true) {
                Segmented(listOf(Str.t("ss.landscape"), Str.t("ss.portrait")), if (s.ssOrient == "portrait") 1 else 0, Modifier.fillMaxWidth()) {
                    Connection.setShared("ssOrient", if (it == 1) "portrait" else "landscape")
                }
            }
            Divider()
            val cs = listOf("auto", "usb", "wifi")
            IconRow(Icons.Rounded.Cable, C.Indigo, Str.t("ss.conn"), Str.t("ss.connSub.${s.ssConn}"), below = true) {
                Segmented(listOf(Str.t("ss.c.auto"), "USB", "Wi-Fi"), cs.indexOf(s.ssConn).coerceAtLeast(0), Modifier.fillMaxWidth()) { Connection.setShared("ssConn", cs[it]) }
            }
            Divider()
            val sc = listOf(100, 125, 150, 175, 200)
            IconRow(Icons.Rounded.AspectRatio, C.Teal, Str.t("ss.scale"), Str.t("ss.scaleSub"), below = true) {
                Segmented(sc.map { "$it%" }, sc.indexOf(s.ssScale).coerceAtLeast(0), Modifier.fillMaxWidth()) { Connection.setShared("ssScale", sc[it]) }
            }
            Divider()
            IconSwitchRow(Icons.Rounded.Speed, C.Pink, Str.t("ss.fps"), s.ssFps, Str.t("ss.fpsSub")) { Connection.setShared("ssFps", it) }
        }
        Hint(Str.t("ss.hintPhone"))

        // ---------------------------------------------------------------- control from the PC
        SectionTitle(Str.t("pm.section"))
        GroupCard {
            Text(Str.t("pm.phoneIntro"), color = C.Text, fontSize = 15.sp, lineHeight = 20.sp, modifier = Modifier.padding(vertical = 8.dp))
            listOf("scr.usb1", "scr.usb2", "scr.usb3", "scr.usb4", "scr.usb5").forEachIndexed { i, k ->
                Row(Modifier.padding(vertical = 6.dp)) {
                    Box(Modifier.size(24.dp).clip(CircleShape).background(C.Green), contentAlignment = Alignment.Center) {
                        Text("${i + 1}", color = Color.White, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                    }
                    Text(Str.t(k), color = C.Text, fontSize = 14.sp, lineHeight = 19.sp, modifier = Modifier.padding(start = 12.dp, top = 2.dp))
                }
            }
            Spacer(Modifier.height(10.dp))
            Row(
                Modifier.fillMaxWidth().bounceClick(pressedScale = 0.96f) {
                    val dev = Settings.Global.getInt(ctx.contentResolver, Settings.Global.DEVELOPMENT_SETTINGS_ENABLED, 0) == 1
                    val i = Intent(if (dev) Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS else Settings.ACTION_DEVICE_INFO_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    try { ctx.startActivity(i) } catch (_: Exception) { try { ctx.startActivity(Intent(Settings.ACTION_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) } catch (_: Exception) { } }
                }.clip(RoundedCornerShape(14.dp)).background(C.Card2).padding(vertical = 13.dp),
                horizontalArrangement = androidx.compose.foundation.layout.Arrangement.Center,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(Icons.Rounded.DeveloperMode, null, tint = C.Green, modifier = Modifier.size(20.dp))
                Text(Str.t("pm.openDev"), color = C.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 8.dp))
            }
            Spacer(Modifier.height(6.dp))
        }
        Hint(Str.t("pm.phoneHint"))
        Spacer(Modifier.height(30.dp))
    }
}

/** A monitor and a phone; the link between them glows while connecting, both screens light up when live. */
@Composable
private fun MonitorArt(live: Boolean, busy: Boolean) {
    val t = rememberInfiniteTransition(label = "art")
    val flow by t.animateFloat(0f, 1f, infiniteRepeatable(tween(1100), RepeatMode.Restart), label = "flow")
    val glow by t.animateFloat(0.35f, 1f, infiniteRepeatable(tween(1300), RepeatMode.Reverse), label = "glow")
    Canvas(Modifier.size(width = 250.dp, height = 140.dp)) {
        val u = size.width / 250f
        val screen: (Offset, Size) -> Brush = { o, sz ->
            if (live) Brush.linearGradient(listOf(C.Blue, C.Purple), o, Offset(o.x + sz.width, o.y + sz.height))
            else Brush.linearGradient(listOf(Color(0xFF161618), Color(0xFF1E1E20)), o, Offset(o.x + sz.width, o.y + sz.height))
        }
        // monitor
        rr(Color(0xFF2C2C2E), Offset(0f, 8 * u), Size(140 * u, 92 * u), 12 * u)
        val so = Offset(6 * u, 14 * u); val ss = Size(128 * u, 80 * u)
        drawRoundRect(screen(so, ss), so, ss, CornerRadius(7 * u))
        rr(Color(0xFF3A3A3C), Offset(61 * u, 100 * u), Size(18 * u, 18 * u), 0f)
        rr(Color(0xFF3A3A3C), Offset(38 * u, 118 * u), Size(64 * u, 7 * u), 3.5f * u)
        // link dots
        for (i in 0 until 4) {
            val x = 150 * u + i * 9 * u
            val on = busy && !live && ((flow * 4).toInt() == i)
            val a = when { live -> 1f; on -> 1f; busy -> 0.35f; else -> 0.25f }
            drawCircle((if (live) C.Green else C.Blue).copy(alpha = a), 3 * u, Offset(x, 72 * u))
        }
        // phone (landscape)
        if (live) drawRoundRect(C.Blue.copy(alpha = 0.35f * glow), Offset(186 * u, 48 * u), Size(64 * u, 48 * u), CornerRadius(14 * u))
        rr(Color(0xFF2C2C2E), Offset(190 * u, 52 * u), Size(56 * u, 40 * u), 9 * u)
        val po = Offset(194 * u, 56 * u); val ps = Size(48 * u, 32 * u)
        drawRoundRect(screen(po, ps), po, ps, CornerRadius(5 * u))
    }
}

private fun DrawScope.rr(c: Color, o: Offset, s: Size, r: Float) = drawRoundRect(c, o, s, CornerRadius(r))
