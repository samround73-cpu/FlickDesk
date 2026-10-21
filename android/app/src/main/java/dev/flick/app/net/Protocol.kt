package dev.flick.app.net

import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.EOFException
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream

/**
 * Wire protocol v2, shared with FlickDesk on Windows (see README "Protocol").
 * Every TCP connection starts with ONE plain UTF-8 JSON line ending in '\n':
 *   {"type":"pair",…}               – pairing (ECDH), plain reply line
 *   {"from":"<id>","nonce":"<b64>"} – opens an encrypted channel: plain {"ok":true} reply,
 *                                     then AES-GCM frames (see SecureChannel).
 */
object Protocol {
    const val VERSION = 2
    const val BEACON_PORT = 47800 // UDP broadcast: PC -> phones
    const val PC_PORT = 47801     // TCP: phone -> PC
    const val PHONE_PORT = 47802  // TCP: PC -> phone

    private const val MAX_HEADER_BYTES = 64 * 1024

    /** Pass a BufferedInputStream and keep using the SAME stream afterwards. */
    fun readHeader(input: InputStream): JSONObject {
        val buffer = ByteArrayOutputStream()
        while (true) {
            val b = input.read()
            if (b < 0) throw EOFException("Connection closed before the header was complete")
            if (b == '\n'.code) break
            buffer.write(b)
            if (buffer.size() > MAX_HEADER_BYTES) throw IOException("Header too large")
        }
        return JSONObject(String(buffer.toByteArray(), Charsets.UTF_8))
    }

    fun writeHeader(out: OutputStream, header: JSONObject) {
        out.write((header.toString() + "\n").toByteArray(Charsets.UTF_8))
        out.flush()
    }

    fun ok() = JSONObject().put("ok", true)
    fun error(code: String) = JSONObject().put("ok", false).put("error", code)

    /** Never trust a file name from the network. */
    fun safeFileName(name: String?): String {
        var n = (name ?: "").substringAfterLast('/').substringAfterLast('\\')
        n = n.replace(Regex("[\\u0000-\\u001f<>:\"|?*]"), "_").trim().trimEnd('.')
        if (n.isEmpty() || n == "..") n = "flicked-file"
        if (n.length > 150) {
            val ext = n.substringAfterLast('.', "").let { if (it.length in 1..15) ".$it" else "" }
            n = n.take(150 - ext.length) + ext
        }
        return n
    }

    /** Turns protocol error codes into something a person can act on. */
    fun describe(code: String?): String = when (code) {
        "not-paired" -> "This PC doesn't know this phone any more – pair again"
        "paused" -> "The PC has paused receiving"
        "denied" -> "The PC declined"
        "qr-expired" -> "That QR code expired – show a new one on the PC"
        "bad-code" -> "That QR code didn't match – try scanning again"
        "bad-size" -> "File too large"
        "save-failed" -> "The PC couldn't save the file"
        null, "" -> "Something went wrong"
        else -> code
    }
}

/** An error reply from the other side (not a network failure). */
class RemoteException(val code: String) : Exception(Protocol.describe(code))
