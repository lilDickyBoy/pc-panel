package com.pcpanel.app.net

import android.app.NotificationManager
import android.content.ContentValues
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.provider.MediaStore
import android.provider.Settings
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.core.app.NotificationCompat
import com.pcpanel.app.PcPanelApp
import com.pcpanel.app.R
import com.pcpanel.app.data.PairedPc
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.service.PhoneLink
import com.pcpanel.app.service.Ringer
import com.pcpanel.app.service.Transfers
import com.pcpanel.app.ui.Str
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.SocketTimeoutException
import java.security.MessageDigest
import java.util.Base64
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicLong

enum class ConnState { Unpaired, Idle, Connecting, Connected, Offline, AuthFailed, Waking }

enum class BannerKind { Info, Success, Error, Warning }
data class Banner(val id: Long, val text: String, val kind: BannerKind = BannerKind.Info)

/**
 * Single connection to the PC. All public state is Compose state, updated on the main thread.
 */
object Connection {
    const val PORT = 47800
    const val DISCOVERY_PORT = 47801

    private lateinit var ctx: Context
    private val main = Handler(Looper.getMainLooper())
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val client = OkHttpClient.Builder()
        .pingInterval(10, TimeUnit.SECONDS)
        .connectTimeout(3, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .build()

    // ------------------------------------------------------------------ observable state
    var state by mutableStateOf(ConnState.Idle)
        private set
    var detail by mutableStateOf("")
        private set
    var stats by mutableStateOf(Stats())
        private set
    var cpuHist by mutableStateOf(List(60) { 0f })
        private set
    var gpuHist by mutableStateOf(List(60) { 0f })
        private set
    var hasStats by mutableStateOf(false)
        private set
    var media by mutableStateOf(MediaState())
        private set
    var art by mutableStateOf<ImageBitmap?>(null)
        private set
    var volume by mutableStateOf(0.5f)
        private set
    var muted by mutableStateOf(false)
        private set
    var outputName by mutableStateOf("")
        private set
    var timer by mutableStateOf(TimerState())
        private set
    var procs by mutableStateOf<List<ProcInfo>>(emptyList())
        private set
    var apps by mutableStateOf<List<PcApp>>(emptyList())
        private set
    var appsLoading by mutableStateOf(false)
        private set
    val icons = mutableStateMapOf<String, ImageBitmap>()
    var brightness by mutableStateOf(-2)   // -2 unknown, -1 unsupported
        private set
    var night by mutableStateOf(0)
        private set
    var screenshot by mutableStateOf<ImageBitmap?>(null)
    var banner by mutableStateOf<Banner?>(null)
        private set

    // v2
    var pcSettings by mutableStateOf(PcSettings())
        private set
    var pcVersion by mutableStateOf("")
        private set
    var mixer by mutableStateOf<List<MixerApp>>(emptyList())
        private set
    var outputs by mutableStateOf<List<AudioOut>>(emptyList())
        private set
    var frame by mutableStateOf<ImageBitmap?>(null)
        private set
    var frameSize by mutableStateOf(Pair(16, 9))
        private set
    var countdown by mutableStateOf(CountdownState())
        private set
    var history by mutableStateOf(TempHistory())
        private set
    var inGame by mutableStateOf(false)
        private set
    var musicHome by mutableStateOf<List<MusicSection>>(emptyList())
        private set
    var musicResults by mutableStateOf<List<MusicItem>>(emptyList())
        private set
    var musicList by mutableStateOf<MusicList?>(null)
    var musicLoading by mutableStateOf(false)
    var musicError by mutableStateOf("")

    // ------------------------------------------------------------------ internals
    private var ws: WebSocket? = null
    private var crypto: CryptoBox? = null
    private val seq = AtomicLong(0)
    @Volatile private var gen = 0
    @Volatile private var userDisconnected = false
    @Volatile var foreground = true
        private set
    @Volatile private var lastBye: String? = null
    private var reconnectJob: Job? = null
    private var softDisconnectJob: Job? = null
    private var backoff = 1000L
    private val iconRequested = HashSet<String>()
    private val pending = ArrayList<() -> Unit>()

    val connected get() = state == ConnState.Connected

    fun init(c: Context) {
        ctx = c.applicationContext
        state = if (Prefs.pc == null) ConnState.Unpaired else ConnState.Idle
    }

    fun ui(block: () -> Unit) {
        if (Looper.myLooper() == Looper.getMainLooper()) block() else main.post(block)
    }

    fun showBanner(text: String, kind: BannerKind = BannerKind.Info) = ui {
        banner = Banner(System.nanoTime(), text, kind)
    }

    private fun setState(s: ConnState, d: String = "") = ui { state = s; detail = d }

    private fun deviceName(): String {
        val n = try { Settings.Global.getString(ctx.contentResolver, "device_name") } catch (_: Exception) { null }
        return if (!n.isNullOrBlank()) n else "${Build.MANUFACTURER.replaceFirstChar { it.uppercase() }} ${Build.MODEL}"
    }

    /** Run [action] now if connected, otherwise connect and run it right after the handshake. */
    fun whenConnected(action: () -> Unit) {
        if (connected) { action(); return }
        synchronized(pending) { pending.add(action) }
        if (Prefs.pc != null) connect(manual = true)
    }

    // ------------------------------------------------------------------ pairing
    fun pairFromUri(text: String): Boolean {
        return try {
            val u = Uri.parse(text.trim())
            if ((u.scheme != "mobilink" && u.scheme != "pcpanel") || u.host != "pair") return false
            val pc = PairedPc(
                host = u.getQueryParameter("h") ?: return false,
                port = u.getQueryParameter("p")?.toIntOrNull() ?: PORT,
                token = u.getQueryParameter("k") ?: return false,
                pcId = u.getQueryParameter("i") ?: "",
                name = u.getQueryParameter("n") ?: "PC",
                mac = u.getQueryParameter("m") ?: "",
                bcast = u.getQueryParameter("b") ?: "255.255.255.255",
            )
            disconnectInternal()
            Prefs.savePc(pc)
            userDisconnected = false
            state = ConnState.Idle
            connect(manual = true)
            true
        } catch (_: Exception) { false }
    }

    fun forget() {
        disconnect()
        Prefs.savePc(null)
        icons.clear(); iconRequested.clear()
        ui { state = ConnState.Unpaired; detail = ""; pcSettings = PcSettings() }
        PcPanelApp.stopAlertService(ctx)
    }

    // ------------------------------------------------------------------ lifecycle
    /** Keep the socket while the app is in the background? */
    private val keepInBackground get() = Prefs.bgAlerts || !Prefs.powerSave

    fun setForeground(fg: Boolean) {
        foreground = fg
        if (fg) {
            softDisconnectJob?.cancel(); softDisconnectJob = null
            if (state == ConnState.Connected) {
                send(JSONObject().put("type", "focus").put("active", true))
                PhoneLink.onForeground(ctx)
            } else if (Prefs.pc != null && !userDisconnected && state != ConnState.AuthFailed) connect()
        } else {
            if (state == ConnState.Connected) send(JSONObject().put("type", "focus").put("active", false))
            stopScreen()
            if (!keepInBackground) {
                // save battery: drop the connection a little after leaving the app
                softDisconnectJob?.cancel()
                softDisconnectJob = scope.launch {
                    delay(Prefs.disconnectDelay.coerceAtLeast(0) * 1000L)
                    // never cut a file transfer or a pending share in half
                    while (!foreground && (Transfers.active || synchronized(pending) { pending.isNotEmpty() })) delay(1000)
                    if (!foreground) {
                        try { send(JSONObject().put("type", "bye").put("reason", "background")) } catch (_: Exception) { }
                        delay(150)
                        disconnectInternal()
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------ connect
    fun connect(manual: Boolean = false) {
        val pc = Prefs.pc ?: run { setState(ConnState.Unpaired); return }
        if (manual) { userDisconnected = false; backoff = 1000L }
        if (userDisconnected) return
        if (state == ConnState.Connecting || state == ConnState.Connected) return
        reconnectJob?.cancel()
        setState(ConnState.Connecting)
        val myGen = ++gen
        scope.launch {
            var ok = tryOpen(pc.host, pc.port, pc.token, myGen)
            if (!ok && myGen == gen && lastBye == null) {
                val found = discover(pc.pcId, pc.bcast)
                if (found != null && myGen == gen) {
                    if (found != pc.host) ui { Prefs.updateHost(found) }
                    ok = tryOpen(found, pc.port, pc.token, myGen)
                }
            }
            if (!ok && myGen == gen) onDrop(wasConnected = false)
        }
    }

    private suspend fun tryOpen(host: String, port: Int, token: String, myGen: Int): Boolean {
        val result = CompletableDeferred<Boolean>()
        lastBye = null
        crypto = CryptoBox(token)
        seq.set(0)
        val req = Request.Builder().url("ws://$host:$port/ws").build()
        val socket = client.newWebSocket(req, Listener(myGen, result))
        ws = socket
        val ok = withTimeoutOrNull(5000) { result.await() } ?: false
        if (!ok) { socket.cancel() }
        return ok
    }

    private class Listener(val myGen: Int, val result: CompletableDeferred<Boolean>) : WebSocketListener() {
        var authed = false

        override fun onMessage(webSocket: WebSocket, text: String) {
            if (myGen != gen) { webSocket.cancel(); return }
            if (!authed && text.startsWith("{")) {
                val hello = try { JSONObject(text) } catch (_: Exception) { return }
                if (hello.optString("type") != "hello") return
                val auth = JSONObject()
                    .put("type", "auth")
                    .put("sid", hello.optString("sid"))
                    .put("device", deviceName())
                    .put("foreground", foreground)
                sendOn(webSocket, auth)
                return
            }
            val o = try { JSONObject(crypto!!.decrypt(text)) } catch (_: Exception) { return }
            if (!authed) {
                if (o.optString("type") == "welcome") {
                    authed = true
                    onWelcome(o)
                    result.complete(true)
                }
                return
            }
            try { handle(o) } catch (_: Exception) { }
        }

        override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
            if (reason.isNotEmpty()) lastBye = reason
            webSocket.close(1000, null)
        }

        override fun onClosed(webSocket: WebSocket, code: Int, reason: String) = dropped()
        override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) = dropped()

        private fun dropped() {
            if (!result.isCompleted) result.complete(false)
            if (myGen != gen) return
            if (authed) onDrop(wasConnected = true)
        }
    }

    private fun sendOn(socket: WebSocket, o: JSONObject): Boolean {
        val c = crypto ?: return false
        o.put("seq", seq.incrementAndGet())
        return socket.send(c.encrypt(o.toString()))
    }

    fun send(o: JSONObject): Boolean {
        val s = ws ?: return false
        if (state != ConnState.Connected) return false
        return try { sendOn(s, o) } catch (_: Exception) { false }
    }

    /** Bytes queued in OkHttp, used for flow control of big transfers. */
    fun queueSize(): Long = ws?.queueSize() ?: 0L

    private fun onWelcome(o: JSONObject) {
        backoff = 1000L
        ui {
            Prefs.updatePcInfo(null, o.optString("mac"), o.optString("bcast"))
            val name = o.optString("pcName")
            if (name.isNotBlank()) Prefs.updatePcInfo(name, null, null)
            pcVersion = o.optString("version")
            state = ConnState.Connected
            detail = ""
            if (Prefs.bgAlerts || !Prefs.powerSave) PcPanelApp.startAlertService(ctx)
            // language picked on the phone while offline wins over the PC's
            if (Prefs.langDirty) setShared("lang", Prefs.lang)
            sendRate()
            PhoneLink.onConnected(ctx)
            val todo = synchronized(pending) { pending.toList().also { pending.clear() } }
            todo.forEach { try { it() } catch (_: Exception) { } }
        }
    }

    private fun onDrop(wasConnected: Boolean) {
        val bye = lastBye
        ws = null
        ui { frame = null }
        PhoneLink.onDisconnected(ctx)
        Transfers.failAll()
        when (bye) {
            "auth", "unpaired" -> { ui { hasStats = false }; setState(ConnState.AuthFailed, Str.t("conn.authFailed")); return }
            "pc_disconnect" -> { userDisconnected = true; ui { hasStats = false }; setState(ConnState.Idle, Str.t("conn.byPc")); return }
        }
        if (userDisconnected) { setState(ConnState.Idle); return }
        setState(ConnState.Offline, if (wasConnected) Str.t("conn.lost") else Str.t("conn.unreachable"))
        if (foreground || keepInBackground) scheduleReconnect()
    }

    private fun scheduleReconnect() {
        reconnectJob?.cancel()
        val wait = backoff
        backoff = (backoff * 2).coerceAtMost(15000L)
        reconnectJob = scope.launch {
            delay(wait)
            if (!userDisconnected && (foreground || keepInBackground) && state != ConnState.Connected && state != ConnState.Connecting)
                connect()
        }
    }

    /** User pressed "Disconnect". */
    fun disconnect() {
        userDisconnected = true
        reconnectJob?.cancel()
        try { send(JSONObject().put("type", "bye").put("reason", "user")) } catch (_: Exception) { }
        disconnectInternal()
        ui { hasStats = false }
        setState(if (Prefs.pc == null) ConnState.Unpaired else ConnState.Idle, Str.t("conn.youDisconnected"))
        PcPanelApp.stopAlertService(ctx)
    }

    private fun disconnectInternal() {
        gen++
        val s = ws
        ws = null
        try { s?.close(1000, "bye") } catch (_: Exception) { }
        if (state == ConnState.Connected || state == ConnState.Connecting) setState(ConnState.Idle)
        PhoneLink.onDisconnected(ctx)
    }

    // ------------------------------------------------------------------ discovery + wake on LAN
    private fun discover(pcId: String, bcast: String): String? {
        return try {
            DatagramSocket().use { s ->
                s.broadcast = true
                s.soTimeout = 400
                val msg = "MOBILINK?$pcId".toByteArray()
                for (addr in listOf("255.255.255.255", bcast).distinct()) {
                    try { s.send(DatagramPacket(msg, msg.size, InetAddress.getByName(addr), DISCOVERY_PORT)) } catch (_: Exception) { }
                }
                val buf = ByteArray(256)
                val deadline = System.currentTimeMillis() + 1800
                while (System.currentTimeMillis() < deadline) {
                    try {
                        val p = DatagramPacket(buf, buf.size)
                        s.receive(p)
                        val txt = String(p.data, 0, p.length)
                        if (txt.startsWith("MOBILINK!$pcId")) return p.address.hostAddress
                    } catch (_: SocketTimeoutException) { }
                }
                null
            }
        } catch (_: Exception) { null }
    }

    fun wake() {
        val pc = Prefs.pc ?: return
        if (pc.mac.isBlank()) { showBanner(Str.t("wol.noMac"), BannerKind.Warning); return }
        userDisconnected = false
        setState(ConnState.Waking, Str.t("wol.sent"))
        scope.launch {
            try {
                val mac = pc.mac.split(":", "-").map { it.toInt(16).toByte() }.toByteArray()
                val pkt = ByteArray(6) { 0xFF.toByte() } + ByteArray(16 * 6) { mac[it % 6] }
                DatagramSocket().use { s ->
                    s.broadcast = true
                    repeat(3) {
                        for (addr in listOf("255.255.255.255", pc.bcast).distinct()) for (port in listOf(9, 7)) {
                            try { s.send(DatagramPacket(pkt, pkt.size, InetAddress.getByName(addr), port)) } catch (_: Exception) { }
                        }
                        delay(300)
                    }
                }
            } catch (_: Exception) { }
            val end = System.currentTimeMillis() + 90_000
            while (System.currentTimeMillis() < end && state != ConnState.Connected && !userDisconnected) {
                delay(4000)
                if (state == ConnState.Waking || state == ConnState.Offline || state == ConnState.Idle) {
                    val myGen = ++gen
                    ui { state = ConnState.Connecting }
                    var ok = tryOpen(pc.host, pc.port, pc.token, myGen)
                    if (!ok) {
                        val found = discover(pc.pcId, pc.bcast)
                        if (found != null) { ui { Prefs.updateHost(found) }; ok = tryOpen(found, pc.port, pc.token, myGen) }
                    }
                    if (!ok && state != ConnState.Connected) ui { state = ConnState.Waking; detail = Str.t("wol.waiting") }
                }
            }
            if (state != ConnState.Connected) setState(ConnState.Offline, Str.t("wol.failed"))
        }
    }

    // ------------------------------------------------------------------ incoming
    private fun handle(o: JSONObject) {
        when (o.optString("type")) {
            "stats" -> {
                val s = Stats.parse(o)
                ui {
                    stats = s
                    hasStats = true
                    inGame = s.game
                    cpuHist = cpuHist.drop(1) + s.cpuLoad
                    gpuHist = gpuHist.drop(1) + s.gpuLoad
                }
            }
            "media" -> { val m = MediaState.parse(o); ui { media = m; if (m.artId == 0) art = null } }
            "art" -> {
                val data = o.optString("data")
                val bmp = if (data.isNotEmpty()) decode(data) else null
                ui { art = bmp?.asImageBitmap() }
            }
            "vol" -> ui { volume = o.optDouble("level", 0.5).toFloat(); muted = o.optBoolean("muted"); outputName = o.optString("output") }
            "timer" -> ui { timer = TimerState(o.optBoolean("active"), o.optString("action"), o.optInt("remaining"), System.currentTimeMillis()) }
            "procs" -> {
                val a = o.optJSONArray("list") ?: JSONArray()
                val list = (0 until a.length()).map { a.getJSONObject(it).let { p -> ProcInfo(p.optString("name"), p.optLong("mem"), p.optInt("count"), p.optBoolean("locked")) } }
                ui { procs = list }
            }
            "apps" -> {
                val a = o.optJSONArray("list") ?: JSONArray()
                val list = (0 until a.length()).map { a.getJSONObject(it).let { p -> PcApp(p.optString("id"), p.optString("name")) } }
                ui { apps = list; appsLoading = false }
            }
            "icon" -> {
                val id = o.optString("id")
                val data = o.optString("data")
                if (data.isNotEmpty()) {
                    val bytes = Base64.getDecoder().decode(data)
                    try { iconFile(id).writeBytes(bytes) } catch (_: Exception) { }
                    val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
                    if (bmp != null) ui { icons[id] = bmp.asImageBitmap() }
                }
            }
            "shot" -> {
                val bytes = Base64.getDecoder().decode(o.optString("data"))
                val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
                val saved = saveToGallery(bytes)
                ui {
                    if (bmp != null) screenshot = bmp.asImageBitmap()
                    showBanner(if (saved) Str.t("shot.saved") else Str.t("shot.received"), BannerKind.Success)
                }
            }
            "display" -> ui { brightness = o.optInt("brightness", -1); night = o.optInt("night") }
            "alert" -> {
                val kind = o.optString("kind").uppercase()
                val temp = o.optInt("temp")
                val text = "$kind $temp°C — ${Str.t("alert.hot")}"
                showBanner(text, BannerKind.Warning)
                notifyAlert(text)
            }
            "result" -> {
                if (!o.optBoolean("ok")) {
                    val msg = when (o.optString("what")) {
                        "kill" -> Str.t("res.killFail")
                        "launch" -> Str.t("res.launchFail")
                        "brightness" -> Str.t("res.brightFail")
                        "nightlight" -> Str.t("res.nightFail")
                        "output" -> Str.t("res.outputFail")
                        else -> Str.t("res.fail")
                    }
                    showBanner(msg, BannerKind.Error)
                }
            }
            "bye" -> lastBye = o.optString("reason")

            // ---------------- v2
            "settings" -> {
                val s = PcSettings.parse(o)
                ui {
                    pcSettings = s
                    if (s.pcName.isNotBlank()) Prefs.updatePcInfo(s.pcName, null, null)
                    if (!Prefs.langDirty && s.lang != Prefs.lang && Str.isSupported(s.lang)) {
                        Prefs.updateLang(s.lang, fromUser = false, connected = true)
                        Str.load(s.lang)
                    }
                    if (s.cpuAlert != Prefs.cpuAlert || s.gpuAlert != Prefs.gpuAlert) Prefs.updateAlerts(s.cpuAlert, s.gpuAlert)
                }
            }
            "pcInfo" -> ui { val n = o.optString("name"); if (n.isNotBlank()) Prefs.updatePcInfo(n, null, null) }
            "game" -> ui { inGame = o.optBoolean("on") }
            "mixer" -> {
                val a = o.optJSONArray("list") ?: JSONArray()
                val list = (0 until a.length()).map { a.getJSONObject(it).let { p ->
                    MixerApp(p.optString("id"), p.optString("name"), p.optDouble("vol", 1.0).toFloat(), p.optBoolean("mute"), p.optBoolean("active"), p.optString("icon"))
                } }
                ui { mixer = list }
            }
            "outputs" -> {
                val a = o.optJSONArray("list") ?: JSONArray()
                val list = (0 until a.length()).map { a.getJSONObject(it).let { p -> AudioOut(p.optString("id"), p.optString("name"), p.optBoolean("def")) } }
                ui { outputs = list }
            }
            "frame" -> {
                val bmp = decode(o.optString("data"))
                val w = o.optInt("w", 16); val h = o.optInt("h", 9)
                if (bmp != null) ui { frame = bmp.asImageBitmap(); frameSize = Pair(w, h) }
            }
            "countdown" -> ui { countdown = CountdownState(o.optBoolean("active"), o.optInt("remaining"), o.optInt("total"), o.optString("label"), System.currentTimeMillis()) }
            "countdownDone" -> {
                val label = o.optString("label")
                showBanner(if (label.isBlank()) Str.t("cd.done") else "$label · ${Str.t("cd.done")}", BannerKind.Success)
            }
            "history" -> {
                fun arr(k: String): List<Float> { val a = o.optJSONArray(k) ?: return emptyList(); return (0 until a.length()).map { a.optDouble(it, -1.0).toFloat() } }
                val h = TempHistory(o.optInt("step", 10), arr("cpu"), arr("gpu"))
                ui { history = h }
            }
            "musicHome" -> {
                val secs = o.optJSONArray("sections") ?: JSONArray()
                val list = (0 until secs.length()).mapNotNull { i -> secs.optJSONObject(i)?.let { s -> MusicSection(s.optString("title"), parseMusicItems(s.optJSONArray("items"))) } }
                ui { musicHome = list; musicLoading = false; musicError = o.optString("error") }
            }
            "musicResults" -> {
                val items = parseMusicItems(o.optJSONArray("items"))
                ui { musicResults = items; musicLoading = false; musicError = o.optString("error") }
            }
            "musicList" -> {
                val l = MusicList(o.optString("kind"), o.optString("id"), o.optString("title"), parseMusicItems(o.optJSONArray("items")))
                ui { musicList = l; musicLoading = false }
            }
            "musicPlayed" -> {
                if (!o.optBoolean("ok")) {
                    val msg = when (o.optString("error")) {
                        "premium" -> Str.t("music.errPremium")
                        "nodevice" -> Str.t("music.errDevice")
                        "login" -> Str.t("music.errLogin")
                        else -> Str.t("music.errFail")
                    }
                    showBanner(msg, BannerKind.Error)
                }
            }
            "spotifyState" -> {
                when (o.optString("error")) {
                    "noclient" -> showBanner(Str.t("music.needSetup"), BannerKind.Warning)
                    "fail" -> showBanner(Str.t("music.loginFail"), BannerKind.Error)
                    "" -> if (o.optBoolean("connected")) { showBanner(Str.t("music.loginOk"), BannerKind.Success); musicHomeReq("spotify") }
                }
            }
            "clip" -> PhoneLink.receiveClipboard(ctx, o.optString("text"))
            "ring" -> Ringer.start(ctx)
            "fileAck" -> Transfers.onAck(o)
            "fileStart" -> Transfers.onIncomingStart(ctx, o)
            "fileChunk" -> Transfers.onIncomingChunk(o)
            "fileEnd" -> Transfers.onIncomingEnd(ctx, o)
        }
    }

    private fun decode(b64: String): Bitmap? = try {
        val b = Base64.getDecoder().decode(b64); BitmapFactory.decodeByteArray(b, 0, b.size)
    } catch (_: Exception) { null }

    // ------------------------------------------------------------------ icons
    private fun iconFile(id: String): File {
        val dir = File(ctx.filesDir, "icons").apply { mkdirs() }
        val h = MessageDigest.getInstance("MD5").digest(id.toByteArray()).joinToString("") { "%02x".format(it) }
        return File(dir, "$h.png")
    }

    fun requestIcon(id: String) {
        if (icons.containsKey(id) || !iconRequested.add(id)) return
        scope.launch {
            val f = iconFile(id)
            if (f.exists()) {
                val bmp = BitmapFactory.decodeFile(f.absolutePath)
                if (bmp != null) { ui { icons[id] = bmp.asImageBitmap() }; return@launch }
            }
            if (state == ConnState.Connected) send(JSONObject().put("type", "icon").put("id", id))
            else iconRequested.remove(id)
        }
    }

    // ------------------------------------------------------------------ helpers
    private fun saveToGallery(jpeg: ByteArray): Boolean {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) return false
        return try {
            val name = "PCPanel_" + java.text.SimpleDateFormat("yyyyMMdd_HHmmss", java.util.Locale.US).format(java.util.Date()) + ".jpg"
            val values = ContentValues().apply {
                put(MediaStore.Images.Media.DISPLAY_NAME, name)
                put(MediaStore.Images.Media.MIME_TYPE, "image/jpeg")
                put(MediaStore.Images.Media.RELATIVE_PATH, "Pictures/PC Panel")
            }
            val uri = ctx.contentResolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, values) ?: return false
            ctx.contentResolver.openOutputStream(uri)?.use { it.write(jpeg) }
            true
        } catch (_: Exception) { false }
    }

    private fun notifyAlert(text: String) {
        try {
            val nm = ctx.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            val n = NotificationCompat.Builder(ctx, PcPanelApp.CH_ALERTS)
                .setSmallIcon(R.drawable.ic_stat_pcpanel)
                .setContentTitle("PC Panel")
                .setContentText(text)
                .setPriority(NotificationCompat.PRIORITY_HIGH)
                .setAutoCancel(true)
                .setContentIntent(PcPanelApp.openAppIntent(ctx))
                .build()
            nm.notify(1002, n)
            val vib = ctx.getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator
            vib?.vibrate(VibrationEffect.createWaveform(longArrayOf(0, 120, 80, 120), -1))
        } catch (_: Exception) { }
    }

    // ------------------------------------------------------------------ commands (UI helpers)
    fun cmd(type: String, vararg kv: Pair<String, Any?>): Boolean {
        val o = JSONObject().put("type", type)
        kv.forEach { (k, v) -> o.put(k, v) }
        return send(o)
    }

    /** Change a setting that lives on the PC (and is mirrored here). */
    fun setShared(key: String, value: Any) {
        // optimistic local update so the switch moves immediately
        val cur = pcSettings
        pcSettings = when (key) {
            "lang" -> cur.copy(lang = value as String)
            "pcName" -> cur.copy(pcName = value as String)
            "notchOn" -> cur.copy(notchOn = value as Boolean)
            "notchStyle" -> cur.copy(notchStyle = value as String)
            "notchMode" -> cur.copy(notchMode = value as String)
            "notchFs" -> cur.copy(notchFs = value as Boolean)
            "notchVol" -> cur.copy(notchVol = value as Boolean)
            "notchConn" -> cur.copy(notchConn = value as Boolean)
            "notchNotif" -> cur.copy(notchNotif = value as Boolean)
            "notchCall" -> cur.copy(notchCall = value as Boolean)
            "notchCharge" -> cur.copy(notchCharge = value as Boolean)
            "sounds" -> cur.copy(sounds = value as Boolean)
            "soundVol" -> cur.copy(soundVol = value as Int)
            "clipSync" -> cur.copy(clipSync = value as Boolean)
            "autoLock" -> cur.copy(autoLock = value as Boolean)
            "shotCursor" -> cur.copy(shotCursor = value as Boolean)
            "gameMode" -> cur.copy(gameMode = value as Boolean)
            "tempHistory" -> cur.copy(tempHistory = value as Boolean)
            "fanCtl" -> cur.copy(fanCtl = value as Boolean)
            "musicSrc" -> cur.copy(musicSrc = value as String)
            "cpuAlert" -> cur.copy(cpuAlert = value as Int)
            "gpuAlert" -> cur.copy(gpuAlert = value as Int)
            "autostart" -> cur.copy(autostart = value as Boolean)
            else -> cur
        }
        if (send(JSONObject().put("type", "set").put("values", JSONObject().put(key, value))) && key == "lang") Prefs.clearLangDirty()
    }

    fun sendRate() = cmd("rate", "ms" to Prefs.refreshMs)

    fun mediaAction(a: String) = cmd("media", "action" to a)
    fun seek(sec: Double) { cmd("media", "action" to "seek", "pos" to sec); ui { media = media.copy(pos = sec, receivedAt = System.currentTimeMillis()) } }
    @JvmName("updateVolume") fun setVolume(v: Float) { volume = v; cmd("volume", "level" to v.toDouble()) }
    fun setMute(m: Boolean) { muted = m; cmd("mute", "value" to m) }
    fun power(action: String) = cmd("power", "action" to action)
    fun startTimer(action: String, minutes: Int) = cmd("timer", "action" to action, "minutes" to minutes)
    fun cancelTimer() = cmd("timerCancel")
    fun screenshotReq() = cmd("screenshot")
    fun refreshProcs() = cmd("procs")
    fun kill(name: String) = cmd("kill", "name" to name)
    fun loadApps() { appsLoading = true; cmd("apps") }
    fun launch(id: String) = cmd("launch", "id" to id)
    fun displayGet() = cmd("displayGet")
    @JvmName("updateBrightness") fun setBrightness(v: Int) { brightness = v; cmd("brightness", "value" to v) }
    @JvmName("updateNight") fun setNight(v: Int) { night = v; cmd("nightlight", "value" to v) }
    fun text(t: String) = cmd("text", "text" to t)
    fun keys(vararg k: String) { val a = JSONArray(); k.forEach { a.put(it) }; cmd("keys", "keys" to a) }
    fun keysList(k: List<String>) { val a = JSONArray(); k.forEach { a.put(it) }; cmd("keys", "keys" to a) }
    fun click(button: String = "left", double: Boolean = false) = cmd("click", "button" to button, "double" to double)
    fun button(button: String, down: Boolean) = cmd("button", "button" to button, "down" to down)
    fun openUrl(url: String) = cmd("open", "target" to url)

    fun mixerReq() = cmd("mixer")
    fun outputsReq() = cmd("outputs")
    fun mixerSet(id: String, vol: Float? = null, mute: Boolean? = null) {
        val o = JSONObject().put("type", "mixerSet").put("id", id)
        if (vol != null) o.put("vol", vol.toDouble())
        if (mute != null) o.put("mute", mute)
        send(o)
        mixer = mixer.map { if (it.id == id) it.copy(vol = vol ?: it.vol, mute = mute ?: it.mute) else it }
    }
    fun outputSet(id: String) { outputs = outputs.map { it.copy(isDefault = it.id == id) }; cmd("outputSet", "id" to id) }

    fun startScreen(fps: Int, quality: Int) = cmd("screenStart", "fps" to fps, "quality" to quality)
    fun stopScreen() { cmd("screenStop"); ui { frame = null } }
    fun clickAt(x: Float, y: Float, button: String = "left", double: Boolean = false) =
        cmd("clickAt", "x" to x.toDouble(), "y" to y.toDouble(), "button" to button, "double" to double)

    fun countdownStart(seconds: Int, label: String) = cmd("countdown", "seconds" to seconds, "label" to label)
    fun countdownCancel() = cmd("countdownCancel")
    fun historyReq() = cmd("history")
    fun fanSet(group: String, mode: String, pct: Int) = cmd("fanSet", "group" to group, "mode" to mode, "pct" to pct)

    fun musicHomeReq(src: String) { musicLoading = true; musicError = ""; cmd("musicHome", "src" to src) }
    fun musicSearch(src: String, q: String) { musicLoading = true; musicError = ""; cmd("musicSearch", "src" to src, "q" to q) }
    fun musicOpenList(kind: String, id: String) { musicLoading = true; musicList = MusicList(kind, id, "", emptyList()); cmd("musicList", "kind" to kind, "id" to id) }
    fun musicPlay(src: String, item: MusicItem) = cmd("musicPlay", "src" to src, "kind" to item.kind, "uri" to item.uri, "ctx" to item.ctx)
    fun musicSearchInBrowser(q: String) = cmd("musicPlay", "src" to "youtube", "kind" to "search", "uri" to q)
    fun spotifyLogin() = cmd("spotifyLogin")
    fun spotifyLogout() = cmd("spotifyLogout")

    // mouse moves / scrolls are batched (~70 Hz) so the network isn't flooded
    private var pdx = 0f; private var pdy = 0f; private var psx = 0f; private var psy = 0f
    private var flushScheduled = false
    private val flush = Runnable {
        flushScheduled = false
        val dx = pdx.toInt(); val dy = pdy.toInt()
        if (dx != 0 || dy != 0) { cmd("mouse", "dx" to dx, "dy" to dy); pdx -= dx; pdy -= dy }
        val sy = psy.toInt(); val sx = psx.toInt()
        if (sy != 0 || sx != 0) { cmd("scroll", "dy" to sy, "dx" to sx); psy -= sy; psx -= sx }
    }
    fun mouseMove(dx: Float, dy: Float) { pdx += dx; pdy += dy; scheduleFlush() }
    fun scroll(dy: Float, dx: Float) { psy += dy; psx += dx; scheduleFlush() }
    private fun scheduleFlush() { if (!flushScheduled) { flushScheduled = true; main.postDelayed(flush, 14) } }
}
