package com.pcpanel.app.ui.screens

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ChevronRight
import androidx.compose.material.icons.rounded.ShowChart
import androidx.compose.material.icons.rounded.ArrowDownward
import androidx.compose.material.icons.rounded.ArrowUpward
import androidx.compose.material.icons.rounded.Bedtime
import androidx.compose.material.icons.rounded.DesktopAccessDisabled
import androidx.compose.material.icons.rounded.Fullscreen
import androidx.compose.material.icons.rounded.Info
import androidx.compose.material.icons.rounded.LinkOff
import androidx.compose.material.icons.rounded.Lock
import androidx.compose.material.icons.rounded.Memory
import androidx.compose.material.icons.rounded.PowerSettingsNew
import androidx.compose.material.icons.rounded.RestartAlt
import androidx.compose.material.icons.rounded.Screenshot
import androidx.compose.material.icons.rounded.Storage
import androidx.compose.material.icons.rounded.Timer
import androidx.compose.material.icons.rounded.Toys
import androidx.compose.material.icons.rounded.DeveloperBoard
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.BannerKind
import com.pcpanel.app.net.ConnState
import com.pcpanel.app.net.Connection
import com.pcpanel.app.net.fmtBytes
import com.pcpanel.app.net.fmtSpeed
import com.pcpanel.app.net.fmtTime
import com.pcpanel.app.net.fmtUptime
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.GroupCard
import com.pcpanel.app.ui.components.Pill
import com.pcpanel.app.ui.components.RingGauge
import com.pcpanel.app.ui.components.SectionTitle
import com.pcpanel.app.ui.components.Segmented
import com.pcpanel.app.ui.components.Sparkline
import com.pcpanel.app.ui.components.bounceClick
import com.pcpanel.app.ui.theme.C
import kotlinx.coroutines.delay
import kotlin.math.roundToInt

@Composable
fun LargeHeader(title: String, subtitle: (@Composable () -> Unit)? = null, actions: (@Composable () -> Unit)? = null) {
    Row(Modifier.fillMaxWidth().padding(top = 12.dp, bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(title, color = C.Text, fontSize = 32.sp, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            subtitle?.invoke()
        }
        actions?.invoke()
    }
}

@Composable
fun CircleIconButton(icon: ImageVector, tint: Color = C.Text, bg: Color = C.Card, size: Int = 40, onClick: () -> Unit) {
    Box(
        Modifier.size(size.dp).bounceClick(onClick = onClick).clip(CircleShape).background(bg),
        contentAlignment = Alignment.Center
    ) { Icon(icon, null, tint = tint, modifier = Modifier.size((size * 0.52f).dp)) }
}

@Composable
fun HomeScreen() {
    val s = Connection.stats
    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .statusBarsPadding()
            .padding(horizontal = 16.dp)
    ) {
        LargeHeader(
            title = Prefs.pc?.name ?: "PC",
            subtitle = {
                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 6.dp)) {
                    val connected = Connection.state == ConnState.Connected
                    Pill(if (connected) Str.t("conn.connected") else Str.t("conn.connecting"), if (connected) C.Green else C.Orange)
                    if (Connection.inGame) { Spacer(Modifier.width(6.dp)); Pill(Str.t("home.inGame"), C.Purple) }
                    if (Connection.hasStats) Text("  ${Str.t("home.uptime")} ${fmtUptime(s.uptime)}", color = C.Text2, fontSize = 13.sp)
                }
            },
            actions = {
                CircleIconButton(Icons.Rounded.Fullscreen) { UiState.dashboard = true }
                Spacer(Modifier.width(10.dp))
                CircleIconButton(Icons.Rounded.LinkOff, tint = C.Red, bg = C.Red.copy(alpha = 0.15f)) { Connection.disconnect() }
            }
        )

        if (!Connection.hasStats) {
            GroupCard(Modifier.padding(top = 16.dp)) {
                Text(Str.t("home.waiting"), color = C.Text2, fontSize = 15.sp, modifier = Modifier.padding(8.dp))
            }
        }

        // ------------------------------------------------ gauges
        GroupCard(Modifier.padding(top = 16.dp), padding = 14.dp) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
                Gauge(Str.t("home.cpu"), s.cpuLoad, C.Blue, if (s.cpuTemp >= 0) "${s.cpuTemp.roundToInt()}°C" else "—")
                Gauge(Str.t("home.gpu"), s.gpuLoad, C.Green, if (s.gpuTemp >= 0) "${s.gpuTemp.roundToInt()}°C" else "—")
                Gauge(Str.t("home.ram"), s.ramPct, C.Purple, String.format("%.1f/%.0f GB", s.ramUsed / 1e9, s.ramTotal / 1e9))
            }
        }

        if (s.limited && Connection.hasStats) {
            Row(
                Modifier.fillMaxWidth().padding(top = 10.dp).clip(RoundedCornerShape(16.dp)).background(C.Orange.copy(alpha = 0.12f)).padding(12.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(Icons.Rounded.Info, null, tint = C.Orange, modifier = Modifier.size(20.dp))
                Text(Str.t("home.limited"), color = C.Orange, fontSize = 13.sp, modifier = Modifier.padding(start = 10.dp), lineHeight = 17.sp)
            }
        }

        // ------------------------------------------------ temperatures
        SectionTitle(Str.t("home.temps"))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            TempCard(
                Modifier.weight(1f), "CPU", Icons.Rounded.Memory, s.cpuTemp, s.cpuName,
                listOfNotNull(
                    if (s.cpuClock > 0) String.format("%.2f GHz", s.cpuClock / 1000f) else null,
                    if (s.cpuPower > 0) "${s.cpuPower.roundToInt()} W" else null
                )
            )
            TempCard(
                Modifier.weight(1f), "GPU", Icons.Rounded.DeveloperBoard, s.gpuTemp, s.gpuName,
                listOfNotNull(
                    if (s.gpuHot > 0) "${Str.t("home.hotspot")} ${s.gpuHot.roundToInt()}°" else null,
                    if (s.gpuPower > 0) "${s.gpuPower.roundToInt()} W" else null,
                    if (s.gpuMemTotal > 0) String.format("%s %.1f/%.0f GB", Str.t("home.vram"), s.gpuMemUsed / 1024f, s.gpuMemTotal / 1024f) else null
                )
            )
        }

        // ------------------------------------------------ activity
        SectionTitle(Str.t("home.activity"))
        GroupCard {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Legend("CPU", C.Blue, "${s.cpuLoad.roundToInt()}%")
                Spacer(Modifier.width(18.dp))
                Legend("GPU", C.Green, "${s.gpuLoad.roundToInt()}%")
            }
            Box(Modifier.fillMaxWidth().height(100.dp).padding(top = 12.dp)) {
                Sparkline(Connection.cpuHist, C.Blue, Modifier.fillMaxSize())
                Sparkline(Connection.gpuHist, C.Green, Modifier.fillMaxSize())
            }
        }

        // ------------------------------------------------ temperature history shortcut
        Row(
            Modifier.fillMaxWidth().padding(top = 12.dp).bounceClick(pressedScale = 0.97f) { UiState.toolPage = "history"; UiState.tab = 3 }
                .clip(RoundedCornerShape(16.dp)).background(C.Card).padding(14.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            com.pcpanel.app.ui.components.IconTile(Icons.Rounded.ShowChart, C.Red)
            Text(Str.t("home.history"), color = C.Text, fontSize = 15.5.sp, modifier = Modifier.weight(1f).padding(start = 12.dp))
            Icon(Icons.Rounded.ChevronRight, null, tint = C.Text3)
        }

        // ------------------------------------------------ fans
        SectionTitle(Str.t("home.fans"), trailing = {
            Text(Str.t("home.fanControl"), color = C.Blue, fontSize = 15.sp, fontWeight = FontWeight.SemiBold,
                modifier = Modifier.bounceClick { UiState.toolPage = "fans"; UiState.tab = 3 }.padding(4.dp))
        })
        GroupCard {
            if (s.gpuFan >= 0 || s.gpuFanPct >= 0) {
                FanRow(Str.t("home.gpuFan"), s.gpuFan.roundToInt(), if (s.gpuFan <= 0 && s.gpuFanPct <= 0) Str.t("home.fanIdle") else null)
            }
            s.fans.forEachIndexed { i, f ->
                if (i > 0 || s.gpuFan >= 0) Box(Modifier.fillMaxWidth().height(0.6.dp).background(C.Sep))
                FanRow(f.name, f.rpm, null)
            }
            if (s.fans.isEmpty() && s.gpuFan < 0) Text(Str.t("home.noFans"), color = C.Text2, fontSize = 14.sp, modifier = Modifier.padding(vertical = 6.dp))
        }

        // ------------------------------------------------ network
        SectionTitle(Str.t("home.network"))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            NetCard(Modifier.weight(1f), Str.t("home.down"), Icons.Rounded.ArrowDownward, C.Blue, fmtSpeed(s.netDown))
            NetCard(Modifier.weight(1f), Str.t("home.up"), Icons.Rounded.ArrowUpward, C.Green, fmtSpeed(s.netUp))
        }

        // ------------------------------------------------ disks
        if (s.disks.isNotEmpty()) {
            SectionTitle(Str.t("home.disks"))
            GroupCard {
                s.disks.forEachIndexed { i, d ->
                    if (i > 0) Spacer(Modifier.height(14.dp))
                    val pct = if (d.total > 0) d.used.toFloat() / d.total else 0f
                    val col = when { pct > 0.9f -> C.Red; pct > 0.75f -> C.Orange; else -> C.Teal }
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Rounded.Storage, null, tint = col, modifier = Modifier.size(20.dp))
                        Text("${d.name} ${d.label}".trim(), color = C.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 8.dp).weight(1f))
                        Text("${fmtBytes(d.total - d.used)} ${Str.t("home.free")}", color = C.Text2, fontSize = 13.sp)
                    }
                    Bar(pct, col, Modifier.padding(top = 8.dp))
                }
            }
        }

        // ------------------------------------------------ quick actions
        SectionTitle(Str.t("home.quick"))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            ActionTile(Modifier.weight(1f), Icons.Rounded.Lock, C.Blue, Str.t("act.lock")) {
                Connection.power("lock"); Connection.showBanner(Str.t("act.locked"), BannerKind.Success)
            }
            ActionTile(Modifier.weight(1f), Icons.Rounded.Screenshot, C.Teal, Str.t("act.shot")) {
                Connection.screenshotReq(); Connection.showBanner(Str.t("shot.taking"))
            }
            ActionTile(Modifier.weight(1f), Icons.Rounded.DesktopAccessDisabled, C.Purple, Str.t("act.displayOff")) {
                Connection.power("displayoff")
            }
        }
        Spacer(Modifier.height(12.dp))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            ActionTile(Modifier.weight(1f), Icons.Rounded.Bedtime, C.Indigo, Str.t("act.sleep")) { UiState.powerConfirm = "sleep" }
            ActionTile(Modifier.weight(1f), Icons.Rounded.RestartAlt, C.Orange, Str.t("act.restart")) { UiState.powerConfirm = "restart" }
            ActionTile(Modifier.weight(1f), Icons.Rounded.PowerSettingsNew, C.Red, Str.t("act.shutdown")) { UiState.powerConfirm = "shutdown" }
        }

        // ------------------------------------------------ timer
        SectionTitle(Str.t("timer.title"))
        TimerCard()

        Spacer(Modifier.height(28.dp))
    }
}

@Composable
private fun Gauge(label: String, value: Float, color: Color, sub: String) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        RingGauge(value, color, 96.dp, stroke = 10.dp) {
            val shown by animateFloatAsState(value, spring(stiffness = Spring.StiffnessVeryLow), label = "g")
            Text("${shown.roundToInt()}%", color = C.Text, fontSize = 21.sp, fontWeight = FontWeight.Bold)
        }
        Text(label, color = C.Text, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 2.dp))
        Text(sub, color = C.Text2, fontSize = 12.sp)
    }
}

@Composable
private fun TempCard(modifier: Modifier, title: String, icon: ImageVector, temp: Float, name: String, lines: List<String>) {
    val col by animateColorAsState(C.temp(temp), tween(600), label = "tc")
    val shown by animateFloatAsState(temp.coerceAtLeast(0f), spring(stiffness = Spring.StiffnessVeryLow), label = "tv")
    Column(modifier.clip(RoundedCornerShape(20.dp)).background(C.Card).padding(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(icon, null, tint = col, modifier = Modifier.size(18.dp))
            Text(title, color = C.Text2, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 6.dp))
        }
        Text(if (temp >= 0) "${shown.roundToInt()}°" else "—", color = col, fontSize = 44.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 4.dp))
        if (name.isNotBlank()) Text(name.replace("NVIDIA ", "").replace("GeForce ", ""), color = C.Text2, fontSize = 11.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
        lines.forEach { Text(it, color = C.Text, fontSize = 13.sp, modifier = Modifier.padding(top = 3.dp)) }
    }
}

@Composable
private fun Legend(name: String, color: Color, value: String) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(9.dp).clip(CircleShape).background(color))
        Text(name, color = C.Text2, fontSize = 13.sp, modifier = Modifier.padding(start = 6.dp))
        Text(" $value", color = C.Text, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
    }
}

@Composable
private fun FanRow(name: String, rpm: Int, note: String?) {
    Row(Modifier.fillMaxWidth().padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
        SpinningFan(rpm)
        Text(name, color = C.Text, fontSize = 15.sp, modifier = Modifier.padding(start = 12.dp).weight(1f))
        Text(note ?: "$rpm RPM", color = if (note != null) C.Text2 else C.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
    }
}

/** The icon spins at a speed proportional to the real fan RPM. */
@Composable
private fun SpinningFan(rpm: Int) {
    var angle by remember { mutableFloatStateOf(0f) }
    val speed by rememberUpdatedState(rpm)
    LaunchedEffect(rpm > 0) {
        if (rpm <= 0) return@LaunchedEffect
        var last = 0L
        while (true) {
            withFrameNanos { t ->
                if (last != 0L) angle = (angle + (t - last) / 1e9f * speed / 1000f * 360f) % 360f
                last = t
            }
        }
    }
    Icon(Icons.Rounded.Toys, null, tint = if (rpm > 0) C.Teal else C.Text3, modifier = Modifier.size(26.dp).rotate(angle))
}

@Composable
private fun NetCard(modifier: Modifier, title: String, icon: ImageVector, color: Color, value: String) {
    Row(modifier.clip(RoundedCornerShape(20.dp)).background(C.Card).padding(14.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(36.dp).clip(CircleShape).background(color.copy(alpha = 0.16f)), contentAlignment = Alignment.Center) {
            Icon(icon, null, tint = color, modifier = Modifier.size(20.dp))
        }
        Column(Modifier.padding(start = 10.dp)) {
            Text(title, color = C.Text2, fontSize = 12.sp)
            Text(value, color = C.Text, fontSize = 15.sp, fontWeight = FontWeight.Bold, maxLines = 1)
        }
    }
}

@Composable
fun Bar(fraction: Float, color: Color, modifier: Modifier = Modifier, height: Int = 8) {
    val f by animateFloatAsState(fraction.coerceIn(0f, 1f), spring(stiffness = Spring.StiffnessLow), label = "bar")
    Box(modifier.fillMaxWidth().height(height.dp).clip(RoundedCornerShape(50)).background(C.Card2)) {
        Box(Modifier.fillMaxHeight().fillMaxWidth(f).clip(RoundedCornerShape(50)).background(color))
    }
}

@Composable
private fun ActionTile(modifier: Modifier, icon: ImageVector, color: Color, label: String, onClick: () -> Unit) {
    Column(
        modifier
            .aspectRatio(1.05f)
            .bounceClick(onClick = onClick)
            .clip(RoundedCornerShape(20.dp))
            .background(C.Card)
            .padding(12.dp),
        verticalArrangement = Arrangement.SpaceBetween
    ) {
        Box(Modifier.size(42.dp).clip(CircleShape).background(color), contentAlignment = Alignment.Center) {
            Icon(icon, null, tint = Color.White, modifier = Modifier.size(23.dp))
        }
        Text(label, color = C.Text, fontSize = 13.5.sp, fontWeight = FontWeight.SemiBold, maxLines = 2, lineHeight = 16.sp)
    }
}

@Composable
private fun TimerCard() {
    val t = Connection.timer
    var tick by remember { mutableLongStateOf(0L) }
    LaunchedEffect(t.active, t.receivedAt) {
        while (t.active) { delay(1000); tick++ }
    }
    var action by remember { mutableIntStateOf(0) }
    GroupCard {
        AnimatedContent(t.active, transitionSpec = { fadeIn(tween(250)) togetherWith fadeOut(tween(150)) }, label = "timer") { active ->
            if (active) {
                @Suppress("UNUSED_VARIABLE") val dummy = tick
                val rem = t.remainingNow()
                Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
                    Icon(if (t.action == "shutdown") Icons.Rounded.PowerSettingsNew else Icons.Rounded.Bedtime, null, tint = if (t.action == "shutdown") C.Red else C.Indigo, modifier = Modifier.size(28.dp))
                    Text(Str.t(if (t.action == "shutdown") "timer.shutdownIn" else "timer.sleepIn"), color = C.Text2, fontSize = 14.sp, modifier = Modifier.padding(top = 6.dp))
                    Text(fmtTime(rem.toDouble()), color = C.Text, fontSize = 46.sp, fontWeight = FontWeight.Bold)
                    Spacer(Modifier.height(8.dp))
                    Box(
                        Modifier.fillMaxWidth().height(46.dp).bounceClick { Connection.cancelTimer() }.clip(RoundedCornerShape(14.dp)).background(C.Card2),
                        contentAlignment = Alignment.Center
                    ) { Text(Str.t("timer.cancel"), color = C.Red, fontSize = 16.sp, fontWeight = FontWeight.SemiBold) }
                }
            } else {
                Column {
                    Segmented(listOf(Str.t("act.sleep"), Str.t("act.shutdown")), action, Modifier.fillMaxWidth()) { action = it }
                    Spacer(Modifier.height(12.dp))
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        listOf(15, 30, 60, 90, 120).forEach { m ->
                            Box(
                                Modifier.weight(1f).height(44.dp)
                                    .bounceClick {
                                        Connection.startTimer(if (action == 1) "shutdown" else "sleep", m)
                                        Connection.showBanner(Str.t("timer.set"), BannerKind.Success)
                                    }
                                    .clip(RoundedCornerShape(12.dp)).background(C.Card2),
                                contentAlignment = Alignment.Center
                            ) {
                                Text(if (m < 60) "${m}m" else if (m % 60 == 0) "${m / 60}h" else "${m / 60}.5h", color = C.Text, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
                            }
                        }
                    }
                    Row(Modifier.padding(top = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Rounded.Timer, null, tint = C.Text3, modifier = Modifier.size(16.dp))
                        Text(" ${Str.t("timer.title")}", color = C.Text3, fontSize = 12.sp)
                    }
                }
            }
        }
    }
}

