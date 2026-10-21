package dev.flick.app.ui

import android.content.Context
import android.graphics.Bitmap
import android.net.Uri
import android.util.LruCache
import android.util.Size
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Small in-memory thumbnail cache backed by ContentResolver.loadThumbnail (images AND videos). */
object Thumbnails {
    private val cache = object : LruCache<String, Bitmap>(32 * 1024 * 1024) {
        override fun sizeOf(key: String, value: Bitmap) = value.byteCount
    }

    suspend fun load(context: Context, uri: Uri, px: Int): Bitmap? {
        val key = "$uri@$px"
        cache.get(key)?.let { return it }
        return withContext(Dispatchers.IO) {
            runCatching { context.contentResolver.loadThumbnail(uri, Size(px, px), null) }
                .getOrNull()
                ?.also { cache.put(key, it) }
        }
    }
}

@Composable
fun rememberThumbnail(uri: Uri, px: Int = 320): ImageBitmap? {
    val context = LocalContext.current
    val bitmap by produceState<Bitmap?>(initialValue = null, uri, px) {
        value = Thumbnails.load(context, uri, px)
    }
    return remember(bitmap) { bitmap?.asImageBitmap() }
}
