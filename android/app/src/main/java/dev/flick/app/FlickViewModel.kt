package dev.flick.app

import android.app.Application
import android.content.ContentResolver
import android.content.ContentUris
import android.net.Uri
import android.os.Bundle
import android.provider.MediaStore
import android.provider.OpenableColumns
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import dev.flick.app.core.CoreEvent
import dev.flick.app.core.FlickCore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

enum class Tab(val label: String) { PHOTOS("Photos"), FILES("Files"), CLIPBOARD("Clipboard"), HISTORY("History") }

/** Screen-level state on top of [FlickCore]: the library, selection, tabs and one-off UI events. */
class FlickViewModel(app: Application) : AndroidViewModel(app) {
    val core = FlickCore.also { it.init(app) }
    private val resolver = app.contentResolver

    private val _media = MutableStateFlow<List<SendItem>>(emptyList())
    val media: StateFlow<List<SendItem>> = _media.asStateFlow()

    private val _mediaDenied = MutableStateFlow(false)
    val mediaDenied: StateFlow<Boolean> = _mediaDenied.asStateFlow()

    private val _files = MutableStateFlow<List<SendItem>>(emptyList())
    /** Items picked or shared into Flick ("Files" tab). */
    val files: StateFlow<List<SendItem>> = _files.asStateFlow()

    private val _selection = MutableStateFlow<List<String>>(emptyList())
    /** Selected item keys, in tap order (the stack). */
    val selection: StateFlow<List<String>> = _selection.asStateFlow()

    private val _tab = MutableStateFlow(Tab.PHOTOS)
    val tab: StateFlow<Tab> = _tab.asStateFlow()

    private val _clipDraft = MutableStateFlow("")
    val clipDraft: StateFlow<String> = _clipDraft.asStateFlow()

    private val _messages = MutableSharedFlow<String>(extraBufferCapacity = 8)
    val messages: SharedFlow<String> = _messages.asSharedFlow()

    val events: SharedFlow<CoreEvent> get() = core.events

    fun setTab(t: Tab) { _tab.value = t }
    fun setClipDraft(text: String) { _clipDraft.value = text }
    fun toast(text: String) { _messages.tryEmit(text) }

    // ------------------------------------------------------------------ selection / stack

    fun toggleSelect(item: SendItem) = _selection.update { if (item.key in it) it - item.key else it + item.key }
    fun clearSelection() { _selection.value = emptyList() }

    fun selectedItems(): List<SendItem> {
        val all = (_files.value + _media.value).associateBy { it.key }
        return _selection.value.mapNotNull { all[it] }
    }

    /** What a lift of [item] carries: the whole stack if it's part of it, else just itself. */
    fun itemsFor(item: SendItem): List<SendItem> =
        if (item.key in _selection.value) selectedItems() else listOf(item)

    // ------------------------------------------------------------------ sending

    /** Returns false (and explains) if no PC is set up for that direction. */
    fun flick(items: List<SendItem>, side: Side, edgePos: Float): Boolean {
        val pc = core.pcFor(side) ?: run {
            toast(if (core.pcs.value.isEmpty()) "Pair a PC first – tap + at the top" else "No PC on the ${side.label.lowercase()} – tap a PC to change its side")
            return false
        }
        val wasStack = items.size > 1 || items.any { it.key in _selection.value }
        viewModelScope.launch {
            val sent = core.send(items, pc, edgePos)
            if (sent > 0) {
                val sentKeys = items.take(sent).map { it.key }.toSet()
                _files.update { list -> list.filterNot { it.key in sentKeys } }
                if (wasStack) _selection.update { list -> list.filterNot { it in sentKeys } }
            }
        }
        return true
    }

    fun sendSelectionTo(pc: PairedPc) {
        val items = selectedItems()
        if (items.isEmpty()) return
        flick(items, pc.side, 0.5f)
    }

    fun sendClip(pc: PairedPc, text: String) {
        if (text.isBlank()) return
        viewModelScope.launch {
            if (core.sendClip(pc, text)) _clipDraft.value = ""
        }
    }

    // ------------------------------------------------------------------ content

    fun loadMedia() {
        viewModelScope.launch {
            val items = withContext(Dispatchers.IO) { runCatching { queryRecentMedia() }.getOrDefault(emptyList()) }
            _media.value = items
            _mediaDenied.value = false
        }
    }

    fun setMediaDenied() { _mediaDenied.value = true }

    fun addFiles(uris: List<Uri>) {
        viewModelScope.launch {
            val items = withContext(Dispatchers.IO) { uris.mapNotNull(::describe) }
            _files.update { list -> (items + list).distinctBy { it.key } }
            _tab.value = Tab.FILES
        }
    }

    fun removeFile(item: SendItem) {
        _files.update { list -> list.filterNot { it.key == item.key } }
        _selection.update { it - item.key }
    }

    fun receiveSharedText(text: String) {
        _clipDraft.value = text
        _tab.value = Tab.CLIPBOARD
    }

    private fun queryRecentMedia(): List<SendItem> {
        val files = MediaStore.Files.getContentUri(MediaStore.VOLUME_EXTERNAL)
        val type = MediaStore.Files.FileColumns.MEDIA_TYPE
        val projection = arrayOf(
            MediaStore.MediaColumns._ID,
            type,
            MediaStore.MediaColumns.DISPLAY_NAME,
            MediaStore.MediaColumns.MIME_TYPE,
            MediaStore.MediaColumns.SIZE,
        )
        val args = Bundle().apply {
            putString(ContentResolver.QUERY_ARG_SQL_SELECTION, "$type=? OR $type=?")
            putStringArray(
                ContentResolver.QUERY_ARG_SQL_SELECTION_ARGS,
                arrayOf(
                    MediaStore.Files.FileColumns.MEDIA_TYPE_IMAGE.toString(),
                    MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO.toString(),
                ),
            )
            putStringArray(ContentResolver.QUERY_ARG_SORT_COLUMNS, arrayOf(MediaStore.MediaColumns.DATE_ADDED))
            putInt(ContentResolver.QUERY_ARG_SORT_DIRECTION, ContentResolver.QUERY_SORT_DIRECTION_DESCENDING)
            putInt(ContentResolver.QUERY_ARG_LIMIT, 150)
        }
        val out = ArrayList<SendItem>()
        resolver.query(files, projection, args, null)?.use { c ->
            val idCol = c.getColumnIndexOrThrow(MediaStore.MediaColumns._ID)
            val typeCol = c.getColumnIndexOrThrow(type)
            val nameCol = c.getColumnIndexOrThrow(MediaStore.MediaColumns.DISPLAY_NAME)
            val mimeCol = c.getColumnIndexOrThrow(MediaStore.MediaColumns.MIME_TYPE)
            val sizeCol = c.getColumnIndexOrThrow(MediaStore.MediaColumns.SIZE)
            while (c.moveToNext()) {
                val id = c.getLong(idCol)
                val isVideo = c.getInt(typeCol) == MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO
                val base = if (isVideo) MediaStore.Video.Media.EXTERNAL_CONTENT_URI else MediaStore.Images.Media.EXTERNAL_CONTENT_URI
                out += SendItem(
                    uri = ContentUris.withAppendedId(base, id),
                    name = c.getString(nameCol) ?: "item-$id",
                    mime = c.getString(mimeCol) ?: if (isVideo) "video/mp4" else "image/jpeg",
                    size = if (c.isNull(sizeCol)) -1 else c.getLong(sizeCol),
                )
            }
        }
        return out
    }

    private fun describe(uri: Uri): SendItem? = runCatching {
        var name: String? = null
        var size = -1L
        resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { c ->
            if (c.moveToFirst()) {
                val n = c.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                val s = c.getColumnIndex(OpenableColumns.SIZE)
                if (n >= 0 && !c.isNull(n)) name = c.getString(n)
                if (s >= 0 && !c.isNull(s)) size = c.getLong(s)
            }
        }
        SendItem(uri, name ?: uri.lastPathSegment ?: "file", resolver.getType(uri) ?: "application/octet-stream", size)
    }.getOrNull()
}
