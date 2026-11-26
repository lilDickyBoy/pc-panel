package com.pcpanel.app.net

import android.media.MediaCodec
import android.media.MediaFormat
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.os.Process
import android.view.MotionEvent
import android.view.Surface
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.util.Base64
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/** What the PC sent in "ssOpen": where to connect and the one-time secrets for this session. */
data class ScreenOpen(
    val host: String, val port: Int, val ticket: ByteArray, val key: ByteArray,
    val w: Int, val h: Int, val usb: Boolean, val id: Long = System.nanoTime(),
)

/**
 * Phone as a second monitor: receives the H.264 stream of the PC's virtual monitor and plays it with the
 * hardware decoder straight onto a Surface (no copies, low-latency mode), and sends touches back.
 *
 * Wire format (both ways): [u32 length][AES-GCM ciphertext | tag16], nonce = direction(4) | counter(8).
 * Plain = [u8 type][payload]. PC→phone: 1 config, 2 frame, 3 ping. Phone→PC: 10 touch, 11 pong, 13 bye.
 */
object ScreenLink {
    enum class Phase { Idle, Connecting, Live, Lost }

    var phase by mutableStateOf(Phase.Idle)
        private set
    var fps by mutableIntStateOf(0)
        private set
    var usb by mutableStateOf(false)
        private set
    var videoW by mutableIntStateOf(0)
        private set
    var videoH by mutableIntStateOf(0)
        private set

    @Volatile private var gen = 0
    @Volatile private var socket: Socket? = null
    @Volatile private var surface: Surface? = null
    @Volatile private var open: ScreenOpen? = null
    private var reader: Thread? = null
    private var writer: Thread? = null
    private val outQ = LinkedBlockingQueue<ByteArray>(256)

    // decoder
    private var codec: MediaCodec? = null
    private var codecThread: HandlerThread? = null
    private var lastSps: ByteArray? = null
    private var lastPps: ByteArray? = null
    private val freeInputs = ArrayBlockingQueue<Int>(64)
    @Volatile private var rendered = 0
    private var fpsTick = 0L

    private fun ui(b: () -> Unit) = Connection.ui(b)

    /** Start (or restart) streaming onto [s] with the parameters from the PC. */
    @Synchronized
    fun start(o: ScreenOpen, s: Surface) {
        stopInternal(sendBye = false)
        val my = ++gen
        open = o
        surface = s
        ui { phase = Phase.Connecting; usb = o.usb; fps = 0 }
        reader = Thread({ runLoop(my) }, "ss-read").apply { priority = Thread.MAX_PRIORITY; start() }
    }

    /** The Surface changed size or was recreated (e.g. after returning to the app). */
    @Synchronized
    fun surfaceChanged(s: Surface?) {
        if (s === surface) return
        surface = s
        val o = open ?: return
        if (s != null) start(o, s) else stopInternal(sendBye = false)
    }

    @Synchronized
    fun stop() {
        stopInternal(sendBye = true)
        open = null
        ui { phase = Phase.Idle; fps = 0 }
    }

    private fun stopInternal(sendBye: Boolean) {
        gen++
        if (sendBye) {
            try { outQ.clear(); outQ.offer(byteArrayOf(13)); Thread.sleep(60) } catch (_: Exception) { }
        }
        try { socket?.close() } catch (_: Exception) { }
        socket = null
        try { writer?.interrupt() } catch (_: Exception) { }
        writer = null
        reader = null
        releaseCodec()
        outQ.clear()
    }

    // ------------------------------------------------------------------ network
    private fun runLoop(my: Int) {
        Process.setThreadPriority(Process.THREAD_PRIORITY_URGENT_DISPLAY)
        var attempts = 0
        while (my == gen) {
            val o = open ?: return
            try {
                val host = o.host.ifBlank { com.pcpanel.app.data.Prefs.pc?.host ?: return }
                val s = Socket()
                s.tcpNoDelay = true
                s.receiveBufferSize = 1 shl 20
                s.connect(InetSocketAddress(host, o.port), 4000)
                socket = s
                val out = DataOutputStream(BufferedOutputStream(s.getOutputStream(), 8192))
                out.write(byteArrayOf(1) + o.ticket)
                out.flush()
                startWriter(my, out, o.key)
                attempts = 0
                read(my, DataInputStream(BufferedInputStream(s.getInputStream(), 1 shl 16)), o.key)
            } catch (_: Exception) {
            }
            try { socket?.close() } catch (_: Exception) { }
            if (my != gen) return
            // brief drop (Wi-Fi hiccup, cable wiggle): try again for a while with the same ticket
            ui { phase = Phase.Lost; fps = 0 }
            releaseCodec()
            if (++attempts > 12) return
            try { Thread.sleep(800) } catch (_: InterruptedException) { return }
        }
    }

    private fun read(my: Int, inp: DataInputStream, key: ByteArray) {
        val k = SecretKeySpec(key, "AES")
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        val nonce = ByteArray(12).also { it[3] = 1 }
        var counter = 0L
        var buf = ByteArray(1 shl 16)
        while (my == gen) {
            val len = inp.readInt()
            if (len < 17 || len > (8 shl 20)) throw IllegalStateException("bad frame")
            if (buf.size < len) buf = ByteArray(maxOf(len, buf.size * 2))
            inp.readFully(buf, 0, len)
            putLong(nonce, 4, counter++)
            cipher.init(Cipher.DECRYPT_MODE, k, GCMParameterSpec(128, nonce))
            val plain = cipher.doFinal(buf, 0, len)
            when (plain[0].toInt()) {
                1 -> onConfig(JSONObject(String(plain, 1, plain.size - 1, Charsets.UTF_8)))
                2 -> onFrame(plain)
                3 -> { val p = plain.copyOf(); p[0] = 11; outQ.offer(p) }
            }
        }
    }

    private fun startWriter(my: Int, out: DataOutputStream, key: ByteArray) {
        outQ.clear()
        writer = Thread({
            val k = SecretKeySpec(key, "AES")
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            val nonce = ByteArray(12).also { it[3] = 2 }
            var counter = 0L
            try {
                while (my == gen) {
                    val p = outQ.poll(500, TimeUnit.MILLISECONDS) ?: continue
                    putLong(nonce, 4, counter++)
                    cipher.init(Cipher.ENCRYPT_MODE, k, GCMParameterSpec(128, nonce))
                    val c = cipher.doFinal(p)
                    out.writeInt(c.size)
                    out.write(c)
                    if (outQ.isEmpty()) out.flush()
                    if (p[0].toInt() == 13) { out.flush(); return@Thread }
                }
            } catch (_: Exception) { }
        }, "ss-write").apply { priority = Thread.MAX_PRIORITY - 1; start() }
    }

    private fun putLong(b: ByteArray, off: Int, v: Long) {
        for (i in 0 until 8) b[off + i] = (v ushr (56 - 8 * i)).toByte()
    }

    // ------------------------------------------------------------------ decoder
    private fun onConfig(o: JSONObject) {
        val sps = Base64.getDecoder().decode(o.optString("sps"))
        val pps = Base64.getDecoder().decode(o.optString("pps"))
        val w = o.optInt("w", 1920)
        val h = o.optInt("h", 1080)
        if (codec != null && sps.contentEquals(lastSps) && pps.contentEquals(lastPps)) return
        releaseCodec()
        val surf = surface ?: return
        lastSps = sps; lastPps = pps
        fun format(full: Boolean) = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, w, h).apply {
            setByteBuffer("csd-0", ByteBuffer.wrap(sps))
            setByteBuffer("csd-1", ByteBuffer.wrap(pps))
            setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, maxOf(w * h, 1 shl 20))
            if (Build.VERSION.SDK_INT >= 30) setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
            if (full) {
                setInteger(MediaFormat.KEY_PRIORITY, 0)                 // real-time
                setInteger(MediaFormat.KEY_OPERATING_RATE, 120)
                // Qualcomm (Snapdragon) decoders: output every frame immediately, no reordering
                setInteger("vendor.qti-ext-dec-low-latency.enable", 1)
                setInteger("vendor.qti-ext-dec-picture-order.enable", 1)
            }
        }
        val ht = HandlerThread("ss-codec", Process.THREAD_PRIORITY_URGENT_DISPLAY).apply { start() }
        codecThread = ht
        val c = MediaCodec.createDecoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
        freeInputs.clear()
        val cb = object : MediaCodec.Callback() {
            override fun onInputBufferAvailable(codec: MediaCodec, index: Int) { freeInputs.offer(index) }
            override fun onOutputBufferAvailable(codec: MediaCodec, index: Int, info: MediaCodec.BufferInfo) {
                try { codec.releaseOutputBuffer(index, true) } catch (_: Exception) { }
                rendered++
                val now = System.currentTimeMillis()
                if (now - fpsTick >= 1000) {
                    val f = rendered
                    rendered = 0
                    fpsTick = now
                    ui { fps = f; if (phase != Phase.Live) phase = Phase.Live }
                }
            }
            override fun onError(codec: MediaCodec, e: MediaCodec.CodecException) { }
            override fun onOutputFormatChanged(codec: MediaCodec, format: MediaFormat) { }
        }
        c.setCallback(cb, Handler(ht.looper))
        try {
            c.configure(format(true), surf, null, 0)
        } catch (e: Exception) {
            // some decoders reject the extra keys: try again with the plain format
            c.reset()
            c.setCallback(cb, Handler(ht.looper))
            c.configure(format(false), surf, null, 0)
        }
        c.start()
        codec = c
        fpsTick = System.currentTimeMillis()
        ui { videoW = w; videoH = h; if (phase != Phase.Live) phase = Phase.Live }
    }

    private fun onFrame(p: ByteArray) {
        val c = codec ?: return
        val key = p[1].toInt() == 1
        var pts = 0L
        for (i in 0 until 8) pts = (pts shl 8) or (p[2 + i].toLong() and 0xFF)
        val idx = freeInputs.poll(40, TimeUnit.MILLISECONDS) ?: return   // decoder busy: skip, never pile up delay
        try {
            val b = c.getInputBuffer(idx) ?: return
            b.clear()
            val n = p.size - 10
            if (n > b.remaining()) return
            b.put(p, 10, n)
            c.queueInputBuffer(idx, 0, n, pts, if (key) MediaCodec.BUFFER_FLAG_KEY_FRAME else 0)
        } catch (_: Exception) { }
    }

    private fun releaseCodec() {
        val c = codec
        codec = null
        lastSps = null; lastPps = null
        try { c?.stop() } catch (_: Exception) { }
        try { c?.release() } catch (_: Exception) { }
        try { codecThread?.quitSafely() } catch (_: Exception) { }
        codecThread = null
        freeInputs.clear()
    }

    // ------------------------------------------------------------------ touch
    /** Sends every finger currently on the glass; x/y are relative to [w]×[h] (the video view). */
    fun touch(e: MotionEvent, w: Int, h: Int) {
        if (phase != Phase.Live || w <= 0 || h <= 0) return
        val n = minOf(e.pointerCount, 10)
        val msg = ByteArray(2 + n * 6)
        msg[0] = 10
        msg[1] = n.toByte()
        val act = e.actionMasked
        val ai = e.actionIndex
        for (i in 0 until n) {
            val phaseB = when (act) {
                MotionEvent.ACTION_DOWN -> 0
                MotionEvent.ACTION_POINTER_DOWN -> if (i == ai) 0 else 1
                MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> 2
                MotionEvent.ACTION_POINTER_UP -> if (i == ai) 2 else 1
                else -> 1
            }
            val x = (e.getX(i) / w).coerceIn(0f, 1f)
            val y = (e.getY(i) / h).coerceIn(0f, 1f)
            val xi = (x * 65535).toInt()
            val yi = (y * 65535).toInt()
            val o = 2 + i * 6
            msg[o] = (e.getPointerId(i) and 0xFF).toByte()
            msg[o + 1] = phaseB.toByte()
            msg[o + 2] = (xi shr 8).toByte(); msg[o + 3] = xi.toByte()
            msg[o + 4] = (yi shr 8).toByte(); msg[o + 5] = yi.toByte()
        }
        // moves are replaceable; downs/ups must never be lost
        if (act == MotionEvent.ACTION_MOVE && outQ.size > 8) return
        outQ.offer(msg)
    }
}
