package com.pcpanel.app.ui.screens

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Bedtime
import androidx.compose.material.icons.rounded.Language
import androidx.compose.material.icons.rounded.Lock
import androidx.compose.material.icons.rounded.LockOpen
import androidx.compose.material.icons.rounded.Login
import androidx.compose.material.icons.rounded.PowerSettingsNew
import androidx.compose.material.icons.rounded.Shield
import androidx.compose.material.icons.rounded.WarningAmber
import androidx.compose.material.icons.rounded.WbSunny
import androidx.compose.material.icons.rounded.Info
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.ActivityItem
import com.pcpanel.app.net.Connection
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.GroupCard
import com.pcpanel.app.ui.components.Hint
import com.pcpanel.app.ui.components.IconSwitchRow
import com.pcpanel.app.ui.components.SubPage
import com.pcpanel.app.ui.components.bounceClick
import com.pcpanel.app.ui.theme.C
import kotlinx.coroutines.delay
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale

private data class ActStyle(val icon: ImageVector, val color: Color)

private fun styleOf(kind: String) = when (kind) {
    "boot" -> ActStyle(Icons.Rounded.PowerSettingsNew, Color(0xFF30D158))
    "logon" -> ActStyle(Icons.Rounded.Login, Color(0xFF0A84FF))
    "unlock" -> ActStyle(Icons.Rounded.LockOpen, Color(0xFFFF9F0A))
    "lock" -> ActStyle(Icons.Rounded.Lock, Color(0xFF8E8E93))
    "wake" -> ActStyle(Icons.Rounded.WbSunny, Color(0xFFFFD60A))
    "sleep" -> ActStyle(Icons.Rounded.Bedtime, Color(0xFF5E5CE6))
    "failed" -> ActStyle(Icons.Rounded.WarningAmber, Color(0xFFFF453A))
    "remote" -> ActStyle(Icons.Rounded.Language, Color(0xFFBF5AF2))
    "shutdown" -> ActStyle(Icons.Rounded.PowerSettingsNew, Color(0xFF8E8E93))
    else -> ActStyle(Icons.Rounded.Info, Color(0xFF8E8E93))
}

@Composable
fun ActivityPage(onBack: () -> Unit) {
    val ps = Connection.pcSettings
    val items = Connection.activity.sortedByDescending { it.time }
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(Unit) {
        Connection.cmd("activityGet")
        while (true) { delay(30_000); now = System.currentTimeMillis() }
    }
    val locale = Locale(Prefs.lang.ifEmpty { "en" })
    val time = SimpleDateFormat("HH:mm", locale)
    val dayFmt = SimpleDateFormat("d MMMM EEEE", locale)

    SubPage(Str.t("tools.activity"), Str.t("tab.tools"), onBack, actions = {
        if (items.isNotEmpty()) Text(
            Str.t("log.clear"), color = C.Red, fontSize = 16.sp,
            modifier = Modifier.bounceClick { Connection.cmd("activityClear") }.padding(8.dp)
        )
    }) {
        // summary card: last time the PC was opened
        val lastOpen = items.firstOrNull { it.kind in setOf("unlock", "logon", "boot", "wake") }
        Box(
            Modifier.fillMaxWidth().padding(top = 8.dp).clip(RoundedCornerShape(22.dp))
                .background(androidx.compose.ui.graphics.Brush.linearGradient(listOf(Color(0xFF1B2140), Color(0xFF101018))))
                .border(1.dp, Color(0x332F6BFF), RoundedCornerShape(22.dp)).padding(18.dp)
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(48.dp).clip(CircleShape).background(Color(0x332F6BFF)), contentAlignment = Alignment.Center) {
                    Icon(Icons.Rounded.Shield, null, tint = Color(0xFF64A8FF), modifier = Modifier.size(26.dp))
                }
                Column(Modifier.padding(start = 14.dp)) {
                    Text(Str.t("log.lastOpened"), color = C.Text2, fontSize = 13.sp)
                    Text(
                        if (lastOpen != null) relative(lastOpen.time, now) + " · " + time.format(Date(lastOpen.time)) else "—",
                        color = C.Text, fontSize = 20.sp, fontWeight = FontWeight.Bold
                    )
                    val fails = items.count { it.kind == "failed" && now - it.time < 7L * 24 * 3600_000 }
                    if (fails > 0) Text(Str.f("log.failsWeek", fails), color = C.Red, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 2.dp))
                }
            }
        }

        GroupCard(Modifier.padding(top = 14.dp)) {
            IconSwitchRow(Icons.Rounded.Shield, C.Indigo, Str.t("set.secAlerts"), ps.secAlerts, Str.t("set.secAlertsSub")) { Connection.setShared("secAlerts", it) }
        }

        if (items.isEmpty()) {
            Hint(Str.t("log.empty"), Modifier.padding(top = 18.dp))
        } else {
            var lastDay = ""
            val cal = Calendar.getInstance()
            items.forEachIndexed { i, e ->
                val day = dayLabel(e.time, dayFmt)
                if (day != lastDay) {
                    Text(day, color = C.Text2, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 6.dp, top = 22.dp, bottom = 6.dp))
                    lastDay = day
                }
                val nextSameDay = i + 1 < items.size && dayLabel(items[i + 1].time, dayFmt) == day
                TimelineRow(e, time.format(Date(e.time)), relative(e.time, now), isLast = !nextSameDay, index = i)
            }
        }
        Spacer(Modifier.height(30.dp))
    }
}

@Composable
private fun TimelineRow(e: ActivityItem, time: String, rel: String, isLast: Boolean, index: Int) {
    val st = styleOf(e.kind)
    val appear by animateFloatAsState(1f, tween(300 + (index.coerceAtMost(10)) * 40), label = "a")
    Row(Modifier.fillMaxWidth().height(64.dp).graphicsLayer { alpha = appear }, verticalAlignment = Alignment.CenterVertically) {
        // rail + dot
        Box(Modifier.width(44.dp).fillMaxHeight(), contentAlignment = Alignment.Center) {
            Box(Modifier.width(2.dp).fillMaxHeight().padding(bottom = if (isLast) 32.dp else 0.dp).background(C.Card2))
            Box(
                Modifier.size(34.dp).clip(CircleShape).background(st.color.copy(alpha = 0.18f)).border(1.5.dp, st.color, CircleShape),
                contentAlignment = Alignment.Center
            ) { Icon(st.icon, null, tint = st.color, modifier = Modifier.size(17.dp)) }
        }
        Row(
            Modifier.weight(1f).padding(start = 8.dp).clip(RoundedCornerShape(16.dp)).background(if (e.kind == "failed") C.Red.copy(alpha = 0.10f) else C.Card)
                .padding(horizontal = 14.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column(Modifier.weight(1f)) {
                Text(Str.t("log." + e.kind), color = C.Text, fontSize = 15.5.sp, fontWeight = FontWeight.Medium)
                Text(if (e.detail.isNotBlank()) e.detail + " · " + rel else rel, color = C.Text2, fontSize = 12.5.sp)
            }
            Text(time, color = C.Text, fontSize = 17.sp, fontWeight = FontWeight.Bold)
        }
    }
}

private fun dayLabel(t: Long, fmt: SimpleDateFormat): String {
    val c = Calendar.getInstance()
    val today = Calendar.getInstance()
    c.timeInMillis = t
    fun same(a: Calendar, b: Calendar) = a.get(Calendar.YEAR) == b.get(Calendar.YEAR) && a.get(Calendar.DAY_OF_YEAR) == b.get(Calendar.DAY_OF_YEAR)
    if (same(c, today)) return Str.t("log.today")
    today.add(Calendar.DAY_OF_YEAR, -1)
    if (same(c, today)) return Str.t("log.yesterday")
    return fmt.format(Date(t)).replaceFirstChar { it.titlecase() }
}

private fun relative(t: Long, now: Long): String {
    val m = ((now - t) / 60_000).coerceAtLeast(0)
    return when {
        m < 1 -> Str.t("log.justNow")
        m < 60 -> Str.f("log.minAgo", m)
        m < 24 * 60 -> Str.f("log.hourAgo", m / 60)
        else -> Str.f("log.dayAgo", m / (24 * 60))
    }
}
