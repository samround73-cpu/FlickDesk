package dev.flick.app.core

import android.app.Application
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.content.ContextCompat
import androidx.lifecycle.DefaultLifecycleObserver
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.ProcessLifecycleOwner
import dev.flick.app.FlickSettings
import dev.flick.app.HistoryItem
import dev.flick.app.NearbyPc
import dev.flick.app.PairedPc
import dev.flick.app.PairingState
import dev.flick.app.PcState
import dev.flick.app.ReceivedFile
import dev.flick.app.SendItem
import dev.flick.app.Side
import dev.flick.app.net.Discovery
import dev.flick.app.net.FlickClient
import dev.flick.app.net.FlickServer
import dev.flick.app.net.PairHandshake
import dev.flick.app.net.Protocol
import dev.flick.app.net.RemoteException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.onEach
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.net.UnknownHostException

/** Things the UI reacts to once (glow, arrival card, toast). */
sealed interface CoreEvent {
    data class Glow(val side: Side) : CoreEvent
    data class Arrived(val file: ReceivedFile) : CoreEvent
    data class Message(val text: String) : CoreEvent
}

/**
 * The app's long-lived brain, independent of any screen: paired PCs, the receiver, keep-alive,
 * pairing, sending, history and settings. Lives as long as the process — which the background
 * receive service keeps alive when the user wants files to arrive with Flick closed.
 */
object FlickCore {
    private lateinit var app: Application
    private lateinit var store: Store
    private lateinit var client: FlickClient
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    lateinit var deviceName: String
        private set

    private val _settings = MutableStateFlow(FlickSettings())
    val settings: StateFlow<FlickSettings> = _settings.asStateFlow()

    private val _pcs = MutableStateFlow<List<PairedPc>>(emptyList())
    val pcs: StateFlow<List<PairedPc>> = _pcs.asStateFlow()

    private val _pcStates = MutableStateFlow<Map<String, PcState>>(emptyMap())
    val pcStates: StateFlow<Map<String, PcState>> = _pcStates.asStateFlow()

    private val _history = MutableStateFlow<List<HistoryItem>>(emptyList())
    val history: StateFlow<List<HistoryItem>> = _history.asStateFlow()

    private val _sending = MutableStateFlow<Map<String, Float>>(emptyMap())
    val sending: StateFlow<Map<String, Float>> = _sending.asStateFlow()

    /** Last text a PC sent: (pc name, text). */
    private val _lastClip = MutableStateFlow<Pair<String, String>?>(null)
    val lastClip: StateFlow<Pair<String, String>?> = _lastClip.asStateFlow()

    private val _pairing = MutableStateFlow<PairingState>(PairingState.Idle)
    val pairing: StateFlow<PairingState> = _pairing.asStateFlow()

    private val _events = MutableSharedFlow<CoreEvent>(extraBufferCapacity = 32)
    val events: SharedFlow<CoreEvent> = _events.asSharedFlow()

    /** PCs announcing themselves on the network (only scanned while someone is watching). */
    lateinit var nearby: StateFlow<List<NearbyPc>>
        private set

    @Volatile var inForeground = false
        private set

    private var pairJob: Job? = null
    private var handshake: PairHandshake? = null
    private var server: FlickServer? = null

    fun init(application: Application) {
        if (::app.isInitialized) return
        app = application
        store = Store(app)
        deviceName = Settings.Global.getString(app.contentResolver, Settings.Global.DEVICE_NAME)
            ?: "${Build.MANUFACTURER} ${Build.MODEL}"
        client = FlickClient(store.deviceId, deviceName)
        _settings.value = store.loadSettings()
        _pcs.value = store.loadPcs()
        _history.value = store.loadHistory()
        Notifications.createChannels(app)

        nearby = Discovery(app).pcs()
            .catch { emit(emptyList()) }
            .onEach { onNearby(it) }
            .stateIn(scope, SharingStarted.WhileSubscribed(5_000), emptyList())

        ProcessLifecycleOwner.get().lifecycle.addObserver(object : DefaultLifecycleObserver {
            override fun onStart(owner: LifecycleOwner) { inForeground = true; pingAll() }
            override fun onStop(owner: LifecycleOwner) { inForeground = false }
        })

        server = FlickServer(app, { id -> _pcs.value.firstOrNull { it.id == id } }, ServerListener).also { it.start(scope) }

        // Keep-alive: lets each PC know where to reach this phone, and keeps "online" dots honest.
        scope.launch {
            while (isActive) {
                pingAll()
                delay(if (inForeground) 15_000 else 45_000)
            }
        }
    }

    // ------------------------------------------------------------------ PCs

    fun pingAll() {
        for (pc in _pcs.value) {
            scope.launch(Dispatchers.IO) {
                val state = try {
                    val reply = client.hello(pc)
                    if (reply.optBoolean("paused")) PcState.PAUSED else PcState.ONLINE
                } catch (e: RemoteException) {
                    if (e.code == "not-paired") PcState.OFFLINE else PcState.ONLINE
                } catch (e: Exception) {
                    PcState.OFFLINE
                }
                _pcStates.update { it + (pc.id to state) }
            }
        }
    }

    private fun onNearby(list: List<NearbyPc>) {
        // A paired PC changed address (DHCP) – follow it.
        var changed = false
        val updated = _pcs.value.map { pc ->
            val seen = list.firstOrNull { it.id == pc.id }
            if (seen != null && (seen.address != pc.host || seen.port != pc.port)) {
                changed = true
                pc.copy(host = seen.address, port = seen.port)
            } else pc
        }
        if (changed) {
            _pcs.value = updated
            store.savePcs(updated)
            pingAll()
        }
        list.forEach { n ->
            if (_pcs.value.any { it.id == n.id }) {
                _pcStates.update { it + (n.id to if (n.paused) PcState.PAUSED else PcState.ONLINE) }
            }
        }
    }

    fun pcFor(side: Side): PairedPc? = _pcs.value.firstOrNull { it.side == side }

    /** Assigns a direction; if another PC had it, they swap. */
    fun setSide(pc: PairedPc, side: Side) {
        val updated = _pcs.value.map {
            when {
                it.id == pc.id -> it.copy(side = side)
                it.side == side -> it.copy(side = pc.side)
                else -> it
            }
        }
        _pcs.value = updated
        store.savePcs(updated)
    }

    fun forget(pc: PairedPc) {
        val updated = _pcs.value.filterNot { it.id == pc.id }
        _pcs.value = updated
        store.savePcs(updated)
        _pcStates.update { it - pc.id }
        scope.launch(Dispatchers.IO) { runCatching { client.unpair(pc) } }
        ensureService()
    }

    // ------------------------------------------------------------------ pairing

    /** Returns false if [raw] isn't a Flick pairing code. */
    fun pairFromQr(raw: String): Boolean {
        val uri = runCatching { Uri.parse(raw.trim()) }.getOrNull() ?: return false
        if (uri.scheme != "flick" || uri.host != "pair") return false
        val hosts = uri.getQueryParameter("h")?.split(',')?.map { it.trim() }?.filter { it.isNotEmpty() }.orEmpty()
        val token = FlickCrypto.fromB64Url(uri.getQueryParameter("t"))
        if (hosts.isEmpty() || token.size != 16) return false
        val port = uri.getQueryParameter("p")?.toIntOrNull() ?: Protocol.PC_PORT
        startPairing(hosts, port, token, uri.getQueryParameter("n") ?: "PC")
        return true
    }

    fun pairManual(host: String, port: Int = Protocol.PC_PORT, name: String = host) =
        startPairing(listOf(host), port, null, name)

    private fun startPairing(hosts: List<String>, port: Int, token: ByteArray?, name: String) {
        pairJob?.cancel()
        handshake = null
        _pairing.value = PairingState.Connecting(name)
        pairJob = scope.launch(Dispatchers.IO) {
            try {
                val h = client.pair(hosts, port, token, name)
                handshake = h
                _pairing.value = PairingState.Confirm(h.pcName, h.checkNumber, h.needsPcApproval)
            } catch (e: Exception) {
                _pairing.value = PairingState.Failed(friendly(e, "Couldn't reach $name"))
            }
        }
    }

    fun confirmPairing() {
        val h = handshake ?: return
        _pairing.value = if (h.needsPcApproval) PairingState.WaitingForPc(h.pcName, h.checkNumber) else PairingState.Connecting(h.pcName)
        pairJob = scope.launch(Dispatchers.IO) {
            try {
                client.confirm(h)
                val existing = _pcs.value.firstOrNull { it.id == h.pcId }
                val pc = PairedPc(h.pcId, h.pcName, h.host, h.port, FlickCrypto.b64(h.key), existing?.side ?: freeSide())
                val updated = _pcs.value.filterNot { it.id == pc.id } + pc
                _pcs.value = updated
                store.savePcs(updated)
                _pcStates.update { it + (pc.id to PcState.ONLINE) }
                handshake = null
                _pairing.value = PairingState.Done(pc.name)
                _events.tryEmit(CoreEvent.Glow(pc.side))
                withContext(Dispatchers.Main) { ensureService() }
            } catch (e: Exception) {
                _pairing.value = PairingState.Failed(friendly(e, "Pairing didn't finish"))
            }
        }
    }

    fun cancelPairing() {
        pairJob?.cancel()
        handshake = null
        _pairing.value = PairingState.Idle
    }

    private fun freeSide(): Side {
        val used = _pcs.value.map { it.side }.toSet()
        return listOf(Side.LEFT, Side.RIGHT, Side.ABOVE).firstOrNull { it !in used } ?: Side.LEFT
    }

    // ------------------------------------------------------------------ sending

    /** Sends [items] as one flick (a stack if more than one). Returns how many arrived. */
    suspend fun send(items: List<SendItem>, pc: PairedPc, edgePos: Float): Int = withContext(Dispatchers.IO) {
        var ok = 0
        items.forEach { item -> _sending.update { it + (item.key to 0f) } }
        for ((i, item) in items.withIndex()) {
            try {
                val big = item.mime == Transfers.FOLDER_MIME || item.size >= Transfers.PROGRESS_THRESHOLD
                val noteKey = "out:${item.key}"
                var lastNote = -1
                client.sendFile(
                    pc, app.contentResolver, app.cacheDir, item, edgePos, i, items.size,
                    onRetry = { attempt, of ->
                        _events.tryEmit(CoreEvent.Message("Connection dropped – resuming ${item.name} ($attempt/$of)…"))
                        if (big) Notifications.progress(app, noteKey, "Reconnecting to ${pc.name}…", item.name, -1f)
                    },
                ) { p ->
                    _sending.update { it + (item.key to p) }
                    val pct = (p * 100).toInt()
                    if (big && pct != lastNote) {
                        lastNote = pct
                        Notifications.progress(app, noteKey, "Sending ${item.name} to ${pc.name}", "$pct %", p)
                    }
                }
                if (big) Notifications.endProgress(app, noteKey)
                ok++
                addHistory(HistoryItem(item.name, item.uri.toString(), item.mime, false, pc.name, item.size, System.currentTimeMillis()))
            } catch (e: Exception) {
                Notifications.endProgress(app, "out:${item.key}")
                _events.tryEmit(CoreEvent.Message("Couldn't send ${item.name}: ${friendly(e, "network error")}"))
                if (e !is RemoteException) _pcStates.update { it + (pc.id to PcState.OFFLINE) }
                // the rest of the stack would fail the same way
                items.drop(i).forEach { rest -> _sending.update { it - rest.key } }
                return@withContext ok
            } finally {
                _sending.update { it - item.key }
            }
        }
        _pcStates.update { it + (pc.id to PcState.ONLINE) }
        ok
    }

    suspend fun sendClip(pc: PairedPc, text: String): Boolean = withContext(Dispatchers.IO) {
        try {
            client.sendClip(pc, text)
            addHistory(HistoryItem(text.take(120), null, "text/plain", false, pc.name, text.length.toLong(), System.currentTimeMillis(), clipboard = true))
            _events.tryEmit(CoreEvent.Message("Sent to ${pc.name}"))
            true
        } catch (e: Exception) {
            _events.tryEmit(CoreEvent.Message("Couldn't send: ${friendly(e, "network error")}"))
            false
        }
    }

    // ------------------------------------------------------------------ settings & service

    fun updateSettings(transform: (FlickSettings) -> FlickSettings) {
        val next = transform(_settings.value)
        _settings.value = next
        store.saveSettings(next)
        ensureService()
    }

    /** Runs the foreground receiver when wanted. Call while the app is in the foreground. */
    fun ensureService() {
        if (!::app.isInitialized) return
        val want = _settings.value.backgroundReceive && _pcs.value.isNotEmpty()
        val intent = Intent(app, ReceiveService::class.java)
        try {
            if (want) ContextCompat.startForegroundService(app, intent) else app.stopService(intent)
        } catch (e: Exception) {
            // Android refuses to start foreground services from the background; we'll retry next time the app opens.
        }
    }

    // ------------------------------------------------------------------ receiving

    private object ServerListener : FlickServer.Listener {
        override fun onPcSeen(pc: PairedPc, address: String) {
            if (address != pc.host) {
                val updated = _pcs.value.map { if (it.id == pc.id) it.copy(host = address) else it }
                _pcs.value = updated
                store.savePcs(updated)
            }
            _pcStates.update { it + (pc.id to PcState.ONLINE) }
        }

        override fun onFileStarting(pc: PairedPc, name: String, batchIndex: Int) {
            if (batchIndex == 0) {
                _events.tryEmit(CoreEvent.Glow(pc.side))
                Haptics.play(app, _settings.value.haptic)
            }
        }

        override fun onFileReceived(pc: PairedPc, uri: Uri, name: String, mime: String, size: Long, batchIndex: Int, batchCount: Int) {
            val file = ReceivedFile(uri, name, mime, pc.name, pc.side, size)
            addHistory(HistoryItem(name, uri.toString(), mime, true, pc.name, size, System.currentTimeMillis()))
            _events.tryEmit(CoreEvent.Arrived(file))
            if (!inForeground && batchIndex == batchCount - 1) Notifications.arrival(app, file, batchCount)
        }

        private val lastPct = java.util.concurrent.ConcurrentHashMap<String, Int>()

        override fun onFileProgress(pc: PairedPc, key: String, name: String, done: Long, total: Long) {
            if (done >= total) {
                lastPct.remove(key)
                Notifications.endProgress(app, key)
                return
            }
            val pct = (done * 100 / total).toInt()
            if (lastPct.put(key, pct) == pct) return
            Notifications.progress(app, key, "Receiving $name from ${pc.name}", "$pct % · ${sizeText(done)} of ${sizeText(total)}", done.toFloat() / total)
        }

        override fun onFileInterrupted(pc: PairedPc, key: String, name: String) {
            lastPct.remove(key)
            Notifications.endProgress(app, key, "Paused: $name", "It carries on when ${pc.name} reconnects")
        }

        private fun sizeText(bytes: Long): String = when {
            bytes >= 1L shl 30 -> "%.1f GB".format(bytes / (1L shl 30).toDouble())
            bytes >= 1L shl 20 -> "%.0f MB".format(bytes / (1L shl 20).toDouble())
            else -> "${bytes / 1024} KB"
        }

        override fun onClip(pc: PairedPc, text: String) {
            _lastClip.value = pc.name to text
            addHistory(HistoryItem(text.take(120), null, "text/plain", true, pc.name, text.length.toLong(), System.currentTimeMillis(), clipboard = true))
            scope.launch(Dispatchers.Main) {
                val copied = _settings.value.autoCopyFromPc && runCatching {
                    app.getSystemService(ClipboardManager::class.java)
                        .setPrimaryClip(ClipData.newPlainText("From ${pc.name}", text))
                }.isSuccess
                if (inForeground) _events.tryEmit(CoreEvent.Message(if (copied) "Copied from ${pc.name}" else "${pc.name} sent text – see Clipboard"))
                else Notifications.clip(app, pc.name, text, copied)
            }
        }

        override fun onError(message: String) {
            _events.tryEmit(CoreEvent.Message(message))
        }
    }

    private fun addHistory(item: HistoryItem) {
        val updated = (listOf(item) + _history.value).take(200)
        _history.value = updated
        scope.launch(Dispatchers.IO) { store.saveHistory(updated) }
    }

    private fun friendly(e: Exception, fallback: String): String = when (e) {
        is RemoteException -> e.message ?: fallback
        is UnknownHostException -> "Couldn't find that address"
        is java.net.ConnectException, is java.net.SocketTimeoutException, is java.net.NoRouteToHostException ->
            "$fallback – is FlickDesk running and on the same Wi-Fi?"
        else -> e.message ?: fallback
    }
}
