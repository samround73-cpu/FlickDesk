package dev.flick.app.ui

import android.content.ActivityNotFoundException
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Velocity
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import dev.flick.app.HistoryItem
import dev.flick.app.NearbyPc
import dev.flick.app.PairedPc
import dev.flick.app.PcState
import dev.flick.app.SendItem
import dev.flick.app.Side
import dev.flick.app.Tab
import java.text.DateFormat
import java.util.Date

/** Everything the home screen shows, gathered by FlickApp. */
class HomeState(
    val pcs: List<PairedPc>,
    val pcStates: Map<String, PcState>,
    val nearby: List<NearbyPc>,
    val tab: Tab,
    val media: List<SendItem>,
    val mediaDenied: Boolean,
    val files: List<SendItem>,
    val selection: List<String>,
    val sending: Map<String, Float>,
    val history: List<HistoryItem>,
    val lastClip: Pair<String, String>?,
    val clipDraft: String,
)

class HomeActions(
    val openSettings: () -> Unit,
    val openScanner: () -> Unit,
    val pairNearby: (NearbyPc) -> Unit,
    val openPc: (PairedPc) -> Unit,
    val setTab: (Tab) -> Unit,
    val askMediaPermission: () -> Unit,
    val pickFiles: () -> Unit,
    val pickFolder: () -> Unit,
    val removeFile: (SendItem) -> Unit,
    val toggleSelect: (SendItem) -> Unit,
    val clearSelection: () -> Unit,
    val sendSelectionTo: (PairedPc) -> Unit,
    val onRelease: (SendItem, Velocity, Offset) -> FlickTarget?,
    val setClipDraft: (String) -> Unit,
    val sendClip: (PairedPc, String) -> Unit,
    val toast: (String) -> Unit,
)

@Composable
fun HomeScreen(s: HomeState, a: HomeActions, drag: FlickDragState) {
    Box(Modifier.fillMaxSize()) {
        Column(
            Modifier
                .fillMaxSize()
                .statusBarsPadding()
                .padding(horizontal = 14.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 10.dp)) {
                Text("Flick", color = Color.White, fontSize = 28.sp, fontWeight = FontWeight.SemiBold)
                Spacer(Modifier.weight(1f))
                IconButton(onClick = a.openSettings) {
                    Icon(Icons.Filled.Settings, contentDescription = "Look and feel settings", tint = Color(0xFFC9CED8))
                }
            }

            SectionLabel("Send to")
            DevicesRow(s, a)
            Text(
                hintFor(s.pcs),
                color = Muted,
                fontSize = 12.5.sp,
                modifier = Modifier.padding(top = 8.dp, bottom = 12.dp),
            )

            Tabs(s.tab, a.setTab)

            Box(Modifier.weight(1f).padding(top = 10.dp)) {
                when (s.tab) {
                    Tab.PHOTOS -> PhotosTab(s, a, drag)
                    Tab.FILES -> FilesTab(s, a, drag)
                    Tab.CLIPBOARD -> ClipboardTab(s, a)
                    Tab.HISTORY -> HistoryTab(s, a)
                }
            }
        }

        if (s.selection.isNotEmpty() && (s.tab == Tab.PHOTOS || s.tab == Tab.FILES)) {
            StackTray(s, a, Modifier.align(Alignment.BottomCenter))
        }
    }
}

private fun hintFor(pcs: List<PairedPc>): String = when {
    pcs.isEmpty() -> "Tap + and scan the QR code FlickDesk shows on your PC."
    pcs.size == 1 -> "Long-press, then flick ${pcs[0].side.arrow} to send to ${pcs[0].name}. Tap to build a stack."
    else -> pcs.sortedBy { it.side.ordinal }.joinToString("  ·  ") { "${it.side.arrow} ${it.name}" }
}

@Composable
fun SectionLabel(text: String, modifier: Modifier = Modifier) {
    Text(
        text.uppercase(),
        color = Faint,
        fontSize = 11.sp,
        fontWeight = FontWeight.SemiBold,
        letterSpacing = 1.sp,
        modifier = modifier.padding(top = 14.dp, bottom = 8.dp),
    )
}

// ---------------------------------------------------------------- devices

@Composable
private fun DevicesRow(s: HomeState, a: HomeActions) {
    val unpaired = s.nearby.filter { n -> s.pcs.none { it.id == n.id } }
    LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        items(s.pcs, key = { it.id }) { pc ->
            val state = s.pcStates[pc.id] ?: PcState.UNKNOWN
            val online = state == PcState.ONLINE
            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier
                    .clip(RoundedCornerShape(14.dp))
                    .border(1.dp, if (online) FlickBlue else Line, RoundedCornerShape(14.dp))
                    .background(if (online) Color(0xFF0F1824) else Surface)
                    .clickable(onClickLabel = "Options for ${pc.name}") { a.openPc(pc) }
                    .padding(horizontal = 12.dp, vertical = 10.dp),
            ) {
                MonitorGlyph(if (online) Color(0xFFCFE8FF) else Muted)
                Column(Modifier.padding(start = 10.dp)) {
                    Text(pc.name, color = Color.White, fontSize = 13.5.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Box(Modifier.size(6.dp).clip(CircleShape).background(stateColor(state)))
                        Text(
                            "  ${stateLabel(state)} · ${pc.side.label} ${pc.side.arrow}",
                            color = if (online) Color(0xFF9FD3FF) else Muted,
                            fontSize = 11.5.sp,
                        )
                    }
                }
            }
        }
        items(unpaired, key = { "nearby-" + it.id }) { n ->
            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier
                    .clip(RoundedCornerShape(14.dp))
                    .border(1.dp, Line, RoundedCornerShape(14.dp))
                    .clickable(onClickLabel = "Pair with ${n.name}") { a.pairNearby(n) }
                    .padding(horizontal = 12.dp, vertical = 10.dp),
            ) {
                MonitorGlyph(Muted)
                Column(Modifier.padding(start = 10.dp)) {
                    Text(n.name, color = Color.White, fontSize = 13.5.sp, maxLines = 1)
                    Text("Nearby · tap to pair", color = Muted, fontSize = 11.5.sp)
                }
            }
        }
        item {
            Box(
                contentAlignment = Alignment.Center,
                modifier = Modifier
                    .size(width = 52.dp, height = 56.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .border(BorderStroke(1.dp, Color(0xFF33373F)), RoundedCornerShape(14.dp))
                    .clickable(onClickLabel = "Pair a PC with a QR code") { a.openScanner() },
            ) {
                Icon(Icons.Filled.Add, contentDescription = "Pair a PC", tint = Muted)
            }
        }
    }
}

@Composable
fun MonitorGlyph(color: Color) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Box(Modifier.size(width = 22.dp, height = 15.dp).border(1.6.dp, color, RoundedCornerShape(3.dp)))
        Box(Modifier.padding(top = 2.dp).size(width = 10.dp, height = 1.6.dp).background(color))
    }
}

fun stateColor(s: PcState) = when (s) {
    PcState.ONLINE -> Good
    PcState.PAUSED -> FlickAmber
    PcState.OFFLINE -> Color(0xFF5B6270)
    PcState.UNKNOWN -> Color(0xFF5B6270)
}

fun stateLabel(s: PcState) = when (s) {
    PcState.ONLINE -> "Online"
    PcState.PAUSED -> "Paused"
    PcState.OFFLINE -> "Offline"
    PcState.UNKNOWN -> "Checking"
}

// ---------------------------------------------------------------- tabs

@Composable
private fun Tabs(current: Tab, onSelect: (Tab) -> Unit) {
    Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Tab.entries.forEach { t ->
            val on = t == current
            Box(
                contentAlignment = Alignment.Center,
                modifier = Modifier
                    .height(36.dp)
                    .clip(RoundedCornerShape(50))
                    .background(if (on) Color(0xFF1E222B) else Color.Transparent)
                    .clickable { onSelect(t) }
                    .semantics {
                        role = Role.Tab
                        selected = on
                    }
                    .padding(horizontal = 14.dp),
            ) {
                Text(t.label, color = if (on) Color.White else Muted, fontSize = 13.sp, fontWeight = if (on) FontWeight.Medium else FontWeight.Normal)
            }
        }
    }
}

@Composable
private fun PhotosTab(s: HomeState, a: HomeActions, drag: FlickDragState) {
    if (s.mediaDenied) {
        Column {
            Text(
                "Allow photo access to see your recent photos here – or use the Files tab, or Share → Flick from any app.",
                color = Muted,
                modifier = Modifier.padding(vertical = 20.dp),
            )
            TextButton(onClick = a.askMediaPermission) { Text("Allow photo access") }
        }
        return
    }
    ItemGrid(s.media, s, a, drag)
}

@Composable
private fun FilesTab(s: HomeState, a: HomeActions, drag: FlickDragState) {
    Column {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Button(
                onClick = a.pickFiles,
                colors = ButtonDefaults.buttonColors(containerColor = FlickBlue, contentColor = Color(0xFF06121F)),
                shape = RoundedCornerShape(12.dp),
            ) { Text("Pick files", fontWeight = FontWeight.SemiBold) }
            Spacer(Modifier.width(8.dp))
            OutlinedButton(
                onClick = a.pickFolder,
                shape = RoundedCornerShape(12.dp),
                border = BorderStroke(1.dp, Color(0xFF2A2E37)),
                colors = ButtonDefaults.outlinedButtonColors(contentColor = Color.White),
            ) { Text("Send a folder", fontWeight = FontWeight.SemiBold) }
        }
        Spacer(Modifier.height(6.dp))
        Text("…or Share → Flick from any app. Big files resume if the Wi-Fi drops.", color = Muted, fontSize = 12.5.sp)
        Spacer(Modifier.height(12.dp))
        if (s.files.isEmpty()) {
            Text("Files you pick or share land here, ready to flick.", color = Faint, fontSize = 13.sp)
        } else {
            ItemGrid(s.files, s, a, drag, removable = true)
        }
    }
}

@Composable
private fun ItemGrid(items: List<SendItem>, s: HomeState, a: HomeActions, drag: FlickDragState, removable: Boolean = false) {
    LazyVerticalGrid(
        columns = GridCells.Adaptive(minSize = 104.dp),
        horizontalArrangement = Arrangement.spacedBy(3.dp),
        verticalArrangement = Arrangement.spacedBy(3.dp),
        contentPadding = PaddingValues(bottom = 120.dp),
        modifier = Modifier.fillMaxSize(),
    ) {
        items(items, key = { it.key }) { item ->
            Box {
                FlickTile(
                    item = item,
                    drag = drag,
                    selectedIndex = s.selection.indexOf(item.key),
                    stackSize = s.selection.size,
                    progress = s.sending[item.key],
                    onTap = a.toggleSelect,
                    onRelease = a.onRelease,
                )
                if (removable) {
                    Box(
                        contentAlignment = Alignment.Center,
                        modifier = Modifier
                            .align(Alignment.TopStart)
                            .padding(4.dp)
                            .size(24.dp)
                            .clip(CircleShape)
                            .background(Color(0xCC000000))
                            .clickable(onClickLabel = "Remove ${item.name}") { a.removeFile(item) },
                    ) {
                        Icon(Icons.Filled.Close, contentDescription = "Remove", tint = Color.White, modifier = Modifier.size(14.dp))
                    }
                }
            }
        }
    }
}

// ---------------------------------------------------------------- clipboard

@Composable
private fun ClipboardTab(s: HomeState, a: HomeActions) {
    val context = LocalContext.current
    LazyColumn(verticalArrangement = Arrangement.spacedBy(12.dp), contentPadding = PaddingValues(bottom = 40.dp)) {
        item {
            s.lastClip?.let { (pcName, text) ->
                Column(
                    Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(16.dp))
                        .background(Surface)
                        .border(1.dp, Line, RoundedCornerShape(16.dp))
                        .padding(14.dp),
                ) {
                    Text("FROM ${pcName.uppercase()}", color = Faint, fontSize = 11.sp, fontWeight = FontWeight.SemiBold)
                    Text(
                        text,
                        color = Color(0xFFDCE7F5),
                        fontFamily = FontFamily.Monospace,
                        fontSize = 13.sp,
                        maxLines = 6,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.padding(vertical = 10.dp),
                    )
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Button(
                            onClick = { copyText(context, text); a.toast("Copied") },
                            colors = ButtonDefaults.buttonColors(containerColor = FlickBlue, contentColor = Color(0xFF06121F)),
                            shape = RoundedCornerShape(10.dp),
                        ) { Text("Copy", fontWeight = FontWeight.SemiBold) }
                        val url = text.trim().takeIf { it.startsWith("http://") || it.startsWith("https://") }
                        if (url != null) {
                            OutlinedButton(onClick = { openUri(context, Uri.parse(url), null) }, shape = RoundedCornerShape(10.dp)) {
                                Text("Open link", color = Color.White)
                            }
                        }
                    }
                }
            }
        }
        item {
            Column(
                Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(16.dp))
                    .background(Surface)
                    .border(1.dp, Line, RoundedCornerShape(16.dp))
                    .padding(14.dp),
            ) {
                Text("SEND TEXT TO YOUR PC", color = Faint, fontSize = 11.sp, fontWeight = FontWeight.SemiBold)
                OutlinedTextField(
                    value = s.clipDraft,
                    onValueChange = a.setClipDraft,
                    placeholder = { Text("Paste or type a link, address, code…") },
                    minLines = 3,
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(vertical = 10.dp),
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                    OutlinedButton(onClick = {
                        val text = context.getSystemService(ClipboardManager::class.java)
                            ?.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(context)?.toString()
                        if (text.isNullOrBlank()) a.toast("Your clipboard is empty") else a.setClipDraft(text)
                    }, shape = RoundedCornerShape(10.dp)) { Text("Paste", color = Color.White) }
                    Spacer(Modifier.weight(1f))
                    if (s.pcs.isEmpty()) Text("Pair a PC first", color = Muted, fontSize = 12.5.sp)
                }
                s.pcs.forEach { pc ->
                    Button(
                        onClick = { a.sendClip(pc, s.clipDraft) },
                        enabled = s.clipDraft.isNotBlank(),
                        colors = ButtonDefaults.buttonColors(containerColor = FlickBlue, contentColor = Color(0xFF06121F)),
                        shape = RoundedCornerShape(10.dp),
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(top = 8.dp),
                    ) { Text("Send to ${pc.name} ${pc.side.arrow}", fontWeight = FontWeight.SemiBold) }
                }
            }
        }
        item {
            Text(
                "Text from your PC (FlickDesk → Send clipboard) is copied here automatically. Change that in Look & feel.",
                color = Faint,
                fontSize = 12.sp,
            )
        }
    }
}

// ---------------------------------------------------------------- history

@Composable
private fun HistoryTab(s: HomeState, a: HomeActions) {
    val context = LocalContext.current
    if (s.history.isEmpty()) {
        Text("Nothing yet. Everything you flick – both ways – shows up here.", color = Muted, modifier = Modifier.padding(top = 12.dp))
        return
    }
    val fmt = remember { DateFormat.getTimeInstance(DateFormat.SHORT) }
    LazyColumn(contentPadding = PaddingValues(bottom = 40.dp)) {
        items(s.history, key = { "${it.time}-${it.name}-${it.incoming}" }) { h ->
            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(12.dp))
                    .clickable {
                        when {
                            h.clipboard -> { copyText(context, h.name); a.toast("Copied") }
                            h.uri != null -> openUri(context, Uri.parse(h.uri), h.mime)
                        }
                    }
                    .padding(vertical = 8.dp, horizontal = 4.dp),
            ) {
                Box(
                    Modifier
                        .size(44.dp)
                        .clip(RoundedCornerShape(9.dp))
                        .background(Color(0xFF1E222B)),
                    contentAlignment = Alignment.Center,
                ) {
                    val thumb = if (h.uri != null && (h.mime.startsWith("image/") || h.mime.startsWith("video/")))
                        rememberThumbnail(Uri.parse(h.uri), 160) else null
                    if (thumb != null) {
                        Image(thumb, contentDescription = null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
                    } else {
                        Text(if (h.clipboard) "Aa" else h.name.substringAfterLast('.', "").take(4).uppercase(), color = FlickBlue, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                    }
                }
                Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                    Text(h.name, color = Color.White, fontSize = 13.5.sp, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(
                        "${if (h.incoming) "From" else "To"} ${h.pcName} · ${fmt.format(Date(h.time))}" + if (h.size > 0 && !h.clipboard) " · ${sizeLabel(h.size)}" else "",
                        color = Muted,
                        fontSize = 12.sp,
                        maxLines = 1,
                    )
                }
                Box(
                    contentAlignment = Alignment.Center,
                    modifier = Modifier
                        .size(28.dp)
                        .clip(CircleShape)
                        .background(if (h.incoming) Color(0xFF10243A) else Color(0xFF2A2233))
                        .semantics { },
                ) {
                    Text(if (h.incoming) "←" else "→", color = if (h.incoming) Color(0xFF9FD3FF) else Color(0xFFE3B8FF), fontSize = 14.sp)
                }
            }
        }
    }
}

fun sizeLabel(bytes: Long): String = when {
    bytes < 1024 -> "$bytes B"
    bytes < 1024 * 1024 -> "${bytes / 1024} KB"
    bytes < 1024L * 1024 * 1024 -> String.format("%.1f MB", bytes / 1048576.0)
    else -> String.format("%.2f GB", bytes / 1073741824.0)
}

fun copyText(context: Context, text: String) {
    context.getSystemService(ClipboardManager::class.java)?.setPrimaryClip(ClipData.newPlainText("Flick", text))
}

fun openUri(context: Context, uri: Uri, mime: String?) {
    val intent = Intent(Intent.ACTION_VIEW).apply {
        if (mime != null) setDataAndType(uri, mime) else data = uri
        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
    }
    try {
        context.startActivity(intent)
    } catch (_: ActivityNotFoundException) {
    } catch (_: SecurityException) {
    }
}

// ---------------------------------------------------------------- stack tray

@Composable
private fun StackTray(s: HomeState, a: HomeActions, modifier: Modifier) {
    val all = remember(s.media, s.files) { (s.files + s.media).associateBy { it.key } }
    val stack = s.selection.mapNotNull { all[it] }
    var menu by remember { mutableStateOf(false) }

    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = modifier
            .navigationBarsPadding()
            .padding(12.dp)
            .fillMaxWidth()
            .heightIn(min = 80.dp)
            .clip(RoundedCornerShape(22.dp))
            .background(Color(0xF0141619))
            .border(1.dp, Color(0xFF2A2E37), RoundedCornerShape(22.dp))
            .padding(horizontal = 14.dp, vertical = 12.dp),
    ) {
        Box(Modifier.size(width = 64.dp, height = 56.dp)) {
            stack.take(3).forEachIndexed { i, item ->
                val thumb = rememberThumbnail(item.uri, 160)
                Box(
                    Modifier
                        .offset(x = (6 + i * 7).dp, y = 5.dp)
                        .size(44.dp)
                        .graphicsLayer {
                            rotationZ = listOf(-10f, 4f, 12f)[i]
                            shadowElevation = 10f
                            shape = RoundedCornerShape(8.dp)
                            clip = true
                        }
                        .background(Color(0xFF2A2E37)),
                ) {
                    if (thumb != null) Image(thumb, contentDescription = null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
                }
            }
        }
        Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
            Text("${stack.size} in the stack", color = Color.White, fontSize = 14.5.sp, fontWeight = FontWeight.SemiBold)
            Text("Long-press any of them and flick once", color = Muted, fontSize = 12.sp)
        }
        IconButton(onClick = a.clearSelection) { Icon(Icons.Filled.Close, contentDescription = "Clear the stack", tint = Muted) }
        Box {
            Button(
                onClick = {
                    when (s.pcs.size) {
                        0 -> a.toast("Pair a PC first – tap + at the top")
                        1 -> a.sendSelectionTo(s.pcs[0])
                        else -> menu = true
                    }
                },
                colors = ButtonDefaults.buttonColors(containerColor = FlickBlue, contentColor = Color(0xFF06121F)),
                shape = RoundedCornerShape(12.dp),
            ) {
                Text(if (s.pcs.size == 1) "${s.pcs[0].side.arrow} Send" else "Send", fontWeight = FontWeight.SemiBold)
            }
            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                s.pcs.forEach { pc ->
                    DropdownMenuItem(text = { Text("${pc.side.arrow}  ${pc.name}") }, onClick = { menu = false; a.sendSelectionTo(pc) })
                }
            }
        }
    }
}

/** Side picker + forget, for a paired PC. */
@Composable
fun PcOptionsDialog(pc: PairedPc, state: PcState, onSide: (Side) -> Unit, onForget: () -> Unit, onDismiss: () -> Unit) {
    androidx.compose.material3.AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = { TextButton(onClick = onDismiss) { Text("Done") } },
        dismissButton = { TextButton(onClick = onForget) { Text("Forget this PC", color = Color(0xFFFF8A80)) } },
        title = { Text(pc.name) },
        text = {
            Column {
                Text("${stateLabel(state)} · ${pc.host}", color = Muted, fontSize = 13.sp)
                Spacer(Modifier.height(16.dp))
                Text("Which way do you flick to reach it?")
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(top = 10.dp)) {
                    Side.entries.forEach { side ->
                        val on = side == pc.side
                        Box(
                            contentAlignment = Alignment.Center,
                            modifier = Modifier
                                .height(44.dp)
                                .clip(RoundedCornerShape(10.dp))
                                .border(1.dp, if (on) FlickBlue else Line, RoundedCornerShape(10.dp))
                                .background(if (on) Color(0xFF0F1824) else Color.Transparent)
                                .clickable { onSide(side) }
                                .semantics { selected = on; role = Role.RadioButton }
                                .padding(horizontal = 14.dp),
                        ) { Text("${side.arrow} ${side.label}") }
                    }
                }
                Spacer(Modifier.height(12.dp))
                Text("In FlickDesk, set “My phone sits on the…” to the matching edge so the light comes from the right place.", color = Faint, fontSize = 12.sp)
            }
        },
    )
}
