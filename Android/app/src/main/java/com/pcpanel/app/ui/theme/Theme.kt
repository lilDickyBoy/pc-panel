package com.pcpanel.app.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

/** iOS dark palette. Pure black background = less battery on the AMOLED screen. */
object C {
    val Bg = Color(0xFF000000)
    val Card = Color(0xFF1C1C1E)
    val Card2 = Color(0xFF2C2C2E)
    val Card3 = Color(0xFF3A3A3C)
    val Sep = Color(0xFF38383A)
    val Text = Color(0xFFFFFFFF)
    val Text2 = Color(0xFF8E8E93)
    val Text3 = Color(0xFF636366)
    val Blue = Color(0xFF0A84FF)
    val Green = Color(0xFF30D158)
    val Orange = Color(0xFFFF9F0A)
    val Red = Color(0xFFFF453A)
    val Purple = Color(0xFFBF5AF2)
    val Indigo = Color(0xFF5E5CE6)
    val Teal = Color(0xFF64D2FF)
    val Yellow = Color(0xFFFFD60A)
    val Pink = Color(0xFFFF375F)

    fun temp(t: Float): Color = when {
        t < 0 -> Text2
        t < 55 -> Green
        t < 70 -> Yellow
        t < 82 -> Orange
        else -> Red
    }
}

@Composable
fun PcPanelTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = C.Blue,
            background = C.Bg,
            surface = C.Card,
            onSurface = C.Text,
            onBackground = C.Text,
        ),
        content = content
    )
}
