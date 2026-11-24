package com.pcpanel.app.ui

import android.content.Context
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue
import com.pcpanel.app.data.Prefs
import org.json.JSONObject

/**
 * Translations. The JSON files live in /i18n at the repo root and are shared with the PC app.
 * Missing keys fall back to English. Reading [t] inside a composable recomposes it on language change.
 */
object Str {
    val languages = listOf(
        "en" to "English", "tr" to "Türkçe", "de" to "Deutsch", "fr" to "Français",
        "es" to "Español", "it" to "Italiano", "pt" to "Português", "ru" to "Русский",
        "pl" to "Polski", "az" to "Azərbaycan", "ja" to "日本語", "zh" to "中文",
    )

    private lateinit var appCtx: Context
    private var en: Map<String, String> = emptyMap()
    private var cur: Map<String, String> = emptyMap()
    private var version by mutableIntStateOf(0)
    var lang: String = "en"
        private set

    fun init(ctx: Context) {
        appCtx = ctx.applicationContext
        en = read("en")
        load(Prefs.lang)
    }

    fun isSupported(code: String) = languages.any { it.first == code }

    fun systemLanguage(): String {
        val l = java.util.Locale.getDefault().language
        return if (isSupported(l)) l else "en"
    }

    private fun read(code: String): Map<String, String> = try {
        val txt = appCtx.assets.open("$code.json").bufferedReader().use { it.readText() }
        val o = JSONObject(txt)
        val m = HashMap<String, String>(o.length() * 2)
        o.keys().forEach { k -> m[k] = o.optString(k) }
        m
    } catch (_: Exception) { emptyMap() }

    fun load(code: String) {
        val c = if (isSupported(code)) code else "en"
        lang = c
        cur = if (c == "en") en else read(c)
        version++
    }

    fun t(key: String): String {
        @Suppress("UNUSED_VARIABLE") val v = version // recompose on language change
        return cur[key]?.takeIf { it.isNotEmpty() } ?: en[key] ?: key
    }

    /** t(key) with {0}, {1}… replaced. */
    fun f(key: String, vararg args: Any?): String {
        var s = t(key)
        args.forEachIndexed { i, a -> s = s.replace("{$i}", a?.toString() ?: "") }
        return s
    }
}
