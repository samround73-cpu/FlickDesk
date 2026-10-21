package dev.flick.app.net

import android.content.Context
import android.net.Uri
import dev.flick.app.PairedPc
import dev.flick.app.core.FlickCrypto
import dev.flick.app.core.SecureChannel
import dev.flick.app.core.Transfers
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
 * Photos go to Pictures/Flick, videos to Movies/Flick, everything else to Download/Flick;
 * folders are unpacked into Download/Flick/<name>. Big files resume after a dropped connection.
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
        /** Bytes received so far of a big file (includes a resumed part). */
        fun onFileProgress(pc: PairedPc, key: String, name: String, done: Long, total: Long) {}
        /** The connection dropped mid-file; the partial copy is kept for a resume. */
        fun onFileInterrupted(pc: PairedPc, key: String, name: String) {}
        fun onClip(pc: PairedPc, text: String)
        fun onError(message: String)
    }

    @Volatile private var serverSocket: ServerSocket? = null
    private val active = java.util.Collections.synchronizedSet(mutableSetOf<String>())

    fun start(scope: CoroutineScope) {
        scope.launch(Dispatchers.IO) {
            runCatching { Transfers.cleanupOldPartials(context) }
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
        if (size < 0 || size > MAX_FILE_BYTES) {
            ch.writeJson(Protocol.error("bad-size"))
            return
        }
        val name = Protocol.safeFileName(h.optString("name"))
        val mime = h.optString("mime").ifBlank { "application/octet-stream" }
        val batchIndex = h.optInt("batchIndex", 0).coerceAtLeast(0)
        val batchCount = h.optInt("batchCount", 1).coerceAtLeast(1)
        val id = Transfers.cleanId(h.optString("id").ifBlank { null })
        val folder = h.optString("folder").ifBlank { null }?.let { Protocol.safeFileName(it) }
        val shownName = folder ?: name

        // Resume: a partial copy from an earlier, interrupted attempt of the same file.
        val partial = Transfers.partialFile(context, pc.id, id, size)
        if (!active.add(partial.name)) {
            ch.writeJson(Protocol.error("busy"))
            return
        }
        try {
            if (partial.length() > size) partial.delete()
            val offset = partial.length()
            ch.writeJson(Protocol.ok().put("offset", offset)) // ready
            if (offset == 0L) listener.onFileStarting(pc, shownName, batchIndex)

            var received = false
            try {
                java.io.FileOutputStream(partial, true).use { out ->
                    var last = 0L
                    ch.receiveToStream(out, size - offset) { got ->
                        val done = offset + got
                        if (done - last >= 512 * 1024 || done == size) {
                            last = done
                            if (size >= Transfers.PROGRESS_THRESHOLD) listener.onFileProgress(pc, partial.name, shownName, done, size)
                        }
                    }
                }
                received = true

                if (folder != null) {
                    // Unpacking can take a while: confirm receipt first so the PC isn't left waiting.
                    runCatching { ch.writeJson(Protocol.ok().put("savedAs", folder)) } // the files still get saved
                    val unpacked = Transfers.unpackFolder(context, partial, folder, MAX_FILE_BYTES)
                    partial.delete()
                    val uri = unpacked.firstUri ?: throw IOException("The folder was empty")
                    listener.onFileReceived(pc, uri, "${unpacked.folder} (${unpacked.files} files)", unpacked.firstMime ?: "application/octet-stream", size, batchIndex, batchCount)
                } else {
                    // Confirm first: copying a big video into the gallery can outlast the PC's wait.
                    runCatching { ch.writeJson(Protocol.ok().put("savedAs", name)) } // the file still gets saved
                    val uri = Transfers.publishFile(context, partial, name, mime)
                    partial.delete()
                    listener.onFileReceived(pc, uri, name, mime, size, batchIndex, batchCount)
                }
            } catch (e: Exception) {
                // Interrupted downloads keep their partial copy so the PC can resume; anything
                // that arrived but couldn't be saved (or has no id to resume by) is dropped.
                if (received || id == null) partial.delete()
                else if (size >= Transfers.PROGRESS_THRESHOLD) listener.onFileInterrupted(pc, partial.name, shownName)
                runCatching { ch.writeJson(Protocol.error("save-failed")) }
                throw e
            }
        } finally {
            active.remove(partial.name)
        }
    }

    companion object {
        private const val MAX_FILE_BYTES = 50L * 1024 * 1024 * 1024
    }
}
