package dev.flick.app.net

import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.os.Environment
import android.provider.MediaStore
import dev.flick.app.PairedPc
import dev.flick.app.core.FlickCrypto
import dev.flick.app.core.SecureChannel
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.IOException
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketException

/**
 * Receives files and clipboard text from paired PCs on TCP 47802, over the encrypted channel.
 * Photos go to Pictures/Flick, videos to Movies/Flick, everything else to Download/Flick.
 */
class FlickServer(
    private val context: Context,
    private val findPc: (pcId: String) -> PairedPc?,
    private val listener: Listener,
) {
    interface Listener {
        fun onPcSeen(pc: PairedPc, address: String)
        fun onFileStarting(pc: PairedPc, name: String, batchIndex: Int)
        fun onFileReceived(pc: PairedPc, uri: Uri, name: String, mime: String, size: Long, batchIndex: Int, batchCount: Int)
        fun onClip(pc: PairedPc, text: String)
        fun onError(message: String)
    }

    @Volatile private var serverSocket: ServerSocket? = null

    fun start(scope: CoroutineScope) {
        scope.launch(Dispatchers.IO) {
            val server = try {
                ServerSocket().apply {
                    reuseAddress = true
                    bind(InetSocketAddress(Protocol.PHONE_PORT))
                }
            } catch (e: IOException) {
                listener.onError("Can't receive files: port ${Protocol.PHONE_PORT} is busy")
                return@launch
            }
            serverSocket = server
            try {
                while (isActive) {
                    val client = server.accept()
                    launch { handle(client) }
                }
            } catch (_: SocketException) {
                // closed by stop()
            } finally {
                server.close()
            }
        }
    }

    fun stop() {
        serverSocket?.close()
    }

    private fun handle(socket: Socket) {
        socket.use { s ->
            try {
                s.soTimeout = 30_000
                val input = BufferedInputStream(s.getInputStream(), 64 * 1024)
                val out = BufferedOutputStream(s.getOutputStream())
                val first = Protocol.readHeader(input)

                val pc = findPc(first.optString("from"))
                val nonce = FlickCrypto.fromB64(first.optString("nonce"))
                if (pc == null || nonce.size != 16) {
                    Protocol.writeHeader(out, Protocol.error("not-paired"))
                    return
                }
                Protocol.writeHeader(out, Protocol.ok())
                val address = s.inetAddress?.hostAddress
                if (address != null) listener.onPcSeen(pc, address)

                val ch = SecureChannel(input, out, FlickCrypto.fromB64(pc.key), nonce, isClient = false)
                val msg = ch.readJson()
                when (msg.optString("type")) {
                    "file" -> receiveFile(ch, msg, pc)
                    "clip" -> {
                        val text = msg.optString("text")
                        if (text.isEmpty() || text.length > 512 * 1024) {
                            ch.writeJson(Protocol.error("bad-size"))
                        } else {
                            ch.writeJson(Protocol.ok())
                            listener.onClip(pc, text)
                        }
                    }
                    else -> ch.writeJson(Protocol.error("unknown-type"))
                }
            } catch (e: Exception) {
                listener.onError("Receive failed: ${e.message ?: e.javaClass.simpleName}")
            }
        }
    }

    private fun receiveFile(ch: SecureChannel, h: org.json.JSONObject, pc: PairedPc) {
        val size = h.optLong("size", -1)
        if (size < 0) {
            ch.writeJson(Protocol.error("bad-size"))
            return
        }
        val name = Protocol.safeFileName(h.optString("name"))
        val mime = h.optString("mime").ifBlank { "application/octet-stream" }
        val batchIndex = h.optInt("batchIndex", 0).coerceAtLeast(0)
        val batchCount = h.optInt("batchCount", 1).coerceAtLeast(1)

        ch.writeJson(Protocol.ok()) // ready
        listener.onFileStarting(pc, name, batchIndex)

        val uri = save(ch, name, mime, size)
        ch.writeJson(Protocol.ok().put("savedAs", name))
        listener.onFileReceived(pc, uri, name, mime, size, batchIndex, batchCount)
    }

    private fun save(ch: SecureChannel, name: String, mime: String, size: Long): Uri {
        val resolver = context.contentResolver
        val volume = MediaStore.VOLUME_EXTERNAL_PRIMARY
        val (collection, folder) = when {
            mime.startsWith("image/") -> MediaStore.Images.Media.getContentUri(volume) to Environment.DIRECTORY_PICTURES
            mime.startsWith("video/") -> MediaStore.Video.Media.getContentUri(volume) to Environment.DIRECTORY_MOVIES
            else -> MediaStore.Downloads.getContentUri(volume) to Environment.DIRECTORY_DOWNLOADS
        }
        val values = ContentValues().apply {
            put(MediaStore.MediaColumns.DISPLAY_NAME, name)
            put(MediaStore.MediaColumns.MIME_TYPE, mime)
            put(MediaStore.MediaColumns.RELATIVE_PATH, "$folder/Flick")
            put(MediaStore.MediaColumns.IS_PENDING, 1)
        }
        val uri = resolver.insert(collection, values) ?: throw IOException("Couldn't create $name")
        try {
            val out = resolver.openOutputStream(uri) ?: throw IOException("Couldn't write $name")
            out.use { ch.receiveToStream(it, size) }
            resolver.update(uri, ContentValues().apply { put(MediaStore.MediaColumns.IS_PENDING, 0) }, null, null)
            return uri
        } catch (e: Exception) {
            resolver.delete(uri, null, null)
            throw e
        }
    }
}
