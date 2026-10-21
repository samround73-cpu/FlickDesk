package dev.flick.app.ui

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideIn
import androidx.compose.animation.slideOut
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.Velocity
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import dev.flick.app.FlickViewModel
import dev.flick.app.PairedPc
import dev.flick.app.PairingState
import dev.flick.app.PcState
import dev.flick.app.ReceivedFile
import dev.flick.app.SendItem
import dev.flick.app.Side
import dev.flick.app.core.CoreEvent
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

private enum class Screen { HOME, SETTINGS, SCAN }

private fun mediaPermissions(): Array<String> = when {
    Build.VERSION.SDK_INT >= 34 -> arrayOf(
        Manifest.permission.READ_MEDIA_IMAGES,
        Manifest.permission.READ_MEDIA_VIDEO,
        Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED,
    )
    Build.VERSION.SDK_INT >= 33 -> arrayOf(Manifest.permission.READ_MEDIA_IMAGES, Manifest.permission.READ_MEDIA_VIDEO)
    else -> arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE)
}

@Composable
fun FlickApp(vm: FlickViewModel) {
    val core = vm.core
    val context = LocalContext.current
    val density = LocalDensity.current
    val scope = rememberCoroutineScope()

    val settings by core.settings.collectAsStateWithLifecycle()
    val pcs by core.pcs.collectAsStateWithLifecycle()
    val pcStates by core.pcStates.collectAsStateWithLifecycle()
    val nearby by core.nearby.collectAsStateWithLifecycle()
    val history by core.history.collectAsStateWithLifecycle()
    val sending by core.sending.collectAsStateWithLifecycle()
    val lastClip by core.lastClip.collectAsStateWithLifecycle()
    val pairing by core.pairing.collectAsStateWithLifecycle()
    val media by vm.media.collectAsStateWithLifecycle()
    val mediaDenied by vm.mediaDenied.collectAsStateWithLifecycle()
    val files by vm.files.collectAsStateWithLifecycle()
    val selection by vm.selection.collectAsStateWithLifecycle()
    val tab by vm.tab.collectAsStateWithLifecycle()
    val clipDraft by vm.clipDraft.collectAsStateWithLifecycle()

    var screen by rememberSaveable { mutableStateOf(Screen.HOME) }
    var pcOptions by remember { mutableStateOf<String?>(null) }
    val drag = remember { FlickDragState() }
    var glowTrigger by remember { mutableIntStateOf(0) }
    var glowSide by remember { mutableStateOf(Side.LEFT) }
    var arrived by remember { mutableStateOf<ReceivedFile?>(null) }
    val snackbar = remember { SnackbarHostState() }

    BackHandler(enabled = screen != Screen.HOME) {
        if (screen == Screen.SCAN) core.cancelPairing()
        screen = Screen.HOME
    }

    // ---- permissions
    val permissions = remember { mediaPermissions() }
    val askMedia = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { result ->
        if (result.values.any { it }) vm.loadMedia() else vm.setMediaDenied()
    }
    val askNotifications = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { }
    val pickFiles = rememberLauncherForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris ->
        if (uris.isNotEmpty()) vm.addFiles(uris)
    }
    LaunchedEffect(Unit) {
        val granted = permissions.any { ContextCompat.checkSelfPermission(context, it) == PackageManager.PERMISSION_GRANTED }
        if (granted) vm.loadMedia() else askMedia.launch(permissions)
    }
    LaunchedEffect(pcs.isNotEmpty()) {
        // Ask for notifications once there's a PC that could send something.
        if (pcs.isNotEmpty() && Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) askNotifications.launch(Manifest.permission.POST_NOTIFICATIONS)
    }

    // ---- one-off events
    LaunchedEffect(Unit) {
        vm.events.collect { e ->
            when (e) {
                is CoreEvent.Glow -> { glowSide = e.side; glowTrigger++ }
                is CoreEvent.Arrived -> arrived = e.file
                is CoreEvent.Message -> launch { snackbar.showSnackbar(e.text) }
            }
        }
    }
    LaunchedEffect(Unit) { vm.messages.collect { launch { snackbar.showSnackbar(it) } } }

    // ---- what happens when a lifted item is let go
    val threshold = with(density) { (450f + 850f * settings.sensitivity).dp.toPx() }
    val edgeMargin = with(density) { 56.dp.toPx() }
    val onRelease: (SendItem, Velocity, Offset) -> FlickTarget? = { item, v, center ->
        val sides = pcs.map { it.side }
        val flicked = sides.firstOrNull { isFlickToward(it, v, threshold) }
        val dropped = if (flicked == null) sides.firstOrNull { isAtEdge(it, center, drag.rootSize, edgeMargin) } else null
        val side = flicked ?: dropped
        when {
            side != null -> {
                val pos = if (flicked != null) edgePosFor(side, v) else 0.5f
                if (vm.flick(vm.itemsFor(item), side, pos)) {
                    FlickTarget(side, pos, if (flicked != null) Offset(v.x, v.y) else side.toward)
                } else null
            }
            Side.entries.any { isFlickToward(it, v, threshold) } -> {
                val dir = Side.entries.first { isFlickToward(it, v, threshold) }
                vm.toast(if (pcs.isEmpty()) "Pair a PC first – tap + at the top" else "No PC on the ${dir.label.lowercase()} – tap a PC to change its side")
                null
            }
            else -> null
        }
    }

    Box(
        Modifier
            .fillMaxSize()
            .background(Color.Black)
            .onSizeChanged { drag.rootSize = it },
    ) {
        when (screen) {
            Screen.HOME -> HomeScreen(
                s = HomeState(pcs, pcStates, nearby, tab, media, mediaDenied, files, selection, sending, history, lastClip, clipDraft),
                a = HomeActions(
                    openSettings = { screen = Screen.SETTINGS },
                    openScanner = { core.cancelPairing(); screen = Screen.SCAN },
                    pairNearby = { n -> core.pairManual(n.address, n.port, n.name); screen = Screen.SCAN },
                    openPc = { pc -> pcOptions = pc.id },
                    setTab = vm::setTab,
                    askMediaPermission = { askMedia.launch(permissions) },
                    pickFiles = { pickFiles.launch(arrayOf("*/*")) },
                    removeFile = vm::removeFile,
                    toggleSelect = vm::toggleSelect,
                    clearSelection = vm::clearSelection,
                    sendSelectionTo = vm::sendSelectionTo,
                    onRelease = onRelease,
                    setClipDraft = vm::setClipDraft,
                    sendClip = vm::sendClip,
                    toast = vm::toast,
                ),
                drag = drag,
            )
            Screen.SETTINGS -> SettingsScreen(
                settings = settings,
                pcs = pcs,
                pcStates = pcStates,
                deviceName = core.deviceName,
                onChange = core::updateSettings,
                onOpenPc = { pc -> pcOptions = pc.id },
                onBack = { screen = Screen.HOME },
            )
            Screen.SCAN -> ScanScreen(
                pairing = pairing,
                onCode = core::pairFromQr,
                onManual = { host ->
                    val h = host.substringBefore(':')
                    val port = host.substringAfter(':', "").toIntOrNull() ?: dev.flick.app.net.Protocol.PC_PORT
                    core.pairManual(h, port)
                },
                onClose = { core.cancelPairing(); screen = Screen.HOME },
            )
        }

        FloatingItem(drag)
        EdgeEffect(trigger = glowTrigger, side = glowSide, preset = settings.effect, palette = settings.palette)

        ArrivalCard(
            file = arrived,
            onOpen = { f -> openUri(context, f.uri, f.mime); arrived = null },
            onShare = { f ->
                val share = Intent(Intent.ACTION_SEND).setType(f.mime).putExtra(Intent.EXTRA_STREAM, f.uri)
                    .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                context.startActivity(Intent.createChooser(share, f.name).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                arrived = null
            },
            onDismiss = { arrived = null },
            modifier = Modifier.align(Alignment.Center),
        )

        SnackbarHost(snackbar, Modifier.align(Alignment.BottomCenter).navigationBarsPadding().padding(bottom = 90.dp))

        PairSheet(
            state = pairing,
            onConfirm = core::confirmPairing,
            onCancel = core::cancelPairing,
            onDone = { core.cancelPairing(); screen = Screen.HOME },
        )
    }

    pcOptions?.let { id ->
        val pc: PairedPc? = pcs.firstOrNull { it.id == id }
        if (pc == null) {
            pcOptions = null
        } else {
            PcOptionsDialog(
                pc = pc,
                state = pcStates[pc.id] ?: PcState.UNKNOWN,
                onSide = { side -> core.setSide(pc, side) },
                onForget = { core.forget(pc); pcOptions = null },
                onDismiss = { pcOptions = null },
            )
        }
    }
}

/** "Arrived from PC" card that sweeps in from the PC's side with Open / Share. */
@Composable
private fun ArrivalCard(
    file: ReceivedFile?,
    onOpen: (ReceivedFile) -> Unit,
    onShare: (ReceivedFile) -> Unit,
    onDismiss: () -> Unit,
    modifier: Modifier,
) {
    val last = remember { mutableStateOf<ReceivedFile?>(null) }
    if (file != null) last.value = file

    LaunchedEffect(file) {
        if (file != null) {
            delay(7_000)
            onDismiss()
        }
    }

    val side = last.value?.side ?: Side.LEFT
    AnimatedVisibility(
        visible = file != null,
        modifier = modifier,
        enter = slideIn(initialOffset = { full ->
            when (side) {
                Side.LEFT -> IntOffset(-full.width * 2, 0)
                Side.RIGHT -> IntOffset(full.width * 2, 0)
                Side.ABOVE -> IntOffset(0, -full.height * 4)
            }
        }) + fadeIn(),
        exit = fadeOut() + slideOut(targetOffset = { IntOffset(0, it.height / 3) }),
    ) {
        val shown = last.value ?: return@AnimatedVisibility
        val thumb = if (shown.mime.startsWith("image/") || shown.mime.startsWith("video/")) rememberThumbnail(shown.uri, 240) else null
        Column(
            Modifier
                .padding(horizontal = 14.dp)
                .fillMaxWidth()
                .clip(RoundedCornerShape(24.dp))
                .background(Color(0xEB16181F))
                .border(1.dp, Color(0x22FFFFFF), RoundedCornerShape(24.dp))
                .padding(14.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(
                    Modifier
                        .size(width = 58.dp, height = 72.dp)
                        .clip(RoundedCornerShape(10.dp))
                        .background(Color(0xFF242833)),
                    contentAlignment = Alignment.Center,
                ) {
                    if (thumb != null) {
                        Image(thumb, contentDescription = null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
                    } else {
                        Text(shown.name.substringAfterLast('.', "FILE").take(4).uppercase(), color = FlickBlue, fontWeight = FontWeight.Bold, fontSize = 12.sp)
                    }
                }
                Column(Modifier.weight(1f).padding(start = 12.dp)) {
                    Text(shown.name, color = Color.White, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text("From ${shown.fromPc}" + if (shown.size > 0) " · ${sizeLabel(shown.size)}" else "", color = Muted, fontSize = 12.5.sp)
                    Text(
                        when {
                            shown.mime.startsWith("image/") -> "Saved to Pictures › Flick"
                            shown.mime.startsWith("video/") -> "Saved to Movies › Flick"
                            else -> "Saved to Download › Flick"
                        },
                        color = Muted,
                        fontSize = 12.5.sp,
                    )
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(top = 14.dp)) {
                Button(
                    onClick = { onOpen(shown) },
                    colors = ButtonDefaults.buttonColors(containerColor = FlickBlue, contentColor = Color(0xFF06121F)),
                    shape = RoundedCornerShape(12.dp),
                    modifier = Modifier.weight(1f).height(44.dp),
                ) { Text("Open", fontWeight = FontWeight.SemiBold) }
                OutlinedButton(onClick = { onShare(shown) }, shape = RoundedCornerShape(12.dp), modifier = Modifier.weight(1f).height(44.dp)) {
                    Text("Share", color = Color.White)
                }
                OutlinedButton(onClick = onDismiss, shape = RoundedCornerShape(12.dp), modifier = Modifier.weight(1f).height(44.dp)) {
                    Text("Done", color = Color.White)
                }
            }
        }
    }
}
