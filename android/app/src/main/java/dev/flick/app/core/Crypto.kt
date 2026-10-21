package dev.flick.app.core

import android.util.Base64
import org.json.JSONObject
import java.io.DataInputStream
import java.io.EOFException
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.security.KeyFactory
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.MessageDigest
import java.security.PrivateKey
import java.security.SecureRandom
import java.security.spec.ECGenParameterSpec
import java.security.spec.X509EncodedKeySpec
import javax.crypto.Cipher
import javax.crypto.KeyAgreement
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * Crypto for protocol v2 — byte-for-byte the same as FlickDesk's Crypto.cs:
 *  • pairing: ECDH P-256, public keys as X.509 SubjectPublicKeyInfo (DER)
 *  • K = HKDF-SHA256(ECDH secret, salt = QR token or 32 zero bytes, "flick-v2-key")
 *  • per connection S = HKDF-SHA256(K, salt = 16-byte nonce, "flick-v2-session")
 *  • frames: AES-256-GCM, nonce = [0,0,0,dir] + 8-byte BE counter (dir 0 = client→server)
 *  • check number = SHA-256("flick-v2-sas" ‖ phonePub ‖ pcPub)[0..4] BE mod 10^6
 */
object FlickCrypto {
    val ZERO_SALT = ByteArray(32)
    private val random = SecureRandom()

    fun randomBytes(n: Int) = ByteArray(n).also { random.nextBytes(it) }

    fun newKeyPair(): KeyPair =
        KeyPairGenerator.getInstance("EC").apply { initialize(ECGenParameterSpec("secp256r1")) }.generateKeyPair()

    fun agree(mine: PrivateKey, otherSpki: ByteArray): ByteArray {
        val other = KeyFactory.getInstance("EC").generatePublic(X509EncodedKeySpec(otherSpki))
        return KeyAgreement.getInstance("ECDH").run {
            init(mine)
            doPhase(other, true)
            generateSecret()
        }
    }

    fun hmac(key: ByteArray, vararg parts: ByteArray): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        parts.forEach { mac.update(it) }
        return mac.doFinal()
    }

    /** RFC 5869 with SHA-256, one 32-byte output block. */
    fun hkdf(ikm: ByteArray, salt: ByteArray, info: String): ByteArray {
        val prk = hmac(salt, ikm)
        return hmac(prk, info.toByteArray(Charsets.UTF_8), byteArrayOf(1)).copyOf(32)
    }

    fun checkNumber(phonePub: ByteArray, pcPub: ByteArray): String {
        val sha = MessageDigest.getInstance("SHA-256")
        sha.update("flick-v2-sas".toByteArray(Charsets.UTF_8))
        sha.update(phonePub)
        sha.update(pcPub)
        val h = sha.digest()
        val v = ((h[0].toLong() and 0xff) shl 24) or ((h[1].toLong() and 0xff) shl 16) or
            ((h[2].toLong() and 0xff) shl 8) or (h[3].toLong() and 0xff)
        val digits = (v % 1_000_000L).toString().padStart(6, '0')
        return digits.substring(0, 3) + " " + digits.substring(3)
    }

    fun sameBytes(a: ByteArray, b: ByteArray) = MessageDigest.isEqual(a, b)

    fun b64(b: ByteArray): String = Base64.encodeToString(b, Base64.NO_WRAP)
    fun fromB64(s: String?): ByteArray = if (s.isNullOrEmpty()) ByteArray(0) else Base64.decode(s, Base64.DEFAULT)
    fun fromB64Url(s: String?): ByteArray =
        if (s.isNullOrEmpty()) ByteArray(0) else Base64.decode(s, Base64.URL_SAFE or Base64.NO_PADDING or Base64.NO_WRAP)

    fun utf8(s: String) = s.toByteArray(Charsets.UTF_8)
}

/**
 * Encrypted, authenticated channel over one TCP connection (blocking I/O — use Dispatchers.IO).
 * Wire frame: [4-byte BE length][AES-GCM ciphertext ‖ 16-byte tag]. Implicit counter nonces
 * mean frames can't be replayed, reordered or dropped without the next decrypt failing.
 */
class SecureChannel(
    input: InputStream,
    private val out: OutputStream,
    longTermKey: ByteArray,
    connectionNonce: ByteArray,
    private val isClient: Boolean,
) {
    private val input = DataInputStream(input)
    private val key = SecretKeySpec(FlickCrypto.hkdf(longTermKey, connectionNonce, "flick-v2-session"), "AES")
    private var sendCounter = 0L
    private var recvCounter = 0L

    private fun nonce(sending: Boolean): ByteArray {
        val n = ByteArray(12)
        val fromClient = if (sending) isClient else !isClient
        n[3] = if (fromClient) 0 else 1
        val c = if (sending) sendCounter++ else recvCounter++
        for (i in 0 until 8) n[4 + i] = (c ushr (56 - 8 * i)).toByte()
        return n
    }

    fun writeFrame(plain: ByteArray, offset: Int = 0, length: Int = plain.size) {
        require(length <= MAX_PLAIN_FRAME) { "Frame too large" }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key, GCMParameterSpec(128, nonce(true)))
        val sealed = cipher.doFinal(plain, offset, length)
        val n = sealed.size
        out.write(byteArrayOf((n ushr 24).toByte(), (n ushr 16).toByte(), (n ushr 8).toByte(), n.toByte()))
        out.write(sealed)
    }

    fun readFrame(): ByteArray {
        val len = try { input.readInt() } catch (e: EOFException) { throw IOException("Connection closed") }
        if (len < 16 || len > MAX_PLAIN_FRAME + 16) throw IOException("Bad frame length")
        val buf = ByteArray(len)
        input.readFully(buf)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(128, nonce(false)))
        return cipher.doFinal(buf) // AEADBadTagException if anything was tampered with
    }

    fun writeJson(message: JSONObject) {
        writeFrame(message.toString().toByteArray(Charsets.UTF_8))
        out.flush()
    }

    fun readJson(): JSONObject = JSONObject(String(readFrame(), Charsets.UTF_8))

    fun sendStream(source: InputStream, size: Long, onProgress: (Long) -> Unit = {}) {
        val buffer = ByteArray(CHUNK)
        var remaining = size
        while (remaining > 0) {
            val want = minOf(buffer.size.toLong(), remaining).toInt()
            var got = 0
            while (got < want) {
                val n = source.read(buffer, got, want - got)
                if (n < 0) throw EOFException("File ended early")
                got += n
            }
            writeFrame(buffer, 0, got)
            remaining -= got
            onProgress(size - remaining)
        }
        out.flush()
    }

    fun receiveToStream(destination: OutputStream, size: Long, onProgress: (Long) -> Unit = {}) {
        var remaining = size
        while (remaining > 0) {
            val chunk = readFrame()
            if (chunk.isEmpty() || chunk.size > remaining) throw IOException("Unexpected chunk size")
            destination.write(chunk)
            remaining -= chunk.size
            onProgress(size - remaining)
        }
    }

    companion object {
        const val MAX_PLAIN_FRAME = 1 shl 20
        private const val CHUNK = 256 * 1024
    }
}
