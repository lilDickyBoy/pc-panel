package com.pcpanel.app.ui.screens

import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Apps
import androidx.compose.material.icons.rounded.Bolt
import androidx.compose.material.icons.rounded.CameraAlt
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.ContentPaste
import androidx.compose.material.icons.rounded.Crop
import androidx.compose.material.icons.rounded.DesktopWindows
import androidx.compose.material.icons.rounded.Folder
import androidx.compose.material.icons.rounded.Headphones
import androidx.compose.material.icons.rounded.Keyboard
import androidx.compose.material.icons.rounded.Language
import androidx.compose.material.icons.rounded.Lock
import androidx.compose.material.icons.rounded.Mic
import androidx.compose.material.icons.rounded.MusicNote
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.QueryStats
import androidx.compose.material.icons.rounded.Send
import androidx.compose.material.icons.rounded.ShowChart
import androidx.compose.material.icons.rounded.Shield
import androidx.compose.material.icons.rounded.SportsEsports
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material.icons.rounded.SwapVert
import androidx.compose.material.icons.rounded.Thermostat
import androidx.compose.material.icons.rounded.Timer
import androidx.compose.material.icons.rounded.Toys
import androidx.compose.material.icons.rounded.UploadFile
import androidx.compose.material.icons.rounded.VolumeOff
import androidx.compose.material.icons.rounded.VolumeUp
import androidx.compose.material.icons.rounded.ViewModule
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.data.DeckButton
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.BannerKind
import com.pcpanel.app.net.Connection
import com.pcpanel.app.net.FanGroup
import com.pcpanel.app.net.fmtBytes
import com.pcpanel.app.service.PhoneLink
import com.pcpanel.app.service.Transfers
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.Chip
import com.pcpanel.app.ui.components.DialogButton
import com.pcpanel.app.ui.components.Divider
import com.pcpanel.app.ui.components.FatSlider
import com.pcpanel.app.ui.components.GroupCard
import com.pcpanel.app.ui.components.Hint
import com.pcpanel.app.ui.components.IconSwitchRow
import com.pcpanel.app.ui.components.IconTile
import com.pcpanel.app.ui.components.IosDialog
import com.pcpanel.app.ui.components.SectionTitle
import com.pcpanel.app.ui.components.Segmented
import com.pcpanel.app.ui.components.SubPage
import com.pcpanel.app.ui.components.bounceClick
import com.pcpanel.app.ui.components.rememberB64Image
import com.pcpanel.app.ui.theme.C
import kotlinx.coroutines.delay
import kotlin.math.roundToInt

private data class ToolDef(val id: String, val icon: ImageVector, val color: Color, val title: String, val sub: String)

@Composable
fun ToolsScreen() {
    val page = UiState.toolPage
    BackHandler(page != null) { UiState.toolPage = null }
    AnimatedContent(
        targetState = page,
        transitionSpec = {
            if (targetState != null) (slideInHorizontally(spring(dampingRatio = 0.9f, stiffness = 380f)) { it } + fadeIn(tween(160))) togetherWith (slideOutHorizontally(tween(260)) { -it / 4 } + fadeOut(tween(200)))
            else (slideInHorizontally(tween(260)) { -it / 4 } + fadeIn(tween(200))) togetherWith (slideOutHorizontally(spring(dampingRatio = 0.9f, stiffness = 380f)) { it } + fadeOut(tween(160)))
        },
        label = "tools"
    ) { p ->
        val back = { UiState.toolPage = null }
        when (p) {
            null -> ToolsGrid()
            "apps" -> AppsScreen(back)
            "deck" -> DeckPage(back)
            "screen" -> SecondScreenPage(back)
            "files" -> FilesPage(back)
            "clip" -> ClipboardPage(back)
            "sound" -> SoundPage(back)
            "timer" -> CountdownPage(back)
            "fans" -> FansPage(back)
            "history" -> HistoryPage(back)
            "activity" -> ActivityPage(back)
            else -> ToolsGrid()
        }
    }
}

@Composable
private fun ToolsGrid() {
    val tools = listOf(
        ToolDef("screen", Icons.Rounded.DesktopWindows, C.Blue, Str.t("tools.screen"), Str.t("tools.screenSub")),
        ToolDef("deck", Icons.Rounded.ViewModule, C.Indigo, Str.t("tools.deck"), Str.t("tools.deckSub")),
        ToolDef("apps", Icons.Rounded.Apps, C.Blue, Str.t("tab.apps"), Str.t("tools.appsSub")),
        ToolDef("files", Icons.Rounded.UploadFile, C.Orange, Str.t("tools.files"), Str.t("tools.filesSub")),
        ToolDef("clip", Icons.Rounded.ContentPaste, C.Teal, Str.t("tools.clip"), Str.t("tools.clipSub")),
        ToolDef("sound", Icons.Rounded.Headphones, C.Pink, Str.t("tools.sound"), Str.t("tools.soundSub")),
        ToolDef("timer", Icons.Rounded.Timer, C.Yellow, Str.t("tools.timer"), Str.t("tools.timerSub")),
        ToolDef("fans", Icons.Rounded.Toys, C.Green, Str.t("tools.fans"), Str.t("tools.fansSub")),
        ToolDef("history", Icons.Rounded.ShowChart, C.Red, Str.t("tools.history"), Str.t("tools.historySub")),
        ToolDef("activity", Icons.Rounded.Shield, C.Indigo, Str.t("tools.activity"), Str.t("tools.activitySub")),
    )
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).statusBarsPadding().padding(horizontal = 16.dp)) {
        LargeHeader(Str.t("tab.tools"))
        Spacer(Modifier.height(14.dp))
        tools.chunked(2).forEach { row ->
            Row(Modifier.fillMaxWidth().padding(bottom = 12.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                row.forEach { t ->
                    Column(
                        Modifier.weight(1f).bounceClick(pressedScale = 0.95f) { UiState.toolPage = t.id }
                            .clip(RoundedCornerShape(22.dp)).background(C.Card).padding(16.dp)
                    ) {
                        IconTile(t.icon, t.color, 44.dp)
                        Text(t.title, color = C.Text, fontSize = 17.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 14.dp), maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(t.sub, color = C.Text2, fontSize = 12.5.sp, lineHeight = 16.sp, maxLines = 2, minLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 2.dp))
                    }
                }
                if (row.size == 1) Spacer(Modifier.weight(1f))
            }
        }
        Spacer(Modifier.height(20.dp))
    }
}

// ============================================================================ Stream Deck
private val deckIcons: Map<String, ImageVector> = mapOf(
    "bolt" to Icons.Rounded.Bolt, "mic" to Icons.Rounded.Mic, "chart" to Icons.Rounded.QueryStats,
    "desktop" to Icons.Rounded.DesktopWindows, "crop" to Icons.Rounded.Crop, "play" to Icons.Rounded.PlayArrow,
    "folder" to Icons.Rounded.Folder, "music" to Icons.Rounded.MusicNote, "game" to Icons.Rounded.SportsEsports,
    "globe" to Icons.Rounded.Language, "keyboard" to Icons.Rounded.Keyboard, "camera" to Icons.Rounded.CameraAlt,
    "lock" to Icons.Rounded.Lock, "star" to Icons.Rounded.Star, "send" to Icons.Rounded.Send, "swap" to Icons.Rounded.SwapVert,
)
private val deckColors = listOf(0xFF0A84FF, 0xFF5E5CE6, 0xFFBF5AF2, 0xFFFF375F, 0xFFFF453A, 0xFFFF9F0A, 0xFFFFD60A, 0xFF30D158, 0xFF64D2FF, 0xFF8E8E93)

private fun runDeck(b: DeckButton) {
    when (b.kind) {
        "keys" -> Connection.keysList(b.value.split("+").map { it.trim().lowercase() }.filter { it.isNotEmpty() })
        "url" -> Connection.openUrl(if (b.value.startsWith("http")) b.value else "https://" + b.value)
        "text" -> Connection.text(b.value)
        "app" -> Connection.launch(b.value)
        "media" -> Connection.mediaAction(b.value)
    }
}

@Composable
private fun DeckPage(onBack: () -> Unit) {
    var editing by remember { mutableStateOf(false) }
    var edit by remember { mutableStateOf<DeckButton?>(null) }
    SubPage(Str.t("tools.deck"), Str.t("tab.tools"), onBack, actions = {
        Text(if (editing) Str.t("apps.done") else Str.t("apps.edit"), color = C.Blue, fontSize = 17.sp, fontWeight = FontWeight.SemiBold,
            modifier = Modifier.bounceClick { editing = !editing }.padding(8.dp))
    }) {
        Hint(if (editing) Str.t("deck.editHint") else Str.t("deck.hint"))
        val items = Prefs.deck
        (items.map { it as DeckButton? } + listOf(null)).chunked(3).forEach { row ->
            Row(Modifier.fillMaxWidth().padding(vertical = 6.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                row.forEach { b ->
                    if (b == null) {
                        Box(
                            Modifier.weight(1f).aspectRatio(1f).bounceClick { edit = DeckButton("d" + System.currentTimeMillis(), "", "keys", "", deckColors[items.size % deckColors.size], "bolt") }
                                .clip(RoundedCornerShape(20.dp)).border(1.5.dp, C.Card3, RoundedCornerShape(20.dp)),
                            contentAlignment = Alignment.Center
                        ) { Icon(Icons.Rounded.Add, null, tint = C.Text2, modifier = Modifier.size(34.dp)) }
                    } else {
                        Column(
                            Modifier.weight(1f).aspectRatio(1f)
                                .bounceClick(pressedScale = 0.88f, onLongClick = { edit = b }) { if (editing) edit = b else runDeck(b) }
                                .clip(RoundedCornerShape(20.dp)).background(Color(b.color)).padding(10.dp),
                            horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center
                        ) {
                            Icon(deckIcons[b.icon] ?: Icons.Rounded.Bolt, null, tint = Color.White, modifier = Modifier.size(34.dp))
                            Text(b.label, color = Color.White, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, textAlign = TextAlign.Center, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 6.dp))
                        }
                    }
                }
                repeat(3 - row.size) { Spacer(Modifier.weight(1f)) }
            }
        }
        Spacer(Modifier.height(30.dp))
    }
    DeckEditor(edit, onClose = { edit = null })
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun DeckEditor(btn: DeckButton?, onClose: () -> Unit) {
    var shown by remember { mutableStateOf<DeckButton?>(null) }
    if (btn != null && shown?.id != btn.id) shown = btn
    val b = shown
    var label by remember(b?.id) { mutableStateOf(b?.label ?: "") }
    var kind by remember(b?.id) { mutableStateOf(b?.kind ?: "keys") }
    var value by remember(b?.id) { mutableStateOf(b?.value ?: "") }
    var color by remember(b?.id) { mutableLongStateOf(b?.color ?: 0xFF0A84FF) }
    var icon by remember(b?.id) { mutableStateOf(b?.icon ?: "bolt") }
    val kinds = listOf("keys", "url", "text", "app", "media")
    LaunchedEffect(kind) { if (kind == "app" && Connection.apps.isEmpty()) Connection.loadApps() }
    BackHandler(btn != null) { onClose() }
    IosDialog(visible = btn != null, onDismiss = onClose) {
        Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState())) {
            Field(Str.t("deck.label"), label) { label = it }
            Spacer(Modifier.height(12.dp))
            Segmented(kinds.map { Str.t("deck.kind.$it") }, kinds.indexOf(kind).coerceAtLeast(0), Modifier.fillMaxWidth()) { kind = kinds[it]; value = "" }
            Spacer(Modifier.height(12.dp))
            when (kind) {
                "app" -> {
                    Text(Str.t("deck.pickApp"), color = C.Text2, fontSize = 13.sp)
                    Column(Modifier.fillMaxWidth().height(160.dp).verticalScroll(rememberScrollState())) {
                        Connection.apps.forEach { a ->
                            Row(Modifier.fillMaxWidth().bounceClick(pressedScale = 0.97f) { value = a.id; if (label.isBlank()) label = a.name }.padding(vertical = 7.dp), verticalAlignment = Alignment.CenterVertically) {
                                Text(a.name, color = if (value == a.id) C.Blue else C.Text, fontSize = 15.sp, modifier = Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
                                if (value == a.id) Icon(Icons.Rounded.Check, null, tint = C.Blue, modifier = Modifier.size(18.dp))
                            }
                        }
                    }
                }
                "media" -> {
                    val opts = listOf("toggle", "next", "prev")
                    Segmented(opts.map { Str.t("deck.media.$it") }, opts.indexOf(value).coerceAtLeast(0), Modifier.fillMaxWidth()) { value = opts[it] }
                    if (value.isBlank()) value = "toggle"
                }
                else -> Field(Str.t("deck.value.$kind"), value) { value = it }
            }
            Spacer(Modifier.height(14.dp))
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                deckColors.forEach { c ->
                    Box(Modifier.size(30.dp).bounceClick { color = c }.clip(CircleShape).background(Color(c)).then(if (c == color) Modifier.border(3.dp, Color.White, CircleShape) else Modifier))
                }
            }
            Spacer(Modifier.height(12.dp))
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                deckIcons.forEach { (k, v) ->
                    Box(Modifier.size(38.dp).bounceClick { icon = k }.clip(RoundedCornerShape(10.dp)).background(if (k == icon) Color(color) else C.Card2), contentAlignment = Alignment.Center) {
                        Icon(v, null, tint = Color.White, modifier = Modifier.size(22.dp))
                    }
                }
            }
            Spacer(Modifier.height(18.dp))
            DialogButton(Str.t("deck.save"), color = C.Blue, filled = true) {
                if (b != null && value.isNotBlank()) {
                    val nb = DeckButton(b.id, label.ifBlank { value.take(16) }, kind, value, color, icon)
                    val list = Prefs.deck
                    Prefs.saveDeck(if (list.any { it.id == b.id }) list.map { if (it.id == b.id) nb else it } else list + nb)
                }
                onClose()
            }
            if (b != null && Prefs.deck.any { it.id == b.id }) {
                Spacer(Modifier.height(10.dp))
                DialogButton(Str.t("deck.delete"), color = C.Red) { Prefs.saveDeck(Prefs.deck.filterNot { it.id == b.id }); onClose() }
            }
            Spacer(Modifier.height(10.dp))
            DialogButton(Str.t("act.cancel")) { onClose() }
        }
    }
}

@Composable
fun Field(label: String, value: String, singleLine: Boolean = true, onChange: (String) -> Unit) {
    Column(Modifier.fillMaxWidth()) {
        Text(label, color = C.Text2, fontSize = 13.sp, modifier = Modifier.padding(bottom = 6.dp, start = 2.dp))
        BasicTextField(
            value, onChange, singleLine = singleLine,
            textStyle = TextStyle(color = C.Text, fontSize = 16.sp),
            cursorBrush = SolidColor(C.Blue),
            modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(C.Card2).padding(horizontal = 14.dp, vertical = 12.dp)
        )
    }
}

// ============================================================================ Files
@Composable
private fun FilesPage(onBack: () -> Unit) {
    val ctx = LocalContext.current
    val pick = rememberLauncherForActivityResult(ActivityResultContracts.GetMultipleContents()) { uris ->
        if (uris.isNotEmpty()) Transfers.send(ctx, uris)
    }
    SubPage(Str.t("tools.files"), Str.t("tab.tools"), onBack) {
        Spacer(Modifier.height(8.dp))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            BigTile(Modifier.weight(1f), Icons.Rounded.CameraAlt, C.Pink, Str.t("files.photos")) { pick.launch("image/*") }
            BigTile(Modifier.weight(1f), Icons.Rounded.UploadFile, C.Orange, Str.t("files.any")) { pick.launch("*/*") }
        }
        Hint(Str.t("files.where"), Modifier.padding(top = 6.dp))
        val list = Transfers.list
        if (list.isNotEmpty()) {
            SectionTitle(Str.t("files.recent"), trailing = {
                Text(Str.t("files.clear"), color = C.Blue, fontSize = 15.sp, modifier = Modifier.bounceClick { Transfers.clearFinished() }.padding(6.dp))
            })
            GroupCard {
                list.forEachIndexed { i, t ->
                    if (i > 0) Divider()
                    Row(Modifier.fillMaxWidth().padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                        IconTile(if (t.incoming) Icons.Rounded.Folder else Icons.Rounded.Send, if (t.failed) C.Red else if (t.incoming) C.Green else C.Blue, 34.dp)
                        Column(Modifier.weight(1f).padding(start = 12.dp)) {
                            Text(t.name, color = C.Text, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            val pct = if (t.size > 0) (t.done.toFloat() / t.size).coerceIn(0f, 1f) else if (t.finished) 1f else 0f
                            if (!t.finished) Bar(pct, C.Blue, Modifier.padding(top = 6.dp), height = 5)
                            Text(
                                when { t.failed -> Str.t("files.failedShort"); t.finished -> (if (t.incoming) Str.t("files.receivedShort") else Str.t("files.sentShort")) + " · " + fmtBytes(t.done); else -> "${(pct * 100).roundToInt()}% · ${fmtBytes(t.done)}" },
                                color = if (t.failed) C.Red else C.Text2, fontSize = 12.5.sp, modifier = Modifier.padding(top = 3.dp)
                            )
                        }
                    }
                }
            }
        }
        Spacer(Modifier.height(30.dp))
    }
}

@Composable
private fun BigTile(modifier: Modifier, icon: ImageVector, color: Color, label: String, onClick: () -> Unit) {
    Column(
        modifier.bounceClick(pressedScale = 0.94f, onClick = onClick).clip(RoundedCornerShape(22.dp)).background(C.Card).padding(vertical = 22.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        IconTile(icon, color, 52.dp)
        Text(label, color = C.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 12.dp, start = 8.dp, end = 8.dp))
    }
}

// ============================================================================ Clipboard
@Composable
private fun ClipboardPage(onBack: () -> Unit) {
    val ctx = LocalContext.current
    var current by remember { mutableStateOf(PhoneLink.readClipboard(ctx) ?: "") }
    var typed by remember { mutableStateOf("") }
    SubPage(Str.t("tools.clip"), Str.t("tab.tools"), onBack) {
        Spacer(Modifier.height(8.dp))
        GroupCard {
            IconSwitchRow(Icons.Rounded.SwapVert, C.Teal, Str.t("clip.auto"), Connection.pcSettings.clipSync, Str.t("clip.autoSub")) { Connection.setShared("clipSync", it) }
        }
        SectionTitle(Str.t("clip.phone"))
        GroupCard {
            Text(current.ifBlank { Str.t("clip.empty") }, color = if (current.isBlank()) C.Text2 else C.Text, fontSize = 15.sp, maxLines = 6, overflow = TextOverflow.Ellipsis)
            Spacer(Modifier.height(12.dp))
            DialogButton(Str.t("clip.sendNow"), color = C.Teal, filled = true) {
                current = PhoneLink.readClipboard(ctx) ?: ""
                if (PhoneLink.pushClipboard(ctx, explicit = true)) Connection.showBanner(Str.t("clip.sent"), BannerKind.Success)
                else Connection.showBanner(Str.t("clip.empty"), BannerKind.Warning)
            }
        }
        SectionTitle(Str.t("clip.typeTitle"))
        GroupCard {
            Field(Str.t("clip.typeHint"), typed, singleLine = false) { typed = it }
            Spacer(Modifier.height(12.dp))
            DialogButton(Str.t("clip.typeSend"), color = C.Blue, filled = true) {
                if (typed.isNotBlank() && Connection.cmd("clip", "text" to typed, "explicit" to true)) {
                    Connection.showBanner(Str.t("clip.sent"), BannerKind.Success); typed = ""
                }
            }
        }
        Spacer(Modifier.height(30.dp))
    }
}

// ============================================================================ Sound (output + mixer)
@Composable
private fun SoundPage(onBack: () -> Unit) {
    LaunchedEffect(Unit) {
        Connection.outputsReq()
        while (true) { Connection.mixerReq(); delay(3000) }
    }
    SubPage(Str.t("tools.sound"), Str.t("tab.tools"), onBack) {
        SectionTitle(Str.t("sound.output"))
        GroupCard {
            val outs = Connection.outputs
            if (outs.isEmpty()) Text(Str.t("apps.loading"), color = C.Text2, fontSize = 14.sp, modifier = Modifier.padding(6.dp))
            outs.forEachIndexed { i, o ->
                if (i > 0) Divider()
                Row(Modifier.fillMaxWidth().bounceClick(pressedScale = 0.97f) { if (!o.isDefault) Connection.outputSet(o.id) }.padding(vertical = 11.dp), verticalAlignment = Alignment.CenterVertically) {
                    IconTile(Icons.Rounded.Headphones, if (o.isDefault) C.Blue else C.Card3, 32.dp)
                    Text(o.name, color = C.Text, fontSize = 15.5.sp, modifier = Modifier.weight(1f).padding(start = 12.dp), maxLines = 2, overflow = TextOverflow.Ellipsis)
                    if (o.isDefault) Icon(Icons.Rounded.Check, null, tint = C.Blue)
                }
            }
        }
        SectionTitle(Str.t("sound.master"))
        GroupCard {
            FatSlider(
                value = Connection.volume, onValueChange = { Connection.setVolume(it) }, height = 50.dp,
                icon = if (Connection.muted || Connection.volume < 0.01f) Icons.Rounded.VolumeOff else Icons.Rounded.VolumeUp,
                trailingText = "${(Connection.volume * 100).roundToInt()}%",
                onCommit = { Connection.setVolume(it) }
            )
        }
        SectionTitle(Str.t("sound.apps"))
        val apps = Connection.mixer
        if (apps.isEmpty()) Hint(Str.t("sound.none"))
        apps.forEach { a ->
            GroupCard(Modifier.padding(bottom = 10.dp), padding = 14.dp) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    val img = rememberB64Image(a.iconB64)
                    if (img != null) Image(img, null, modifier = Modifier.size(30.dp))
                    else IconTile(Icons.Rounded.VolumeUp, C.Card3, 30.dp)
                    Text(a.name, color = C.Text, fontSize = 15.5.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f).padding(start = 12.dp), maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Box(
                        Modifier.size(36.dp).bounceClick { Connection.mixerSet(a.id, mute = !a.mute) }.clip(CircleShape).background(if (a.mute) C.Red.copy(alpha = 0.2f) else C.Card2),
                        contentAlignment = Alignment.Center
                    ) { Icon(if (a.mute) Icons.Rounded.VolumeOff else Icons.Rounded.VolumeUp, null, tint = if (a.mute) C.Red else C.Text, modifier = Modifier.size(20.dp)) }
                }
                Spacer(Modifier.height(10.dp))
                FatSlider(
                    value = a.vol, onValueChange = { Connection.mixerSet(a.id, vol = it) }, height = 40.dp,
                    color = if (a.mute) C.Text3 else Color.White,
                    trailingText = "${(a.vol * 100).roundToInt()}%",
                    onCommit = { Connection.mixerSet(a.id, vol = it) }
                )
            }
        }
        Spacer(Modifier.height(30.dp))
    }
}

// ============================================================================ Countdown in the notch
@Composable
private fun CountdownPage(onBack: () -> Unit) {
    var minutes by remember { mutableIntStateOf(25) }
    var label by remember { mutableStateOf("") }
    val cd = Connection.countdown
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(cd.active, cd.receivedAt) { while (cd.active) { delay(500); now = System.currentTimeMillis() } }
    SubPage(Str.t("tools.timer"), Str.t("tab.tools"), onBack) {
        Hint(Str.t("cd.hint"))
        if (cd.active) {
            @Suppress("UNUSED_VARIABLE") val n = now
            val rem = cd.remainingNow()
            GroupCard(Modifier.padding(top = 8.dp)) {
                Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally) {
                    Box(Modifier.size(200.dp), contentAlignment = Alignment.Center) {
                        val frac = if (cd.total > 0) rem.toFloat() / cd.total else 0f
                        Canvas(Modifier.fillMaxSize().padding(8.dp)) {
                            drawArc(C.Card2, -90f, 360f, false, style = Stroke(14.dp.toPx(), cap = StrokeCap.Round))
                            drawArc(C.Orange, -90f, 360f * frac, false, style = Stroke(14.dp.toPx(), cap = StrokeCap.Round))
                        }
                        Text(String.format("%d:%02d", rem / 60, rem % 60), color = C.Text, fontSize = 44.sp, fontWeight = FontWeight.Bold)
                    }
                    if (cd.label.isNotBlank()) Text(cd.label, color = C.Orange, fontSize = 17.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 6.dp))
                    Spacer(Modifier.height(16.dp))
                    DialogButton(Str.t("cd.cancel"), color = C.Red) { Connection.countdownCancel() }
                }
            }
        } else {
            SectionTitle(Str.t("cd.duration"))
            GroupCard {
                Text(String.format("%d %s", minutes, Str.t("cd.min")), color = C.Text, fontSize = 34.sp, fontWeight = FontWeight.Bold, modifier = Modifier.align(Alignment.CenterHorizontally))
                Spacer(Modifier.height(12.dp))
                FlowRowChips(listOf(1, 3, 5, 10, 15, 25, 30, 45, 60, 90), minutes) { minutes = it }
            }
            SectionTitle(Str.t("cd.labelTitle"))
            GroupCard {
                Field(Str.t("cd.labelHint"), label) { label = it }
                Spacer(Modifier.height(10.dp))
                FlowRowLabels(listOf(Str.t("cd.preset.focus"), Str.t("cd.preset.break"), Str.t("cd.preset.tea"), Str.t("cd.preset.game"))) { label = it }
            }
            Spacer(Modifier.height(16.dp))
            DialogButton(Str.t("cd.start"), color = C.Orange, filled = true) { Connection.countdownStart(minutes * 60, label) }
        }
        Spacer(Modifier.height(30.dp))
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun FlowRowChips(values: List<Int>, selected: Int, onPick: (Int) -> Unit) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        values.forEach { v -> Chip("$v", v == selected) { onPick(v) } }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun FlowRowLabels(values: List<String>, onPick: (String) -> Unit) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        values.forEach { v -> Chip(v, false) { onPick(v) } }
    }
}

// ============================================================================ Fans
@Composable
private fun FansPage(onBack: () -> Unit) {
    val s = Connection.stats
    SubPage(Str.t("tools.fans"), Str.t("tab.tools"), onBack) {
        Spacer(Modifier.height(6.dp))
        GroupCard {
            IconSwitchRow(Icons.Rounded.Toys, C.Green, Str.t("fans.allow"), Connection.pcSettings.fanCtl, Str.t("fans.allowSub")) { Connection.setShared("fanCtl", it) }
        }
        if (Connection.pcSettings.fanCtl) {
            FanGroupCard(Str.t("fans.gpu"), "gpu", s.fanGpu, if (s.gpuTemp >= 0) "${s.gpuTemp.roundToInt()}°C" else "—",
                if (s.gpuFan >= 0) "${s.gpuFan.roundToInt()} RPM" else "")
            FanGroupCard(Str.t("fans.case"), "case", s.fanCase, if (s.cpuTemp >= 0) "${s.cpuTemp.roundToInt()}°C" else "—",
                s.fans.joinToString("  ·  ") { "${it.name}: ${it.rpm}" })
        }
        Hint(Str.t("fans.warn"), Modifier.padding(top = 10.dp))
        Spacer(Modifier.height(30.dp))
    }
}

@Composable
private fun FanGroupCard(title: String, group: String, g: FanGroup, temp: String, rpm: String) {
    val modes = listOf("auto", "silent", "balanced", "performance", "manual")
    var manual by remember(group) { mutableIntStateOf(g.pct) }
    SectionTitle(title, trailing = { Text(temp, color = C.Text2, fontSize = 15.sp) })
    GroupCard {
        if (!g.available) {
            Text(Str.t("fans.unavailable"), color = C.Text2, fontSize = 14.sp, lineHeight = 19.sp)
            return@GroupCard
        }
        Segmented(modes.map { Str.t("fans.mode.$it") }, modes.indexOf(g.mode).coerceAtLeast(0), Modifier.fillMaxWidth()) {
            Connection.fanSet(group, modes[it], manual)
        }
        if (g.mode == "manual") {
            Spacer(Modifier.height(12.dp))
            FatSlider(
                value = ((manual - 30) / 70f).coerceIn(0f, 1f), onValueChange = { }, height = 46.dp,
                icon = Icons.Rounded.Toys, trailingText = "$manual%",
                onCommit = { manual = (30 + it * 70).roundToInt(); Connection.fanSet(group, "manual", manual) }
            )
        }
        if (rpm.isNotBlank()) Text(rpm, color = C.Text2, fontSize = 13.sp, modifier = Modifier.padding(top = 10.dp))
    }
}

// ============================================================================ Temperature history
@Composable
private fun HistoryPage(onBack: () -> Unit) {
    LaunchedEffect(Unit) { while (true) { Connection.historyReq(); delay(15000) } }
    val h = Connection.history
    SubPage(Str.t("tools.history"), Str.t("tab.tools"), onBack) {
        if (!Connection.pcSettings.tempHistory) {
            GroupCard(Modifier.padding(top = 8.dp)) {
                IconSwitchRow(Icons.Rounded.Thermostat, C.Red, Str.t("hist.enable"), false, Str.t("hist.enableSub")) { Connection.setShared("tempHistory", it) }
            }
        }
        HistoryChart(Str.t("home.cpu"), h.cpu, C.Blue, h.step)
        HistoryChart(Str.t("home.gpu"), h.gpu, C.Green, h.step)
        Hint(Str.t("hist.hint"))
        Spacer(Modifier.height(30.dp))
    }
}

@Composable
fun HistoryChart(title: String, values: List<Float>, color: Color, step: Int) {
    val valid = values.filter { it > 0 }
    SectionTitle(title, trailing = {
        if (valid.isNotEmpty()) Text(String.format("%s %d°  ·  %s %d°  ·  %s %d°", Str.t("hist.min"), valid.min().roundToInt(), Str.t("hist.avg"), valid.average().roundToInt(), Str.t("hist.max"), valid.max().roundToInt()), color = C.Text2, fontSize = 12.5.sp)
    })
    GroupCard {
        if (valid.size < 2) {
            Text(Str.t("hist.empty"), color = C.Text2, fontSize = 14.sp, modifier = Modifier.padding(vertical = 20.dp))
            return@GroupCard
        }
        val lo = (valid.min() - 5).coerceAtLeast(0f)
        val hi = valid.max() + 5
        Box(Modifier.fillMaxWidth().height(150.dp)) {
            Canvas(Modifier.fillMaxSize()) {
                // grid
                for (i in 0..3) {
                    val y = size.height * i / 3f
                    drawLine(C.Sep, Offset(0f, y), Offset(size.width, y), strokeWidth = 1f)
                }
                val n = values.size
                val path = Path()
                var started = false
                values.forEachIndexed { i, v ->
                    if (v <= 0) return@forEachIndexed
                    val x = if (n <= 1) 0f else size.width * i / (n - 1)
                    val y = size.height * (1f - (v - lo) / (hi - lo))
                    if (!started) { path.moveTo(x, y); started = true } else path.lineTo(x, y)
                }
                drawPath(path, color, style = Stroke(2.5.dp.toPx(), cap = StrokeCap.Round))
            }
        }
        Row(Modifier.fillMaxWidth().padding(top = 6.dp)) {
            Text("-${(values.size * step) / 60} ${Str.t("cd.min")}", color = C.Text3, fontSize = 11.sp, modifier = Modifier.weight(1f))
            Text(Str.t("hist.now"), color = C.Text3, fontSize = 11.sp)
        }
    }
}
