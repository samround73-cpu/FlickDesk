package dev.flick.app.net

import android.content.ContentResolver
import dev.flick.app.PairedPc
import dev.flick.app.SendItem
import dev.flick.app.core.FlickCrypto
import dev.flick.app.core.SecureChannel
import dev.flick.app.core.Transfers
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.File
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket

/** What a successful pairing handshake produced; becomes a [PairedPc] once confirmed. */
class PairHandshake(
    val pcId: String,
    val pcName: String,
    val host: String,
    val port: Int,
    val key: ByteArray,
    val checkNumber: String,
    val needsPcApproval: Boolean,
)

/** Phone → PC requests. Everything blocks: call from Dispatchers.IO. */
class FlickClient(private val deviceId: String, private val deviceName: String) {
    private companion object {
        /** Waits before each retry of an interrupted transfer. */
        val RETRY_DELAYS_MS = longArrayOf(1_500, 3_000, 6_000, 10_000)
    }

    // ------------------------------------------------------------------ pairing

    /**
     * ECDH handshake. With a QR [token] the phone proves it scanned the code (HMAC) and checks
     * the PC's proof back; the token also salts the key. Tries each host in turn.
     */
    fun pair(hosts: List<String>, port: Int, token: ByteArray?, fallbackName: String): PairHandshake {
        val keyPair = FlickCrypto.newKeyPair()
        val phonePub = keyPair.public.encoded
        var lastError: Exception = IOException("No address to try")

        for (host in hosts) {
            try {
                Socket().use { socket ->
                    socket.connect(InetSocketAddress(host, port), 3_000)
                    socket.soTimeout = 15_000
                    val request = JSONObject()
                        .put("v", Protocol.VERSION)
                        .put("type", "pair")
                        .put("deviceId", deviceId)
                        .put("deviceName", deviceName)
                        .put("port", Protocol.PHONE_PORT)
                        .put("pub", FlickCrypto.b64(phonePub))
                        .put("qr", token != null)
                    if (token != null) {
                        request.put("mac", FlickCrypto.b64(
                            FlickCrypto.hmac(token, FlickCrypto.utf8("flick-pair-v2"), phonePub, FlickCrypto.utf8(deviceId))))
                    }
                    Protocol.writeHeader(socket.getOutputStream(), request)
                    val reply = Protocol.readHeader(BufferedInputStream(socket.getInputStream()))
                    if (!reply.optBoolean("ok")) throw RemoteException(reply.optString("error"))

                    val pcPub = FlickCrypto.fromB64(reply.optString("pub"))
                    if (token != null) {
                        val expected = FlickCrypto.hmac(token, FlickCrypto.utf8("flick-pair-v2-pc"), pcPub, phonePub)
                        if (!FlickCrypto.sameBytes(expected, FlickCrypto.fromB64(reply.optString("mac")))) {
                            throw IOException("That PC couldn't prove it showed this QR code")
                        }
                    }
                    val secret = FlickCrypto.agree(keyPair.private, pcPub)
                    val key = FlickCrypto.hkdf(secret, token ?: FlickCrypto.ZERO_SALT, "flick-v2-key")
                    return PairHandshake(
                        pcId = reply.getString("pcId"),
                        pcName = reply.optString("name").ifBlank { fallbackName },
                        host = host,
                        port = port,
                        key = key,
                        checkNumber = FlickCrypto.checkNumber(phonePub, pcPub),
                        needsPcApproval = reply.optBoolean("needsPcApproval"),
                    )
                }
            } catch (e: RemoteException) {
                throw e // the PC answered – no point trying other addresses
            } catch (e: Exception) {
                lastError = e
            }
        }
        throw lastError
    }

    /** Completes pairing over the new encrypted channel. Waits for the PC user if needed. */
    fun confirm(h: PairHandshake) {
        val reply = withChannel(h.host, h.port, h.key, readTimeoutMs = 130_000) { ch ->
            ch.writeJson(JSONObject().put("type", "pair-confirm"))
            ch.readJson()
        }
        if (!reply.optBoolean("ok")) throw RemoteException(reply.optString("error"))
    }

    // ------------------------------------------------------------------ requests

    /** Keep-alive: tells the PC this phone's current address. Returns the reply (has "paused"). */
    fun hello(pc: PairedPc): JSONObject = withPc(pc) { ch ->
        ch.writeJson(JSONObject().put("type", "hello").put("deviceName", deviceName).put("port", Protocol.PHONE_PORT))
        ch.readJson().also { if (!it.optBoolean("ok")) throw RemoteException(it.optString("error")) }
    }

    fun sendClip(pc: PairedPc, text: String) = withPc(pc) { ch ->
        ch.writeJson(JSONObject().put("type", "clip").put("text", text))
        val reply = ch.readJson()
        if (!reply.optBoolean("ok")) throw RemoteException(reply.optString("error"))
    }

    fun unpair(pc: PairedPc) = withPc(pc) { ch ->
        ch.writeJson(JSONObject().put("type", "unpair"))
        ch.readJson()
    }

    /**
     * Streams one file – or a picked folder, zipped on the way and unpacked on the PC.
     * [edgePos] (0..1) tells the PC where along its edge to start the light, derived from the
     * angle of the flick. [batchIndex]/[batchCount] mark a stack. If the connection drops after
     * the PC accepted the file, it reconnects and resumes from the PC's copy (up to 4 times);
     * [onRetry] is told before each attempt.
     */
    fun sendFile(
        pc: PairedPc,
        resolver: ContentResolver,
        cacheDir: File,
        item: SendItem,
        edgePos: Float,
        batchIndex: Int,
        batchCount: Int,
        onRetry: (attempt: Int, of: Int) -> Unit = { _, _ -> },
        onProgress: (Float) -> Unit,
    ) {
        val isFolder = item.mime == Transfers.FOLDER_MIME
        // A folder is zipped first; some content providers don't report a size, which the protocol needs up front.
        val tempCopy: File? = when {
            isFolder -> Transfers.zipFolder(resolver, item.uri, cacheDir).first
            item.size < 0 -> copyToCache(resolver, cacheDir, item)
            else -> null
        }
        try {
            val size = tempCopy?.length() ?: item.size
            val id = if (tempCopy == null) Transfers.stableId(item.uri, size) else Transfers.newId()
            var started = false
            var attempt = 0
            while (true) {
                try {
                    withPc(pc) { ch ->
                        val header = JSONObject()
                            .put("type", "file")
                            .put("name", if (isFolder) "${item.name}.zip" else item.name)
                            .put("size", size)
                            .put("mime", if (isFolder) "application/zip" else item.mime)
                            .put("edgePos", edgePos.toDouble())
                            .put("batchIndex", batchIndex)
                            .put("batchCount", batchCount)
                            .put("id", id)
                        if (isFolder) header.put("folder", item.name)
                        ch.writeJson(header)
                        val ready = ch.readJson()
                        if (!ready.optBoolean("ok")) throw RemoteException(ready.optString("error"))
                        val offset = ready.optLong("offset", 0).coerceIn(0, size)
                        started = true

                        val input = tempCopy?.inputStream()
                            ?: resolver.openInputStream(item.uri)
                            ?: throw IOException("Can't open ${item.name}")
                        input.use { src ->
                            Transfers.skipFully(src, offset)
                            onProgress(if (size == 0L) 1f else offset.toFloat() / size)
                            ch.sendStream(src, size - offset) { sent ->
                                onProgress(if (size == 0L) 1f else (offset + sent).toFloat() / size)
                            }
                        }
                        val reply = ch.readJson()
                        if (!reply.optBoolean("ok")) throw RemoteException(reply.optString("error"))
                    }
                    return
                } catch (e: Exception) {
                    val busy = e is RemoteException && e.code == "busy"
                    val dropped = started && e !is RemoteException
                    if ((!busy && !dropped) || attempt >= RETRY_DELAYS_MS.size) throw e
                    onRetry(attempt + 1, RETRY_DELAYS_MS.size)
                    Thread.sleep(RETRY_DELAYS_MS[attempt])
                    attempt++
                }
            }
        } finally {
            tempCopy?.delete()
        }
    }

    // ------------------------------------------------------------------ plumbing

    private fun <T> withPc(pc: PairedPc, block: (SecureChannel) -> T): T =
        withChannel(pc.host, pc.port, FlickCrypto.fromB64(pc.key), 30_000, block)

    private fun <T> withChannel(host: String, port: Int, key: ByteArray, readTimeoutMs: Int, block: (SecureChannel) -> T): T =
        Socket().use { socket ->
            socket.connect(InetSocketAddress(host, port), 4_000)
            socket.soTimeout = readTimeoutMs
            socket.tcpNoDelay = true
            val input = BufferedInputStream(socket.getInputStream(), 64 * 1024)
            val out = BufferedOutputStream(socket.getOutputStream(), 64 * 1024)
            val nonce = FlickCrypto.randomBytes(16)
            Protocol.writeHeader(
                out,
                JSONObject().put("v", Protocol.VERSION).put("from", deviceId).put("nonce", FlickCrypto.b64(nonce)),
            )
            val open = Protocol.readHeader(input)
            if (!open.optBoolean("ok")) throw RemoteException(open.optString("error"))
            block(SecureChannel(input, out, key, nonce, isClient = true))
        }

    private fun copyToCache(resolver: ContentResolver, cacheDir: File, item: SendItem): File {
        val file = File.createTempFile("flick-", ".tmp", cacheDir)
        try {
            val input = resolver.openInputStream(item.uri) ?: throw IOException("Can't open ${item.name}")
            input.use { src -> file.outputStream().use { dst -> src.copyTo(dst) } }
            return file
        } catch (e: Exception) {
            file.delete()
            throw e
        }
    }
}
