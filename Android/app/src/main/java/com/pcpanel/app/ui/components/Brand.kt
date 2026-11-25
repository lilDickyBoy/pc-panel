package com.pcpanel.app.ui.components

import androidx.annotation.DrawableRes
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.pcpanel.app.R
import com.pcpanel.app.ui.theme.C

/** Brand logos (the ones you sent) for Spotify, YouTube, Discord, WhatsApp, Bluetooth. */
object BrandRes {
    val spotify = R.drawable.brand_spotify
    val spotifyWhite = R.drawable.brand_spotify_white
    val youtube = R.drawable.brand_youtube
    val discord = R.drawable.brand_discord
    val whatsapp = R.drawable.brand_whatsapp
    val bluetooth = R.drawable.brand_bluetooth
    val SpotifyGreen = Color(0xFF1ED760)
    val Dark = Color(0xFF151517)
}

@Composable
fun BrandTile(@DrawableRes res: Int, bg: Color, size: Dp = 30.dp, pad: Float = 0.18f) {
    Box(Modifier.size(size).clip(RoundedCornerShape(size * 0.27f)).background(bg), contentAlignment = Alignment.Center) {
        Image(painterResource(res), null, modifier = Modifier.size(size * (1 - 2 * pad)))
    }
}

@Composable
fun BrandSwitchRow(@DrawableRes res: Int, bg: Color, title: String, checked: Boolean, subtitle: String? = null, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
        BrandTile(res, bg)
        Column(Modifier.weight(1f).padding(start = 12.dp, end = 12.dp)) {
            Text(title, color = C.Text, fontSize = 16.sp)
            if (subtitle != null) Text(subtitle, color = C.Text2, fontSize = 12.5.sp, lineHeight = 16.sp, modifier = Modifier.padding(top = 2.dp))
        }
        IosSwitch(checked, onChange)
    }
}

@Composable
fun BrandRow(@DrawableRes res: Int, bg: Color, title: String, subtitle: String? = null, content: @Composable () -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
        BrandTile(res, bg)
        Column(Modifier.weight(1f).padding(start = 12.dp, end = 12.dp)) {
            Text(title, color = C.Text, fontSize = 16.sp)
            if (subtitle != null) Text(subtitle, color = C.Text2, fontSize = 12.5.sp, lineHeight = 16.sp, modifier = Modifier.padding(top = 2.dp))
        }
        content()
    }
}

/** Choice chip with a logo, e.g. the Spotify / YouTube picker. */
@Composable
fun BrandChip(@DrawableRes res: Int, text: String, selected: Boolean, accent: Color, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Row(
        modifier
            .bounceClick(pressedScale = 0.94f, onClick = onClick)
            .clip(RoundedCornerShape(14.dp))
            .background(if (selected) accent.copy(alpha = 0.18f) else C.Card2)
            .border(1.5.dp, if (selected) accent.copy(alpha = 0.7f) else Color.Transparent, RoundedCornerShape(14.dp))
            .padding(horizontal = 14.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Image(painterResource(res), null, modifier = Modifier.size(20.dp))
        Text(text, color = if (selected) C.Text else C.Text2, fontSize = 15.sp, fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Medium, modifier = Modifier.padding(start = 8.dp))
    }
}
