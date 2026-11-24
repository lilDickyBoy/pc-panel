package com.pcpanel.app

import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.widget.Toast
import com.pcpanel.app.data.Prefs
import com.pcpanel.app.net.Connection
import com.pcpanel.app.service.Transfers
import com.pcpanel.app.ui.Str

/**
 * "Share → PC Panel" from any app:
 *  • a link (YouTube, Spotify, a web page…) opens on the PC
 *  • other text goes to the PC clipboard
 *  • files / photos are sent to the PC's Downloads\PC Panel folder
 */
class ShareActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        handle(intent)
        finish()
    }

    @Suppress("DEPRECATION")
    private fun streams(i: Intent): List<Uri> {
        val out = ArrayList<Uri>()
        if (i.action == Intent.ACTION_SEND_MULTIPLE) {
            val l: List<Uri>? = if (Build.VERSION.SDK_INT >= 33) i.getParcelableArrayListExtra(Intent.EXTRA_STREAM, Uri::class.java) else i.getParcelableArrayListExtra(Intent.EXTRA_STREAM)
            l?.let { out.addAll(it) }
        } else {
            val u: Uri? = if (Build.VERSION.SDK_INT >= 33) i.getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java) else i.getParcelableExtra(Intent.EXTRA_STREAM)
            u?.let { out.add(it) }
        }
        return out
    }

    private fun handle(i: Intent?) {
        if (i == null) return
        if (Prefs.pc == null) { Toast.makeText(this, Str.t("share.notPaired"), Toast.LENGTH_LONG).show(); return }
        val files = streams(i)
        if (files.isNotEmpty()) {
            // keep read access to the shared files after this activity finishes
            files.forEach { u -> try { grantUriPermission(packageName, u, Intent.FLAG_GRANT_READ_URI_PERMISSION) } catch (_: Exception) { } }
            Connection.whenConnected { Transfers.send(applicationContext, files) }
            Toast.makeText(this, Str.f("share.sendingFiles", files.size), Toast.LENGTH_SHORT).show()
            return
        }
        val text = i.getStringExtra(Intent.EXTRA_TEXT)?.trim() ?: return
        val url = Regex("https?://\\S+").find(text)?.value
        if (url != null) {
            Connection.whenConnected { Connection.openUrl(url) }
            Toast.makeText(this, Str.t("share.openingLink"), Toast.LENGTH_SHORT).show()
        } else {
            Connection.whenConnected { Connection.cmd("clip", "text" to text, "explicit" to true) }
            Toast.makeText(this, Str.t("share.clipboard"), Toast.LENGTH_SHORT).show()
        }
    }
}
