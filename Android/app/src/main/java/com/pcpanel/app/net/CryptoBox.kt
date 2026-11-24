package com.pcpanel.app.net

import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * AES-256-GCM, key = SHA-256(pairing token).
 * Wire format: base64(nonce[12] | ciphertext | tag[16]) — identical to the PC side.
 */
class CryptoBox(token: String) {
    private val key = SecretKeySpec(
        MessageDigest.getInstance("SHA-256").digest(token.toByteArray(Charsets.UTF_8)), "AES"
    )
    private val rnd = SecureRandom()

    fun encrypt(plain: String): String {
        val nonce = ByteArray(12).also { rnd.nextBytes(it) }
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.ENCRYPT_MODE, key, GCMParameterSpec(128, nonce))
        val ct = c.doFinal(plain.toByteArray(Charsets.UTF_8))
        return Base64.getEncoder().encodeToString(nonce + ct)
    }

    fun decrypt(b64: String): String {
        val d = Base64.getDecoder().decode(b64)
        require(d.size >= 28)
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(128, d, 0, 12))
        return String(c.doFinal(d, 12, d.size - 12), Charsets.UTF_8)
    }
}
