package com.pcpanel.app.ui.components

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.LruCache
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ChevronLeft
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.ui.theme.C
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.util.Base64
import java.util.concurrent.TimeUnit

// ====================================================================== tiny image loader (album covers, thumbnails)
object NetImages {
    private val http = OkHttpClient.Builder().connectTimeout(8, TimeUnit.SECONDS).readTimeout(12, TimeUnit.SECONDS).build()
    private val cache = object : LruCache<String, Bitmap>(12 * 1024 * 1024) {
        override fun sizeOf(key: String, value: Bitmap) = value.byteCount
    }

    fun cached(url: String): Bitmap? = cache.get(url)

    suspend fun load(url: String): Bitmap? = withContext(Dispatchers.IO) {
        cache.get(url)?.let { return@withContext it }
        try {
            http.newCall(Request.Builder().url(url).build()).execute().use { r ->
                if (!r.isSuccessful) return@use null
                val bytes = r.body?.bytes() ?: return@use null
                val opts = BitmapFactory.Options().apply { inPreferredConfig = Bitmap.Config.RGB_565 }
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size, opts)?.also { cache.put(url, it) }
            }
        } catch (_: Exception) { null }
    }
}

/** Image from the internet with a soft fade-in. */
@Composable
fun NetImage(url: String, modifier: Modifier = Modifier, placeholder: Color = C.Card2) {
    var bmp by remember(url) { mutableStateOf(NetImages.cached(url)?.asImageBitmap()) }
    LaunchedEffect(url) { if (bmp == null && url.isNotBlank()) bmp = NetImages.load(url)?.asImageBitmap() }
    val alpha by animateFloatAsState(if (bmp != null) 1f else 0f, tween(260), label = "img")
    Box(modifier.background(placeholder)) {
        bmp?.let { Image(it, null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize().graphicsLayer { this.alpha = alpha }) }
    }
}

/** base64 PNG → ImageBitmap (remembered). */
@Composable
fun rememberB64Image(b64: String): ImageBitmap? = remember(b64) {
    if (b64.isBlank()) null else try {
        val b = Base64.getDecoder().decode(b64)
        BitmapFactory.decodeByteArray(b, 0, b.size)?.asImageBitmap()
    } catch (_: Exception) { null }
}

// ====================================================================== rows
@Composable
fun IconTile(icon: ImageVector, bg: Color, size: Dp = 30.dp) {
    Box(Modifier.size(size).clip(RoundedCornerShape(size * 0.27f)).background(bg), contentAlignment = Alignment.Center) {
        Icon(icon, null, tint = Color.White, modifier = Modifier.size(size * 0.6f))
    }
}

@Composable
fun IconSwitchRow(icon: ImageVector, bg: Color, title: String, checked: Boolean, subtitle: String? = null, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
        IconTile(icon, bg)
        Column(Modifier.weight(1f).padding(start = 12.dp, end = 12.dp)) {
            Text(title, color = C.Text, fontSize = 16.sp)
            if (subtitle != null) Text(subtitle, color = C.Text2, fontSize = 12.5.sp, lineHeight = 16.sp, modifier = Modifier.padding(top = 2.dp))
        }
        IosSwitch(checked, onChange)
    }
}

/** A row with an icon, a title and any control on the right (or below when [below] = true). */
@Composable
fun IconRow(icon: ImageVector, bg: Color, title: String, subtitle: String? = null, below: Boolean = false, content: @Composable () -> Unit) {
    if (below) {
        Column(Modifier.fillMaxWidth().padding(vertical = 10.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconTile(icon, bg)
                Column(Modifier.weight(1f).padding(start = 12.dp)) {
                    Text(title, color = C.Text, fontSize = 16.sp)
                    if (subtitle != null) Text(subtitle, color = C.Text2, fontSize = 12.5.sp, lineHeight = 16.sp, modifier = Modifier.padding(top = 2.dp))
                }
            }
            Box(Modifier.padding(top = 10.dp)) { content() }
        }
    } else {
        Row(Modifier.fillMaxWidth().padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
            IconTile(icon, bg)
            Column(Modifier.weight(1f).padding(start = 12.dp, end = 12.dp)) {
                Text(title, color = C.Text, fontSize = 16.sp)
                if (subtitle != null) Text(subtitle, color = C.Text2, fontSize = 12.5.sp, lineHeight = 16.sp, modifier = Modifier.padding(top = 2.dp))
            }
            content()
        }
    }
}

@Composable
fun Hint(text: String, modifier: Modifier = Modifier) {
    Text(text, color = C.Text2, fontSize = 13.sp, lineHeight = 18.sp, modifier = modifier.padding(horizontal = 6.dp, vertical = 6.dp))
}

// ====================================================================== sub page (iOS navigation style)
@Composable
fun SubPage(title: String, backLabel: String, onBack: () -> Unit, scroll: Boolean = true, actions: (@Composable () -> Unit)? = null, content: @Composable ColumnScope.() -> Unit) {
    Column(Modifier.fillMaxSize().background(C.Bg).statusBarsPadding()) {
        Row(Modifier.fillMaxWidth().padding(start = 6.dp, end = 12.dp, top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            Row(Modifier.bounceClick(pressedScale = 0.9f, onClick = onBack).padding(6.dp), verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Rounded.ChevronLeft, null, tint = C.Blue, modifier = Modifier.size(30.dp))
                Text(backLabel, color = C.Blue, fontSize = 17.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Box(Modifier.weight(1f))
            actions?.invoke()
        }
        Text(title, color = C.Text, fontSize = 32.sp, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(horizontal = 18.dp, vertical = 4.dp))
        val m = Modifier.fillMaxSize().padding(horizontal = 16.dp)
        Column(if (scroll) m.verticalScroll(rememberScrollState()) else m, verticalArrangement = Arrangement.Top, content = content)
    }
}

/** Big rounded choice chip used for single-choice lists. */
@Composable
fun Chip(text: String, selected: Boolean, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Box(
        modifier
            .bounceClick(pressedScale = 0.94f, onClick = onClick)
            .clip(RoundedCornerShape(12.dp))
            .background(if (selected) C.Blue.copy(alpha = 0.22f) else C.Card2)
            .padding(horizontal = 14.dp, vertical = 10.dp),
        contentAlignment = Alignment.Center
    ) {
        Text(text, color = if (selected) C.Blue else C.Text, fontSize = 14.5.sp, fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Medium, maxLines = 1)
    }
}
