package com.pcpanel.app.net

import androidx.compose.ui.graphics.Color
import org.json.JSONObject

data class Fan(val name: String, val rpm: Int)
data class Disk(val name: String, val label: String, val used: Long, val total: Long)

data class Stats(
    val cpuName: String = "",
    val cpuLoad: Float = 0f,
    val cpuTemp: Float = -1f,
    val cpuClock: Float = -1f,
    val cpuPower: Float = -1f,
    val gpuName: String = "",
    val gpuLoad: Float = 0f,
    val gpuTemp: Float = -1f,
    val gpuHot: Float = -1f,
    val gpuClock: Float = -1f,
    val gpuPower: Float = -1f,
    val gpuMemUsed: Float = -1f,
    val gpuMemTotal: Float = -1f,
    val gpuFan: Float = -1f,
    val gpuFanPct: Float = -1f,
    val ramUsed: Long = 0,
    val ramTotal: Long = 1,
    val fans: List<Fan> = emptyList(),
    val disks: List<Disk> = emptyList(),
    val netDown: Long = 0,
    val netUp: Long = 0,
    val uptime: Long = 0,
    val limited: Boolean = false,
    val game: Boolean = false,
    val fanGpu: FanGroup = FanGroup(),
    val fanCase: FanGroup = FanGroup(),
    val fanCtlEnabled: Boolean = false,
) {
    val ramPct: Float get() = if (ramTotal > 0) ramUsed * 100f / ramTotal else 0f

    companion object {
        fun parse(o: JSONObject): Stats {
            val c = o.optJSONObject("cpu") ?: JSONObject()
            val g = o.optJSONObject("gpu") ?: JSONObject()
            val r = o.optJSONObject("ram") ?: JSONObject()
            val n = o.optJSONObject("net") ?: JSONObject()
            val fans = o.optJSONArray("fans")?.let { a -> (0 until a.length()).map { a.getJSONObject(it).let { f -> Fan(f.optString("n"), f.optInt("rpm")) } } } ?: emptyList()
            val disks = o.optJSONArray("disks")?.let { a ->
                (0 until a.length()).map { a.getJSONObject(it).let { d -> Disk(d.optString("n"), d.optString("label"), d.optLong("used"), d.optLong("total")) } }
            } ?: emptyList()
            fun JSONObject.f(k: String) = optDouble(k, -1.0).toFloat()
            return Stats(
                cpuName = c.optString("name"), cpuLoad = c.f("load").coerceAtLeast(0f), cpuTemp = c.f("temp"),
                cpuClock = c.f("clock"), cpuPower = c.f("power"),
                gpuName = g.optString("name"), gpuLoad = g.f("load").coerceAtLeast(0f), gpuTemp = g.f("temp"), gpuHot = g.f("hot"),
                gpuClock = g.f("clock"), gpuPower = g.f("power"), gpuMemUsed = g.f("memUsed"), gpuMemTotal = g.f("memTotal"),
                gpuFan = g.f("fan"), gpuFanPct = g.f("fanPct"),
                ramUsed = r.optLong("used"), ramTotal = r.optLong("total", 1).coerceAtLeast(1),
                fans = fans, disks = disks,
                netDown = n.optLong("down"), netUp = n.optLong("up"),
                uptime = o.optLong("uptime"), limited = o.optBoolean("limited"),
                game = o.optBoolean("game"),
                fanGpu = FanGroup.parse(o.optJSONObject("fanCtl")?.optJSONObject("gpu")),
                fanCase = FanGroup.parse(o.optJSONObject("fanCtl")?.optJSONObject("case")),
                fanCtlEnabled = o.optJSONObject("fanCtl")?.optBoolean("enabled") ?: false,
            )
        }
    }
}

data class MediaState(
    val has: Boolean = false,
    val title: String = "",
    val artist: String = "",
    val album: String = "",
    val app: String = "",
    val playing: Boolean = false,
    val pos: Double = 0.0,
    val dur: Double = 0.0,
    val artId: Int = 0,
    val color: Color = Color(0xFF0A84FF),
    val receivedAt: Long = 0L,
) {
    /** Position extrapolated from the time we received it. */
    fun positionNow(): Double {
        var p = pos
        if (playing) p += (System.currentTimeMillis() - receivedAt) / 1000.0
        if (dur > 0) p = p.coerceAtMost(dur)
        return p.coerceAtLeast(0.0)
    }

    companion object {
        fun parse(o: JSONObject): MediaState {
            val col = try { Color(android.graphics.Color.parseColor(o.optString("color", "#0A84FF"))) } catch (_: Exception) { Color(0xFF0A84FF) }
            return MediaState(
                has = o.optBoolean("has"), title = o.optString("title"), artist = o.optString("artist"),
                album = o.optString("album"), app = o.optString("app"), playing = o.optBoolean("playing"),
                pos = o.optDouble("pos", 0.0), dur = o.optDouble("dur", 0.0), artId = o.optInt("artId"),
                color = col, receivedAt = System.currentTimeMillis()
            )
        }
    }
}

data class TimerState(val active: Boolean = false, val action: String = "", val remaining: Int = 0, val receivedAt: Long = 0L) {
    fun remainingNow(): Int = if (!active) 0 else (remaining - ((System.currentTimeMillis() - receivedAt) / 1000).toInt()).coerceAtLeast(0)
}

data class FanGroup(val mode: String = "auto", val pct: Int = 60, val available: Boolean = false) {
    companion object {
        fun parse(o: JSONObject?): FanGroup = if (o == null) FanGroup() else
            FanGroup(o.optString("mode", "auto"), o.optDouble("pct", 60.0).toInt(), o.optBoolean("available"))
    }
}

/** Settings shared with (and owned by) the PC. */
data class PcSettings(
    val known: Boolean = false,
    val lang: String = "en",
    val pcName: String = "",
    val notchOn: Boolean = true,
    val notchStyle: String = "notch",
    val notchMode: String = "auto",
    val notchFs: Boolean = true,
    val notchVol: Boolean = true,
    val notchConn: Boolean = true,
    val notchNotif: Boolean = true,
    val notchCall: Boolean = true,
    val notchDrop: Boolean = true,
    val notchCharge: Boolean = true,
    val sounds: Boolean = true,
    val soundVol: Int = 70,
    val clipSync: Boolean = true,
    val autoLock: Boolean = false,
    val shotCursor: Boolean = false,
    val gameMode: Boolean = true,
    val tempHistory: Boolean = true,
    val fanCtl: Boolean = true,
    val musicSrc: String = "spotify",
    val cpuAlert: Int = 90,
    val gpuAlert: Int = 85,
    val autostart: Boolean = true,
    val spotify: Boolean = false,
    val spotifyUser: String = "",
    val spotifyClient: Boolean = false,
    val ytKey: Boolean = false,
    val version: String = "",
) {
    companion object {
        fun parse(o: JSONObject) = PcSettings(
            known = true,
            lang = o.optString("lang", "en"), pcName = o.optString("pcName"),
            notchOn = o.optBoolean("notchOn", true), notchStyle = o.optString("notchStyle", "notch"), notchMode = o.optString("notchMode", "auto"),
            notchFs = o.optBoolean("notchFs", true), notchVol = o.optBoolean("notchVol", true), notchConn = o.optBoolean("notchConn", true),
            notchNotif = o.optBoolean("notchNotif", true), notchCall = o.optBoolean("notchCall", true), notchDrop = o.optBoolean("notchDrop", true), notchCharge = o.optBoolean("notchCharge", true),
            sounds = o.optBoolean("sounds", true), soundVol = o.optInt("soundVol", 70), clipSync = o.optBoolean("clipSync", true),
            autoLock = o.optBoolean("autoLock"), shotCursor = o.optBoolean("shotCursor"), gameMode = o.optBoolean("gameMode", true),
            tempHistory = o.optBoolean("tempHistory", true), fanCtl = o.optBoolean("fanCtl", true), musicSrc = o.optString("musicSrc", "spotify"),
            cpuAlert = o.optInt("cpuAlert", 90), gpuAlert = o.optInt("gpuAlert", 85), autostart = o.optBoolean("autostart", true),
            spotify = o.optBoolean("spotify"), spotifyUser = o.optString("spotifyUser"), spotifyClient = o.optBoolean("spotifyClient"),
            ytKey = o.optBoolean("ytKey"), version = o.optString("version"),
        )
    }
}

data class MixerApp(val id: String, val name: String, val vol: Float, val mute: Boolean, val active: Boolean, val iconB64: String)
data class AudioOut(val id: String, val name: String, val isDefault: Boolean)
data class MusicItem(val kind: String, val id: String, val uri: String, val title: String, val sub: String, val img: String, val ctx: String)
data class MusicSection(val title: String, val items: List<MusicItem>)
data class MusicList(val kind: String, val id: String, val title: String, val items: List<MusicItem>)
data class CountdownState(val active: Boolean = false, val remaining: Int = 0, val total: Int = 0, val label: String = "", val receivedAt: Long = 0L) {
    fun remainingNow(): Int = if (!active) 0 else (remaining - ((System.currentTimeMillis() - receivedAt) / 1000).toInt()).coerceAtLeast(0)
}
data class TempHistory(val step: Int = 10, val cpu: List<Float> = emptyList(), val gpu: List<Float> = emptyList())
data class Transfer(val id: String, val name: String, val size: Long, val done: Long, val incoming: Boolean, val finished: Boolean = false, val failed: Boolean = false)

fun parseMusicItems(a: org.json.JSONArray?): List<MusicItem> {
    if (a == null) return emptyList()
    return (0 until a.length()).mapNotNull { i ->
        val o = a.optJSONObject(i) ?: return@mapNotNull null
        MusicItem(o.optString("kind"), o.optString("id"), o.optString("uri"), o.optString("title"), o.optString("sub"), o.optString("img"), o.optString("ctx"))
    }
}

data class ProcInfo(val name: String, val mem: Long, val count: Int, val locked: Boolean)
data class PcApp(val id: String, val name: String)

fun fmtBytes(b: Long): String {
    val v = b.toDouble()
    return when {
        v >= 1e12 -> String.format("%.1f TB", v / 1e12)
        v >= 1e9 -> String.format("%.1f GB", v / 1e9)
        v >= 1e6 -> String.format("%.0f MB", v / 1e6)
        v >= 1e3 -> String.format("%.0f KB", v / 1e3)
        else -> "$b B"
    }
}

fun fmtSpeed(bps: Long): String {
    val bits = bps * 8.0
    return when {
        bits >= 1e9 -> String.format("%.2f Gbps", bits / 1e9)
        bits >= 1e6 -> String.format("%.1f Mbps", bits / 1e6)
        bits >= 1e3 -> String.format("%.0f Kbps", bits / 1e3)
        else -> String.format("%.0f bps", bits)
    }
}

fun fmtTime(sec: Double): String {
    val s = sec.toLong().coerceAtLeast(0)
    val h = s / 3600; val m = (s % 3600) / 60; val ss = s % 60
    return if (h > 0) String.format("%d:%02d:%02d", h, m, ss) else String.format("%d:%02d", m, ss)
}

fun fmtUptime(sec: Long): String {
    val d = sec / 86400; val h = (sec % 86400) / 3600; val m = (sec % 3600) / 60
    return when { d > 0 -> "${d}d ${h}h"; h > 0 -> "${h}h ${m}m"; else -> "${m}m" }
}
