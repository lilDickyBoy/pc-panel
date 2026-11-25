package com.pcpanel.app.data

import android.content.Context
import android.content.SharedPreferences
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import org.json.JSONArray
import org.json.JSONObject

data class PairedPc(
    val host: String,
    val port: Int,
    val token: String,
    val pcId: String,
    val name: String,
    val mac: String,
    val bcast: String,
)

data class FavApp(val id: String, val name: String)

/**
 * A Stream-Deck button. kind: keys | app | url | text | media | power
 * value: for keys "ctrl+shift+m", app id, url, text, media action, power action.
 */
data class DeckButton(val id: String, val label: String, val kind: String, val value: String, val color: Long, val icon: String)

/** Phone-only settings, backed by SharedPreferences and exposed as Compose state. */
object Prefs {
    private lateinit var sp: SharedPreferences

    var pc by mutableStateOf<PairedPc?>(null)
        private set
    var lang by mutableStateOf("en")
        private set
    /** true when the language was changed on the phone while not connected → push it to the PC on connect */
    var langDirty = false
        private set
    var bgAlerts by mutableStateOf(false)
        private set
    var haptics by mutableStateOf(true)
        private set
    var autoDashboard by mutableStateOf(true)
        private set
    var dimDashboard by mutableStateOf(true)
        private set
    var touchSpeed by mutableStateOf(1.0f)
        private set
    var cpuAlert by mutableStateOf(90)
        private set
    var gpuAlert by mutableStateOf(85)
        private set
    var favorites by mutableStateOf<List<FavApp>>(emptyList())
        private set

    // ---- battery & connection
    /** Save battery: drop the connection after leaving the app. */
    var powerSave by mutableStateOf(true)
        private set
    /** seconds before disconnecting after leaving the app (0 = immediately) */
    var disconnectDelay by mutableStateOf(60)
        private set
    var lastActivitySeen = 0L
        private set
    /** live data refresh in ms: 1000 / 2000 / 5000 */
    var refreshMs by mutableStateOf(1000)
        private set

    // ---- phone → PC features
    var mirrorNotifs by mutableStateOf(false)
        private set
    var mirrorCalls by mutableStateOf(true)
        private set
    var mutedApps by mutableStateOf<Set<String>>(emptySet())
        private set
    var deck by mutableStateOf<List<DeckButton>>(emptyList())
        private set

    fun init(ctx: Context) {
        sp = ctx.getSharedPreferences("mobilink", Context.MODE_PRIVATE)
        val host = sp.getString("host", null)
        val token = sp.getString("token", null)
        if (host != null && token != null) {
            pc = PairedPc(
                host, sp.getInt("port", 47800), token,
                sp.getString("pcId", "") ?: "", sp.getString("pcName", "PC") ?: "PC",
                sp.getString("mac", "") ?: "", sp.getString("bcast", "255.255.255.255") ?: "255.255.255.255"
            )
        }
        lang = sp.getString("lang", null) ?: java.util.Locale.getDefault().language.let {
            if (it in listOf("en", "tr", "de", "fr", "es", "it", "pt", "ru", "pl", "az", "ja", "zh")) it else "en"
        }
        langDirty = sp.getBoolean("langDirty", false)
        bgAlerts = sp.getBoolean("bgAlerts", false)
        haptics = sp.getBoolean("haptics", true)
        autoDashboard = sp.getBoolean("autoDashboard", true)
        dimDashboard = sp.getBoolean("dimDashboard", true)
        touchSpeed = sp.getFloat("touchSpeed", 1.0f)
        cpuAlert = sp.getInt("cpuAlert", 90)
        gpuAlert = sp.getInt("gpuAlert", 85)
        powerSave = sp.getBoolean("powerSave", true)
        disconnectDelay = sp.getInt("disconnectDelay2", 60)
        lastActivitySeen = sp.getLong("lastActivitySeen", 0L)
        refreshMs = sp.getInt("refreshMs", 1000)
        mirrorNotifs = sp.getBoolean("mirrorNotifs", false)
        mirrorCalls = sp.getBoolean("mirrorCalls", true)
        mutedApps = sp.getStringSet("mutedApps", emptySet())?.toSet() ?: emptySet()
        favorites = try {
            val arr = JSONArray(sp.getString("favorites", "[]"))
            (0 until arr.length()).map { arr.getJSONObject(it).let { o -> FavApp(o.getString("id"), o.getString("name")) } }
        } catch (_: Exception) { emptyList() }
        deck = try {
            val raw = sp.getString("deck", null)
            if (raw == null) defaultDeck() else {
                val arr = JSONArray(raw)
                (0 until arr.length()).map { arr.getJSONObject(it).let { o ->
                    DeckButton(o.getString("id"), o.getString("label"), o.getString("kind"), o.getString("value"), o.optLong("color", 0xFF0A84FF), o.optString("icon", "bolt"))
                } }
            }
        } catch (_: Exception) { defaultDeck() }
    }

    private fun defaultDeck() = listOf(
        DeckButton("d1", "Discord mute", "keys", "ctrl+shift+m", 0xFF5E5CE6, "mic"),
        DeckButton("d2", "Task Manager", "keys", "ctrl+shift+escape", 0xFF30D158, "chart"),
        DeckButton("d3", "Desktop", "keys", "win+d", 0xFF0A84FF, "desktop"),
        DeckButton("d4", "Snip", "keys", "win+shift+s", 0xFFFF9F0A, "crop"),
        DeckButton("d5", "YouTube", "url", "https://www.youtube.com", 0xFFFF3B30, "play"),
        DeckButton("d6", "Explorer", "keys", "win+e", 0xFF64D2FF, "folder"),
    )

    fun savePc(p: PairedPc?) {
        pc = p
        sp.edit().apply {
            if (p == null) {
                remove("host"); remove("token"); remove("pcId"); remove("pcName"); remove("mac"); remove("bcast")
            } else {
                putString("host", p.host); putInt("port", p.port); putString("token", p.token)
                putString("pcId", p.pcId); putString("pcName", p.name); putString("mac", p.mac); putString("bcast", p.bcast)
            }
        }.apply()
    }

    fun updateHost(host: String) { pc?.let { savePc(it.copy(host = host)) } }
    fun updatePcInfo(name: String?, mac: String?, bcast: String?) {
        pc?.let {
            savePc(it.copy(
                name = if (name.isNullOrBlank()) it.name else name,
                mac = if (mac.isNullOrBlank()) it.mac else mac,
                bcast = if (bcast.isNullOrBlank()) it.bcast else bcast
            ))
        }
    }

    /** @param fromUser true when picked on the phone (→ sent to the PC) */
    fun updateLang(v: String, fromUser: Boolean, connected: Boolean) {
        lang = v
        langDirty = fromUser && !connected
        sp.edit().putString("lang", v).putBoolean("langDirty", langDirty).apply()
    }
    fun clearLangDirty() { langDirty = false; sp.edit().putBoolean("langDirty", false).apply() }

    fun updateBgAlerts(v: Boolean) { bgAlerts = v; sp.edit().putBoolean("bgAlerts", v).apply() }
    fun updateHaptics(v: Boolean) { haptics = v; sp.edit().putBoolean("haptics", v).apply() }
    fun updateAutoDashboard(v: Boolean) { autoDashboard = v; sp.edit().putBoolean("autoDashboard", v).apply() }
    fun updateDimDashboard(v: Boolean) { dimDashboard = v; sp.edit().putBoolean("dimDashboard", v).apply() }
    fun updateTouchSpeed(v: Float) { touchSpeed = v; sp.edit().putFloat("touchSpeed", v).apply() }
    fun updateAlerts(cpu: Int, gpu: Int) { cpuAlert = cpu; gpuAlert = gpu; sp.edit().putInt("cpuAlert", cpu).putInt("gpuAlert", gpu).apply() }
    fun updatePowerSave(v: Boolean) { powerSave = v; sp.edit().putBoolean("powerSave", v).apply() }
    fun updateLastActivitySeen(v: Long) { lastActivitySeen = v; sp.edit().putLong("lastActivitySeen", v).apply() }
    fun updateDisconnectDelay(v: Int) { disconnectDelay = v; sp.edit().putInt("disconnectDelay2", v).apply() }
    fun updateRefreshMs(v: Int) { refreshMs = v; sp.edit().putInt("refreshMs", v).apply() }
    fun updateMirrorNotifs(v: Boolean) { mirrorNotifs = v; sp.edit().putBoolean("mirrorNotifs", v).apply() }
    fun updateMirrorCalls(v: Boolean) { mirrorCalls = v; sp.edit().putBoolean("mirrorCalls", v).apply() }
    fun toggleMutedApp(pkg: String) {
        mutedApps = if (pkg in mutedApps) mutedApps - pkg else mutedApps + pkg
        sp.edit().putStringSet("mutedApps", mutedApps).apply()
    }

    fun saveFavorites(list: List<FavApp>) {
        favorites = list
        val arr = JSONArray()
        list.forEach { arr.put(JSONObject().put("id", it.id).put("name", it.name)) }
        sp.edit().putString("favorites", arr.toString()).apply()
    }

    fun toggleFavorite(app: FavApp) {
        val cur = favorites
        saveFavorites(if (cur.any { it.id == app.id }) cur.filterNot { it.id == app.id } else cur + app)
    }

    fun saveDeck(list: List<DeckButton>) {
        deck = list
        val arr = JSONArray()
        list.forEach { arr.put(JSONObject().put("id", it.id).put("label", it.label).put("kind", it.kind).put("value", it.value).put("color", it.color).put("icon", it.icon)) }
        sp.edit().putString("deck", arr.toString()).apply()
    }
}
