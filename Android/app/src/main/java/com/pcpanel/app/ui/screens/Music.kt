package com.pcpanel.app.ui.screens

import androidx.activity.compose.BackHandler
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ChevronLeft
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.Favorite
import androidx.compose.material.icons.rounded.Headphones
import androidx.compose.material.icons.rounded.MusicNote
import androidx.compose.material.icons.rounded.OpenInNew
import androidx.compose.material.icons.rounded.PlayArrow
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.net.BannerKind
import com.pcpanel.app.net.ConnState
import com.pcpanel.app.net.Connection
import com.pcpanel.app.net.MusicItem
import com.pcpanel.app.ui.Str
import com.pcpanel.app.ui.components.DialogButton
import com.pcpanel.app.ui.components.GroupCard
import com.pcpanel.app.ui.components.Hint
import com.pcpanel.app.ui.components.NetImage
import com.pcpanel.app.ui.components.Segmented
import com.pcpanel.app.ui.components.bounceClick
import com.pcpanel.app.ui.theme.C

private val SpotifyGreen = Color(0xFF1DB954)
private val YouTubeRed = Color(0xFFFF0033)

/** Search & play music on the PC (Spotify Web API or YouTube in the PC's browser). */
@Composable
fun MusicBrowser() {
    val ps = Connection.pcSettings
    val connected = Connection.state == ConnState.Connected
    var src by rememberSaveable { mutableStateOf(ps.musicSrc) }
    LaunchedEffect(ps.musicSrc) { src = ps.musicSrc }
    var q by rememberSaveable { mutableStateOf("") }
    var searched by rememberSaveable { mutableStateOf(false) }
    val focus = LocalFocusManager.current

    LaunchedEffect(src, connected, ps.spotify) {
        searched = false
        if (connected && src == "spotify" && ps.spotify) Connection.musicHomeReq("spotify")
    }

    fun search() {
        if (q.isBlank()) return
        focus.clearFocus()
        searched = true
        if (src == "youtube" && !ps.ytKey) { Connection.musicSearchInBrowser(q.trim()); Connection.showBanner(Str.t("music.openedOnPc"), BannerKind.Success); return }
        Connection.musicSearch(src, q.trim())
    }

    Column(Modifier.fillMaxWidth()) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 8.dp, bottom = 10.dp)) {
            Text(Str.t("music.browse"), color = C.Text, fontSize = 22.sp, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
            Row(
                Modifier.bounceClick { UiState.toolPage = "sound"; UiState.tab = 3 }.clip(RoundedCornerShape(50)).background(Color.White.copy(alpha = 0.1f)).padding(horizontal = 12.dp, vertical = 7.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(Icons.Rounded.Headphones, null, tint = C.Text, modifier = Modifier.size(16.dp))
                Text(Connection.outputName.ifBlank { Str.t("tools.sound") }, color = C.Text, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(start = 6.dp).width(110.dp))
            }
        }
        Segmented(listOf("Spotify", "YouTube"), if (src == "youtube") 1 else 0, Modifier.fillMaxWidth()) {
            src = if (it == 1) "youtube" else "spotify"
        }
        Spacer(Modifier.height(10.dp))
        // ---------------- search field
        Row(
            Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Color.White.copy(alpha = 0.1f)).padding(horizontal = 12.dp, vertical = 11.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Icon(Icons.Rounded.Search, null, tint = C.Text2, modifier = Modifier.size(20.dp))
            Box(Modifier.weight(1f).padding(horizontal = 8.dp)) {
                if (q.isEmpty()) Text(if (src == "youtube") Str.t("music.searchYt") else Str.t("music.searchSp"), color = C.Text2, fontSize = 16.sp)
                BasicTextField(
                    q, { q = it }, singleLine = true,
                    textStyle = TextStyle(color = C.Text, fontSize = 16.sp),
                    cursorBrush = SolidColor(C.Blue),
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                    keyboardActions = KeyboardActions(onSearch = { search() }),
                    modifier = Modifier.fillMaxWidth()
                )
            }
            if (q.isNotEmpty()) Icon(Icons.Rounded.Close, null, tint = C.Text2, modifier = Modifier.size(20.dp).bounceClick { q = ""; searched = false })
        }
        Spacer(Modifier.height(14.dp))

        // ---------------- content
        when {
            !connected -> Hint(Str.t("music.offline"))
            src == "spotify" && !ps.spotify -> SpotifyConnectCard(ps.spotifyClient)
            src == "youtube" && !ps.ytKey && !searched -> Hint(Str.t("music.ytNoKey"))
            Connection.musicLoading -> Box(Modifier.fillMaxWidth().padding(24.dp), contentAlignment = Alignment.Center) {
                CircularProgressIndicator(color = if (src == "youtube") YouTubeRed else SpotifyGreen, strokeWidth = 3.dp, modifier = Modifier.size(30.dp))
            }
            searched -> {
                val items = Connection.musicResults
                if (items.isEmpty()) Hint(if (Connection.musicError == "nokey") Str.t("music.ytNoKey") else Str.t("music.noResults"))
                items.forEach { MusicRow(it, src) }
                if (src == "youtube" && q.isNotBlank()) {
                    Spacer(Modifier.height(8.dp))
                    DialogButton(Str.t("music.openResultsOnPc"), color = YouTubeRed) { Connection.musicSearchInBrowser(q.trim()) }
                }
            }
            src == "spotify" -> {
                Connection.musicHome.forEach { sec ->
                    if (sec.title == "recent") {
                        Text(Str.t("music.recent"), color = C.Text, fontSize = 17.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 6.dp, bottom = 10.dp))
                        Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                            sec.items.forEach { CoverCard(it, src) }
                        }
                        Spacer(Modifier.height(10.dp))
                    } else {
                        Text(Str.t("music.playlists"), color = C.Text, fontSize = 17.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 10.dp, bottom = 6.dp))
                        sec.items.forEach { MusicRow(it, src) }
                    }
                }
            }
        }
    }
}

@Composable
private fun SpotifyConnectCard(hasClient: Boolean) {
    GroupCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(44.dp).clip(CircleShape).background(SpotifyGreen), contentAlignment = Alignment.Center) {
                Icon(Icons.Rounded.MusicNote, null, tint = Color.Black, modifier = Modifier.size(26.dp))
            }
            Column(Modifier.padding(start = 12.dp).weight(1f)) {
                Text("Spotify", color = C.Text, fontSize = 17.sp, fontWeight = FontWeight.Bold)
                Text(if (hasClient) Str.t("music.connectSub") else Str.t("music.needSetup"), color = C.Text2, fontSize = 13.sp, lineHeight = 17.sp)
            }
        }
        if (hasClient) {
            Spacer(Modifier.height(14.dp))
            DialogButton(Str.t("music.connect"), color = SpotifyGreen, filled = true) {
                Connection.spotifyLogin()
                Connection.showBanner(Str.t("music.loginOnPc"))
            }
        }
    }
}

private fun play(item: MusicItem, src: String) {
    when (item.kind) {
        "track", "video" -> { Connection.musicPlay(src, item); Connection.showBanner(Str.f("music.playing", item.title), BannerKind.Success) }
        "liked" -> Connection.musicOpenList("liked", "liked")
        else -> Connection.musicOpenList(item.kind, item.id)
    }
}

@Composable
private fun CoverCard(item: MusicItem, src: String) {
    Column(Modifier.width(132.dp).bounceClick(pressedScale = 0.94f) { play(item, src) }) {
        NetImage(item.img, Modifier.size(132.dp).clip(RoundedCornerShape(12.dp)))
        Text(item.title, color = C.Text, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(top = 6.dp))
        Text(item.sub, color = C.Text2, fontSize = 12.5.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

@Composable
fun MusicRow(item: MusicItem, src: String) {
    Row(Modifier.fillMaxWidth().bounceClick(pressedScale = 0.97f) { play(item, src) }.padding(vertical = 7.dp), verticalAlignment = Alignment.CenterVertically) {
        val shape = if (item.kind == "artist") CircleShape else RoundedCornerShape(8.dp)
        if (item.kind == "liked") {
            Box(Modifier.size(54.dp).clip(shape).background(Brush.linearGradient(listOf(Color(0xFF450AF5), Color(0xFFC4EFD9)))), contentAlignment = Alignment.Center) {
                Icon(Icons.Rounded.Favorite, null, tint = Color.White, modifier = Modifier.size(24.dp))
            }
        } else {
            val w = if (item.kind == "video") 86.dp else 54.dp
            NetImage(item.img, Modifier.width(w).height(54.dp).clip(shape))
        }
        Column(Modifier.weight(1f).padding(start = 12.dp)) {
            Text(if (item.kind == "liked") Str.t("music.liked") else item.title, color = C.Text, fontSize = 15.5.sp, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
            val kind = when (item.kind) { "track" -> ""; "video" -> ""; else -> Str.t("music.kind.${item.kind}") }
            Text(listOf(kind, item.sub).filter { it.isNotBlank() }.joinToString(" · "), color = C.Text2, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        Icon(if (item.kind == "video") Icons.Rounded.OpenInNew else Icons.Rounded.PlayArrow, null, tint = C.Text2, modifier = Modifier.size(22.dp))
    }
}

/** Full-screen list of a playlist / album / artist. */
@Composable
fun MusicListSheet() {
    val list = Connection.musicList
    BackHandler(list != null) { Connection.musicList = null }
    androidx.compose.animation.AnimatedVisibility(
        list != null,
        enter = slideInVertically(spring(dampingRatio = 0.86f, stiffness = 320f)) { it } + fadeIn(),
        exit = slideOutVertically(tween(240)) { it } + fadeOut(tween(240))
    ) {
        var shown by remember { mutableStateOf(list) }
        if (list != null) shown = list
        val l = shown ?: return@AnimatedVisibility
        Column(Modifier.fillMaxSize().background(C.Bg).statusBarsPadding().navigationBarsPadding()) {
            Row(Modifier.fillMaxWidth().padding(start = 6.dp, end = 12.dp, top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                Row(Modifier.bounceClick(pressedScale = 0.9f) { Connection.musicList = null }.padding(6.dp), verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Rounded.ChevronLeft, null, tint = C.Blue, modifier = Modifier.size(30.dp))
                    Text(Str.t("tab.media"), color = C.Blue, fontSize = 17.sp)
                }
            }
            Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 18.dp)) {
                Text(if (l.kind == "liked") Str.t("music.liked") else l.title.ifBlank { "…" }, color = C.Text, fontSize = 30.sp, fontWeight = FontWeight.Bold, maxLines = 2, overflow = TextOverflow.Ellipsis)
                if (l.kind != "artist" && l.items.isNotEmpty()) {
                    Spacer(Modifier.height(14.dp))
                    DialogButton(Str.t("music.playAll"), color = SpotifyGreen, filled = true) {
                        val item = if (l.kind == "liked") MusicItem("liked", "liked", "liked", "", "", "", "")
                        else MusicItem(l.kind, l.id, "spotify:${l.kind}:${l.id}", l.title, "", "", "")
                        Connection.musicPlay("spotify", item)
                        Connection.showBanner(Str.f("music.playing", item.title.ifBlank { Str.t("music.liked") }), BannerKind.Success)
                    }
                }
                Spacer(Modifier.height(10.dp))
                if (Connection.musicLoading && l.items.isEmpty()) {
                    Box(Modifier.fillMaxWidth().padding(30.dp), contentAlignment = Alignment.Center) { CircularProgressIndicator(color = SpotifyGreen, strokeWidth = 3.dp, modifier = Modifier.size(30.dp)) }
                }
                l.items.forEach { MusicRow(it, "spotify") }
                Spacer(Modifier.height(30.dp))
            }
        }
    }
}
