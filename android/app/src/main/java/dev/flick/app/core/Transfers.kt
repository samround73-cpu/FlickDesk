package dev.flick.app.core

import android.content.ContentResolver
import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.os.Environment
import android.provider.DocumentsContract
import android.provider.MediaStore
import android.webkit.MimeTypeMap
import dev.flick.app.net.Protocol
import java.io.BufferedOutputStream
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.io.InputStream
import java.security.MessageDigest
import java.util.UUID
import java.util.zip.Deflater
import java.util.zip.ZipEntry
import java.util.zip.ZipFile
import java.util.zip.ZipOutputStream

/**
 * Large and resumable transfers – the same scheme as FlickDesk's Transfers.cs:
 *  • every file carries an `id`; the receiver keeps a partial copy keyed by sender + id + size and
 *    answers "ready" with `offset` = bytes it already has, so a dropped transfer resumes;
 *  • a folder travels as one zip (no compression) with `folder` = its name and is unpacked on arrival.
 */
object Transfers {
    /** Marks a picked folder (a document tree) in the Files tab. */
    const val FOLDER_MIME = "vnd.flick/folder"

    /** At or above this size a transfer gets a progress notification. */
    const val PROGRESS_THRESHOLD = 16L * 1024 * 1024

    private const val PARTIAL_LIFETIME_MS = 3L * 24 * 60 * 60 * 1000

    private fun hex(bytes: ByteArray, chars: Int) = bytes.joinToString("") { "%02x".format(it) }.take(chars)
    private fun sha(s: String) = MessageDigest.getInstance("SHA-256").digest(s.toByteArray(Charsets.UTF_8))

    fun stableId(uri: Uri, size: Long): String = hex(sha("$uri|$size"), 32)
    fun newId(): String = UUID.randomUUID().toString().replace("-", "")

    fun cleanId(id: String?): String? =
        id?.takeIf { it.isNotEmpty() && it.length <= 64 && it.all { c -> c.isLetterOrDigit() && c.code < 128 || c == '-' || c == '_' } }

    private fun incomingDir(context: Context) = File(context.filesDir, "incoming").apply { mkdirs() }

    fun partialFile(context: Context, pcId: String, id: String?, size: Long): File {
        val key = if (id == null) newId() else hex(sha("$pcId|$id|$size"), 32)
        return File(incomingDir(context), "$key.part")
    }

    fun cleanupOldPartials(context: Context) {
        val now = System.currentTimeMillis()
        incomingDir(context).listFiles()?.forEach { if (now - it.lastModified() > PARTIAL_LIFETIME_MS) it.delete() }
        context.cacheDir.listFiles { f -> f.name.startsWith("flick-folder-") }?.forEach { if (now - it.lastModified() > 86_400_000) it.delete() }
    }

    /** Skips exactly [n] bytes (some streams skip short or not at all). */
    fun skipFully(input: InputStream, n: Long) {
        var left = n
        val scratch = ByteArray(64 * 1024)
        while (left > 0) {
            val skipped = input.skip(left)
            if (skipped > 0) { left -= skipped; continue }
            val read = input.read(scratch, 0, minOf(scratch.size.toLong(), left).toInt())
            if (read < 0) throw IOException("File ended early")
            left -= read
        }
    }

    // ------------------------------------------------------------------ sending a folder

    fun folderName(resolver: ContentResolver, tree: Uri): String {
        val doc = DocumentsContract.buildDocumentUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree))
        resolver.query(doc, arrayOf(DocumentsContract.Document.COLUMN_DISPLAY_NAME), null, null, null)?.use { c ->
            if (c.moveToFirst()) c.getString(0)?.let { return it }
        }
        return "Folder"
    }

    /** Zips a picked folder (no compression) into the cache. Returns the zip and how many files went in. */
    fun zipFolder(resolver: ContentResolver, tree: Uri, cacheDir: File): Pair<File, Int> {
        val zip = File(cacheDir, "flick-folder-${newId()}.zip")
        var files = 0
        try {
            ZipOutputStream(BufferedOutputStream(FileOutputStream(zip), 256 * 1024)).use { out ->
                out.setLevel(Deflater.NO_COMPRESSION)
                fun walk(docId: String, prefix: String) {
                    val children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, docId)
                    val rows = mutableListOf<Triple<String, String, String?>>()
                    resolver.query(
                        children,
                        arrayOf(
                            DocumentsContract.Document.COLUMN_DOCUMENT_ID,
                            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                            DocumentsContract.Document.COLUMN_MIME_TYPE,
                        ),
                        null, null, null,
                    )?.use { c ->
                        while (c.moveToNext()) {
                            val id = c.getString(0) ?: continue
                            val name = c.getString(1) ?: continue
                            rows += Triple(id, name, c.getString(2))
                        }
                    }
                    for ((id, name, mime) in rows) {
                        if (mime == DocumentsContract.Document.MIME_TYPE_DIR) {
                            out.putNextEntry(ZipEntry("$prefix$name/"))
                            out.closeEntry()
                            walk(id, "$prefix$name/")
                        } else {
                            out.putNextEntry(ZipEntry(prefix + name))
                            resolver.openInputStream(DocumentsContract.buildDocumentUriUsingTree(tree, id))?.use { it.copyTo(out, 256 * 1024) }
                            out.closeEntry()
                            files++
                        }
                    }
                }
                walk(DocumentsContract.getTreeDocumentId(tree), "")
            }
            return zip to files
        } catch (e: Exception) {
            zip.delete()
            throw e
        }
    }

    // ------------------------------------------------------------------ receiving

    private fun collectionFor(mime: String): Pair<Uri, String> {
        val volume = MediaStore.VOLUME_EXTERNAL_PRIMARY
        return when {
            mime.startsWith("image/") -> MediaStore.Images.Media.getContentUri(volume) to Environment.DIRECTORY_PICTURES
            mime.startsWith("video/") -> MediaStore.Video.Media.getContentUri(volume) to Environment.DIRECTORY_MOVIES
            else -> MediaStore.Downloads.getContentUri(volume) to Environment.DIRECTORY_DOWNLOADS
        }
    }

    /** Copies a fully received file into Pictures/Movies/Download → Flick. */
    fun publishFile(context: Context, source: File, name: String, mime: String): Uri {
        val (collection, folder) = collectionFor(mime)
        return insert(context, collection, "$folder/Flick", name, mime) { out -> source.inputStream().use { it.copyTo(out, 256 * 1024) } }
    }

    class Unpacked(val firstUri: Uri?, val firstName: String?, val firstMime: String?, val files: Int, val folder: String)

    /**
     * Unpacks a received folder zip into Download/Flick/<name>/… . Every path segment is cleaned and
     * nothing can climb out of the folder. Stops if the contents add up to more than [maxBytes].
     */
    fun unpackFolder(context: Context, zipFile: File, name: String, maxBytes: Long): Unpacked {
        val base = uniqueFolder(context, Protocol.safeFileName(name))
        val downloads = MediaStore.Downloads.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY)
        var first: Uri? = null
        var firstName: String? = null
        var firstMime: String? = null
        var files = 0
        var total = 0L
        ZipFile(zipFile).use { zip ->
            for (entry in zip.entries()) {
                if (entry.isDirectory) continue
                val segments = entry.name.replace('\\', '/').split('/')
                    .filter { it.isNotEmpty() && it != "." && it != ".." }
                    .map { Protocol.safeFileName(it) }
                if (segments.isEmpty()) continue
                total += entry.size.coerceAtLeast(0)
                if (total > maxBytes) throw IOException("Folder is larger than allowed")
                val fileName = segments.last()
                val dir = (listOf("${Environment.DIRECTORY_DOWNLOADS}/Flick/$base") + segments.dropLast(1)).joinToString("/")
                val mime = mimeFor(fileName)
                val uri = insert(context, downloads, dir, fileName, mime) { out -> zip.getInputStream(entry).use { it.copyTo(out, 256 * 1024) } }
                if (first == null || (firstMime?.startsWith("image/") != true && mime.startsWith("image/"))) {
                    first = uri; firstName = fileName; firstMime = mime
                }
                files++
            }
        }
        return Unpacked(first, firstName, firstMime, files, base)
    }

    private fun mimeFor(name: String): String =
        MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substringAfterLast('.', "").lowercase())
            ?: "application/octet-stream"

    /** "Holiday", or "Holiday (2)" if Flick already made a Download/Flick/Holiday. */
    private fun uniqueFolder(context: Context, name: String): String {
        val downloads = MediaStore.Downloads.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY)
        fun taken(candidate: String): Boolean =
            context.contentResolver.query(
                downloads, arrayOf(MediaStore.MediaColumns._ID),
                "${MediaStore.MediaColumns.RELATIVE_PATH} LIKE ?",
                arrayOf("${Environment.DIRECTORY_DOWNLOADS}/Flick/$candidate/%"), null,
            )?.use { it.count > 0 } ?: false
        if (!taken(name)) return name
        var i = 2
        while (taken("$name ($i)")) i++
        return "$name ($i)"
    }

    private fun insert(context: Context, collection: Uri, relativePath: String, name: String, mime: String, write: (java.io.OutputStream) -> Unit): Uri {
        val resolver = context.contentResolver
        val values = ContentValues().apply {
            put(MediaStore.MediaColumns.DISPLAY_NAME, name)
            put(MediaStore.MediaColumns.MIME_TYPE, mime)
            put(MediaStore.MediaColumns.RELATIVE_PATH, relativePath)
            put(MediaStore.MediaColumns.IS_PENDING, 1)
        }
        val uri = resolver.insert(collection, values) ?: throw IOException("Couldn't create $name")
        try {
            (resolver.openOutputStream(uri) ?: throw IOException("Couldn't write $name")).use(write)
            resolver.update(uri, ContentValues().apply { put(MediaStore.MediaColumns.IS_PENDING, 0) }, null, null)
            return uri
        } catch (e: Exception) {
            resolver.delete(uri, null, null)
            throw e
        }
    }
}
