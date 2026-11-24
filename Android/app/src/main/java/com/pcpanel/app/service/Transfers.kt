package com.pcpanel.app.service

import android.app.NotificationManager
import android.app.PendingIntent
import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import android.provider.OpenableColumns
import android.webkit.MimeTypeMap
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.app.NotificationCompat
import androidx.core.content.FileProvider
import com.pcpanel.app.PcPanelApp
import com.pcpanel.app.R
import com.pcpanel.app.net.BannerKind
import com.pcpanel.app.net.Connection
import com.pcpanel.app.net.Transfer
import com.pcpanel.app.ui.Str
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import org.json.JSONObject
import java.io.File
import java.io.OutputStream
import java.util.Base64
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap

/** File transfer with the PC (256 KB chunks, every chunk acknowledged by the PC). */
object Transfers {
    private const val CHUNK = 256 * 1024

    var list by mutableStateOf<List<Transfer>>(emptyList())
        private set

    val active: Boolean get() = list.any { !it.finished }

    private val acks = ConcurrentHashMap<String, CompletableDeferred<Boolean>>()

    private class Incoming(val uri: Uri?, val file: File?, val out: OutputStream, val name: String, val size: Long, var got: Long = 0)
    private val incoming = ConcurrentHashMap<String, Incoming>()

    private fun update(id: String, f: (Transfer) -> Transfer) = Connection.ui { list = list.map { if (it.id == id) f(it) else it } }
    private fun add(t: Transfer) = Connection.ui { list = (listOf(t) + list).take(20) }
    fun clearFinished() { list = list.filter { !it.finished } }

    // ------------------------------------------------------------------ phone → PC
    fun send(ctx: Context, uris: List<Uri>) {
        Connection.scope.launch {
            for (u in uris) sendOne(ctx.applicationContext, u)
        }
    }

    private fun meta(ctx: Context, uri: Uri): Pair<String, Long> {
        var name = "file"; var size = -1L
        try {
            ctx.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { c ->
                if (c.moveToFirst()) {
                    val ni = c.getColumnIndex(OpenableColumns.DISPLAY_NAME); if (ni >= 0) name = c.getString(ni) ?: name
                    val si = c.getColumnIndex(OpenableColumns.SIZE); if (si >= 0 && !c.isNull(si)) size = c.getLong(si)
                }
            }
        } catch (_: Exception) { }
        return name to size
    }

    private suspend fun waitAck(id: String, idx: Int): Boolean {
        val d = CompletableDeferred<Boolean>()
        acks["$id:$idx"] = d
        return withTimeoutOrNull(30_000) { d.await() } ?: false
    }

    fun onAck(o: JSONObject) {
        acks.remove(o.optString("id") + ":" + o.optInt("idx", -2))?.complete(o.optBoolean("ok"))
    }

    private suspend fun sendOne(ctx: Context, uri: Uri) {
        val (name, size) = meta(ctx, uri)
        val id = UUID.randomUUID().toString().replace("-", "")
        add(Transfer(id, name, size, 0, incoming = false))
        try {
            if (!Connection.connected) throw Exception("offline")
            Connection.cmd("fileStart", "id" to id, "name" to name, "size" to size.coerceAtLeast(0))
            if (!waitAck(id, -1)) throw Exception("start refused")
            ctx.contentResolver.openInputStream(uri)?.use { input ->
                val buf = ByteArray(CHUNK)
                var idx = 0; var sent = 0L
                while (true) {
                    var n = 0
                    while (n < CHUNK) { val r = input.read(buf, n, CHUNK - n); if (r <= 0) break; n += r }
                    if (n <= 0) break
                    val ok = Connection.cmd("fileChunk", "id" to id, "idx" to idx, "data" to Base64.getEncoder().encodeToString(if (n == CHUNK) buf else buf.copyOf(n)))
                    if (!ok || !waitAck(id, idx)) throw Exception("chunk failed")
                    idx++; sent += n
                    val s = sent
                    update(id) { it.copy(done = s) }
                    if (n < CHUNK) break
                }
            } ?: throw Exception("can't open")
            Connection.cmd("fileEnd", "id" to id)
            update(id) { it.copy(finished = true, done = if (it.size > 0) it.size else it.done) }
            Connection.showBanner(Str.f("files.sent", name), BannerKind.Success)
        } catch (_: Exception) {
            Connection.cmd("fileEnd", "id" to id, "cancel" to true)
            update(id) { it.copy(finished = true, failed = true) }
            Connection.showBanner(Str.f("files.failed", name), BannerKind.Error)
        }
    }

    // ------------------------------------------------------------------ PC → phone
    fun onIncomingStart(ctx: Context, o: JSONObject) {
        val id = o.optString("id")
        val name = o.optString("name", "file").replace(Regex("[\\\\/:*?\"<>|]"), "_")
        val size = o.optLong("size")
        try {
            val inc: Incoming
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                val mime = MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substringAfterLast('.', "").lowercase()) ?: "application/octet-stream"
                val values = ContentValues().apply {
                    put(MediaStore.Downloads.DISPLAY_NAME, name)
                    put(MediaStore.Downloads.MIME_TYPE, mime)
                    put(MediaStore.Downloads.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/PC Panel")
                    put(MediaStore.Downloads.IS_PENDING, 1)
                }
                val uri = ctx.contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values) ?: return
                inc = Incoming(uri, null, ctx.contentResolver.openOutputStream(uri) ?: return, name, size)
            } else {
                val dir = File(ctx.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS), "PC Panel").apply { mkdirs() }
                val f = File(dir, name)
                inc = Incoming(null, f, f.outputStream(), name, size)
            }
            incoming[id] = inc
            add(Transfer(id, name, size, 0, incoming = true))
        } catch (_: Exception) { }
    }

    fun onIncomingChunk(o: JSONObject) {
        val inc = incoming[o.optString("id")] ?: return
        try {
            val bytes = Base64.getDecoder().decode(o.optString("data"))
            inc.out.write(bytes)
            inc.got += bytes.size
            val got = inc.got
            update(o.optString("id")) { it.copy(done = got) }
        } catch (_: Exception) { }
    }

    fun onIncomingEnd(ctx: Context, o: JSONObject) {
        val id = o.optString("id")
        val inc = incoming.remove(id) ?: return
        try { inc.out.close() } catch (_: Exception) { }
        var openUri: Uri? = null
        try {
            if (inc.uri != null) {
                ctx.contentResolver.update(inc.uri, ContentValues().apply { put(MediaStore.Downloads.IS_PENDING, 0) }, null, null)
                openUri = inc.uri
            } else if (inc.file != null) {
                openUri = FileProvider.getUriForFile(ctx, ctx.packageName + ".files", inc.file)
            }
        } catch (_: Exception) { }
        update(id) { it.copy(finished = true, done = inc.got) }
        Connection.showBanner(Str.f("files.received", inc.name), BannerKind.Success)
        notifyReceived(ctx, inc.name, openUri)
    }

    private fun notifyReceived(ctx: Context, name: String, uri: Uri?) {
        try {
            val view = if (uri != null) {
                val i = Intent(Intent.ACTION_VIEW).setDataAndType(uri, ctx.contentResolver.getType(uri) ?: "*/*")
                    .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
                PendingIntent.getActivity(ctx, name.hashCode(), i, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            } else PcPanelApp.openAppIntent(ctx)
            val n = NotificationCompat.Builder(ctx, PcPanelApp.CH_FILES)
                .setSmallIcon(R.drawable.ic_stat_pcpanel)
                .setContentTitle(Str.t("files.receivedTitle"))
                .setContentText(name)
                .setAutoCancel(true)
                .setContentIntent(view)
                .build()
            (ctx.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager).notify(2000 + (name.hashCode() and 0xFFF), n)
        } catch (_: Exception) { }
    }

    fun failAll() {
        acks.values.forEach { it.complete(false) }
        acks.clear()
        incoming.values.forEach { try { it.out.close() } catch (_: Exception) { } }
        incoming.clear()
    }
}
