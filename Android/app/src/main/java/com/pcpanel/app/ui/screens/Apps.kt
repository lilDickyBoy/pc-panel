package com.pcpanel.app.ui.screens

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
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
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Apps
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.Lock
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.Remove
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.data.FavApp
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.BannerKind
import com.pcpanel.app.net.Connection
import com.pcpanel.app.net.fmtBytes
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.GroupCard
import com.pcpanel.app.ui.components.SectionTitle
import com.pcpanel.app.ui.components.bounceClick
import com.pcpanel.app.ui.theme.C
import kotlinx.coroutines.delay

@Composable
fun AppIcon(id: String, name: String, size: Int) {
    LaunchedEffect(id) { Connection.requestIcon(id) }
    val icon = Connection.icons[id]
    Box(
        Modifier.size(size.dp).clip(RoundedCornerShape((size * 0.2237f).dp)).background(C.Card2),
        contentAlignment = Alignment.Center
    ) {
        if (icon != null) Image(icon, null, modifier = Modifier.size((size * 0.72f).dp))
        else Text(name.take(1).uppercase(), color = C.Text2, fontSize = (size * 0.4f).sp, fontWeight = FontWeight.Bold)
    }
}

@Composable
fun AppsScreen(onBack: () -> Unit) {
    var editing by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) {
        while (true) { Connection.refreshProcs(); delay(5000) }
    }
    com.pcpanel.app.ui.components.SubPage(Str.t("tab.apps"), Str.t("tab.tools"), onBack, actions = {
        if (Prefs.favorites.isNotEmpty()) {
            Text(
                if (editing) Str.t("apps.done") else Str.t("apps.edit"),
                color = C.Blue, fontSize = 17.sp, fontWeight = FontWeight.SemiBold,
                modifier = Modifier.bounceClick { editing = !editing }.padding(8.dp)
            )
        }
    }) {

        SectionTitle(Str.t("apps.favorites"))
        val favs = Prefs.favorites
        GroupCard(padding = 14.dp) {
            if (favs.isEmpty()) {
                Column(Modifier.fillMaxWidth().padding(vertical = 10.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                    Icon(Icons.Rounded.Apps, null, tint = C.Text3, modifier = Modifier.size(44.dp))
                    Text(Str.t("apps.empty"), color = C.Text2, fontSize = 14.sp, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 8.dp, bottom = 14.dp))
                    AddAppsButton()
                }
            } else {
                favs.chunked(4).forEach { row ->
                    Row(Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
                        row.forEach { app -> FavTile(Modifier.weight(1f), app, editing) }
                        repeat(4 - row.size) { Spacer(Modifier.weight(1f)) }
                    }
                }
                Spacer(Modifier.height(8.dp))
                AddAppsButton()
            }
        }

        SectionTitle(Str.t("apps.running"), trailing = {
            Box(Modifier.size(34.dp).bounceClick { Connection.refreshProcs() }.clip(CircleShape).background(C.Card), contentAlignment = Alignment.Center) {
                Icon(Icons.Rounded.Refresh, null, tint = C.Blue, modifier = Modifier.size(20.dp))
            }
        })
        val procs = Connection.procs
        GroupCard(padding = 12.dp) {
            if (procs.isEmpty()) Text(Str.t("apps.loading"), color = C.Text2, modifier = Modifier.padding(8.dp))
            val maxMem = (procs.maxOfOrNull { it.mem } ?: 1L).coerceAtLeast(1L)
            procs.forEachIndexed { i, p ->
                if (i > 0) Box(Modifier.fillMaxWidth().height(0.6.dp).background(C.Sep))
                Row(Modifier.fillMaxWidth().padding(vertical = 9.dp), verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text(p.name, color = C.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
                            if (p.count > 1) Text("  ×${p.count}", color = C.Text3, fontSize = 12.sp)
                        }
                        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 5.dp)) {
                            Bar(p.mem.toFloat() / maxMem, if (p.mem > 1_500_000_000) C.Orange else C.Blue, Modifier.weight(1f), height = 5)
                            Text(fmtBytes(p.mem), color = C.Text2, fontSize = 12.sp, modifier = Modifier.padding(start = 10.dp))
                        }
                    }
                    Spacer(Modifier.size(12.dp))
                    if (p.locked) {
                        Icon(Icons.Rounded.Lock, null, tint = C.Text3, modifier = Modifier.size(18.dp).padding(horizontal = 0.dp))
                        Spacer(Modifier.size(20.dp))
                    } else {
                        Text(
                            Str.t("apps.end"), color = C.Red, fontSize = 14.sp, fontWeight = FontWeight.SemiBold,
                            modifier = Modifier.bounceClick { UiState.killTarget = p.name }.clip(RoundedCornerShape(10.dp)).background(C.Red.copy(alpha = 0.14f)).padding(horizontal = 12.dp, vertical = 7.dp)
                        )
                    }
                }
            }
        }
        Spacer(Modifier.height(28.dp))
    }
}

@Composable
private fun AddAppsButton() {
    Row(
        Modifier.fillMaxWidth().height(46.dp).bounceClick { UiState.appPicker = true; Connection.loadApps() }.clip(RoundedCornerShape(14.dp)).background(C.Card2),
        horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically
    ) {
        Icon(Icons.Rounded.Add, null, tint = C.Blue)
        Text(Str.t("apps.add"), color = C.Blue, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 6.dp))
    }
}

@Composable
private fun FavTile(modifier: Modifier, app: FavApp, editing: Boolean) {
    // iOS-style jiggle while editing
    val jiggle = rememberInfiniteTransition(label = "jiggle")
    val rot by jiggle.animateFloat(-2.2f, 2.2f, infiniteRepeatable(tween(130 + (app.id.hashCode() and 31)), RepeatMode.Reverse), label = "rot")
    Column(
        modifier.bounceClick(pressedScale = 0.88f) {
            if (editing) Prefs.toggleFavorite(app)
            else { Connection.launch(app.id); Connection.showBanner("${Str.t("apps.launching")} ${app.name}", BannerKind.Success) }
        },
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Box(Modifier.rotate(if (editing) rot else 0f)) {
            AppIcon(app.id, app.name, 60)
            androidx.compose.animation.AnimatedVisibility(editing, enter = scaleIn(), exit = scaleOut(), modifier = Modifier.offset((-6).dp, (-6).dp)) {
                Box(Modifier.size(22.dp).clip(CircleShape).background(C.Card3), contentAlignment = Alignment.Center) {
                    Icon(Icons.Rounded.Remove, null, tint = Color.White, modifier = Modifier.size(16.dp))
                }
            }
        }
        Text(app.name, color = C.Text, fontSize = 11.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 6.dp, start = 2.dp, end = 2.dp))
    }
}

// ================================================================== picker (full screen sheet)
@Composable
fun AppPicker(onClose: () -> Unit) {
    var q by remember { mutableStateOf("") }
    BackHandler { onClose() }
    Column(Modifier.fillMaxSize().background(C.Bg).statusBarsPadding().navigationBarsPadding().imePadding()) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(Str.t("apps.add"), color = C.Text, fontSize = 26.sp, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
            Box(Modifier.size(36.dp).bounceClick(onClick = onClose).clip(CircleShape).background(C.Card2), contentAlignment = Alignment.Center) {
                Icon(Icons.Rounded.Close, null, tint = C.Text2, modifier = Modifier.size(20.dp))
            }
        }
        Row(
            Modifier.padding(horizontal = 16.dp).fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(C.Card2).padding(horizontal = 12.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Icon(Icons.Rounded.Search, null, tint = C.Text2, modifier = Modifier.size(20.dp))
            Box(Modifier.weight(1f).padding(start = 8.dp)) {
                if (q.isEmpty()) Text(Str.t("apps.search"), color = C.Text3, fontSize = 16.sp)
                BasicTextField(q, { q = it }, singleLine = true, textStyle = TextStyle(color = C.Text, fontSize = 16.sp), cursorBrush = SolidColor(C.Blue), modifier = Modifier.fillMaxWidth())
            }
        }
        Spacer(Modifier.height(8.dp))
        if (Connection.appsLoading && Connection.apps.isEmpty()) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator(color = C.Blue) }
        } else {
            val list = Connection.apps.filter { q.isBlank() || it.name.contains(q, ignoreCase = true) }
            val favIds = Prefs.favorites.map { it.id }.toSet()
            LazyColumn(Modifier.fillMaxSize().padding(horizontal = 16.dp)) {
                items(list, key = { it.id }) { a ->
                    val fav = a.id in favIds
                    Row(
                        Modifier.fillMaxWidth().bounceClick(pressedScale = 0.97f) { Prefs.toggleFavorite(FavApp(a.id, a.name)) }.padding(vertical = 8.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        AppIcon(a.id, a.name, 42)
                        Text(a.name, color = C.Text, fontSize = 16.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f).padding(horizontal = 12.dp))
                        Icon(
                            if (fav) Icons.Rounded.CheckCircle else Icons.Rounded.Add, null,
                            tint = if (fav) C.Green else C.Text3, modifier = Modifier.size(26.dp)
                        )
                    }
                }
            }
        }
    }
}
