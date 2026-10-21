package dev.flick.app

import android.app.Application
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.core.content.IntentCompat
import dev.flick.app.core.FlickCore
import dev.flick.app.ui.FlickApp
import dev.flick.app.ui.FlickTheme

class FlickApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        FlickCore.init(this)
    }
}

class MainActivity : ComponentActivity() {

    private val vm: FlickViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        if (savedInstanceState == null) handleIntent(intent)
        setContent {
            FlickTheme {
                FlickApp(vm)
            }
        }
    }

    override fun onStart() {
        super.onStart()
        // Foreground services may only be started while we're visible.
        FlickCore.ensureService()
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleIntent(intent)
    }

    /** "Share → Flick" from other apps, and taps on our own notifications. */
    private fun handleIntent(intent: Intent?) {
        intent ?: return
        intent.getStringExtra(EXTRA_TAB)?.let { tab ->
            Tab.entries.firstOrNull { it.name.equals(tab, ignoreCase = true) }?.let(vm::setTab)
        }
        when (intent.action) {
            Intent.ACTION_SEND -> {
                val stream = IntentCompat.getParcelableExtra(intent, Intent.EXTRA_STREAM, Uri::class.java)
                val text = intent.getStringExtra(Intent.EXTRA_TEXT)
                when {
                    stream != null -> vm.addFiles(listOf(stream))
                    !text.isNullOrBlank() -> vm.receiveSharedText(text)
                }
            }
            Intent.ACTION_SEND_MULTIPLE -> {
                val uris = IntentCompat.getParcelableArrayListExtra(intent, Intent.EXTRA_STREAM, Uri::class.java).orEmpty()
                if (uris.isNotEmpty()) vm.addFiles(uris)
            }
        }
    }

    companion object {
        const val EXTRA_TAB = "dev.flick.app.TAB"
    }
}
