package dev.flick.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import dev.flick.app.EffectPreset
import dev.flick.app.FlickSettings
import dev.flick.app.Haptic
import dev.flick.app.PairedPc
import dev.flick.app.PcState
import dev.flick.app.Side
import dev.flick.app.core.Haptics

@Composable
fun SettingsScreen(
    settings: FlickSettings,
    pcs: List<PairedPc>,
    pcStates: Map<String, PcState>,
    deviceName: String,
    onChange: ((FlickSettings) -> FlickSettings) -> Unit,
    onOpenPc: (PairedPc) -> Unit,
    onBack: () -> Unit,
) {
    val context = LocalContext.current
    var preview by remember { mutableIntStateOf(1) }

    Column(
        Modifier
            .fillMaxSize()
            .statusBarsPadding()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp)
            .navigationBarsPadding()
            .padding(bottom = 24.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 6.dp)) {
            IconButton(onClick = onBack, modifier = Modifier.padding(start = 0.dp)) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back", tint = Color.White)
            }
            Text("Look & feel", color = Color.White, fontSize = 22.sp, fontWeight = FontWeight.SemiBold)
        }

        // live preview
        Box(
            Modifier
                .padding(top = 12.dp)
                .fillMaxWidth()
                .height(150.dp)
                .clip(RoundedCornerShape(18.dp))
                .background(Brush.radialGradient(listOf(Color(0xFF18243B), Color(0xFF090C14))))
                .border(1.dp, Line, RoundedCornerShape(18.dp)),
        ) {
            EdgeEffect(trigger = preview, side = Side.LEFT, preset = settings.effect, palette = settings.palette)
            IconButton(
                onClick = { preview++; Haptics.play(context, settings.haptic) },
                modifier = Modifier.align(Alignment.BottomEnd).padding(6.dp),
            ) {
                Icon(Icons.Filled.Refresh, contentDescription = "Replay the effect", tint = Color.White)
            }
            Text(
                settings.effect.label,
                color = Color.White,
                fontWeight = FontWeight.SemiBold,
                modifier = Modifier.align(Alignment.BottomStart).padding(14.dp),
            )
        }

        SectionLabel("Arrival effect")
        LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            items(EffectPreset.choices) { p ->
                val on = p == when (settings.effect) {
                    EffectPreset.AURORA -> EffectPreset.HALO
                    EffectPreset.PRISM -> EffectPreset.RIPPLE
                    else -> settings.effect
                }
                Column(
                    Modifier
                        .width(118.dp)
                        .clip(RoundedCornerShape(14.dp))
                        .border(1.dp, if (on) FlickBlue else Line, RoundedCornerShape(14.dp))
                        .background(if (on) Color(0xFF0F1824) else Surface)
                        .clickable { onChange { s -> s.copy(effect = p) }; preview++ }
                        .semantics { selected = on; role = Role.RadioButton }
                        .padding(12.dp),
                ) {
                    Text(p.label, color = Color.White, fontSize = 13.5.sp, fontWeight = FontWeight.SemiBold)
                    Text(p.blurb, color = Muted, fontSize = 12.sp)
                }
            }
        }

        SectionLabel("Palette")
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            FlickPalettes.all.forEach { (name, ints) ->
                val on = name == settings.palette
                val colors = (ints.toList() + ints[0]).map { Color(it) }
                Box(
                    contentAlignment = Alignment.Center,
                    modifier = Modifier
                        .size(48.dp)
                        .clip(CircleShape)
                        .border(2.dp, if (on) Color.White else Color.Transparent, CircleShape)
                        .clickable { onChange { s -> s.copy(palette = name) }; preview++ }
                        .semantics { contentDescription = name; selected = on; role = Role.RadioButton },
                ) {
                    Box(Modifier.size(38.dp).clip(CircleShape).background(Brush.sweepGradient(colors)))
                }
            }
        }

        SectionLabel("Flick feel")
        Column(
            Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(16.dp))
                .background(Surface)
                .border(1.dp, Line, RoundedCornerShape(16.dp))
                .padding(14.dp),
        ) {
            Row {
                Text("Flick sensitivity", color = Color.White, fontWeight = FontWeight.Medium)
                Spacer(Modifier.weight(1f))
                Text(
                    when {
                        settings.sensitivity < 0.33f -> "Light"
                        settings.sensitivity < 0.66f -> "Medium"
                        else -> "Firm"
                    },
                    color = Muted,
                    fontSize = 12.5.sp,
                )
            }
            Slider(
                value = settings.sensitivity,
                onValueChange = { v -> onChange { s -> s.copy(sensitivity = v) } },
                colors = SliderDefaults.colors(thumbColor = Color.White, activeTrackColor = FlickBlue),
                modifier = Modifier.semantics { contentDescription = "Flick sensitivity" },
            )
            Row {
                Text("Light swipe", color = Faint, fontSize = 11.5.sp)
                Spacer(Modifier.weight(1f))
                Text("Firm throw", color = Faint, fontSize = 11.5.sp)
            }
            Spacer(Modifier.height(14.dp))
            Text("Haptics", color = Color.White, fontWeight = FontWeight.Medium)
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp), modifier = Modifier.padding(top = 10.dp)) {
                Haptic.entries.forEach { hp ->
                    val on = hp == settings.haptic
                    Box(
                        contentAlignment = Alignment.Center,
                        modifier = Modifier
                            .weight(1f)
                            .height(44.dp)
                            .clip(RoundedCornerShape(10.dp))
                            .border(1.dp, if (on) FlickBlue else Line, RoundedCornerShape(10.dp))
                            .background(if (on) Color(0xFF0F1824) else Color.Transparent)
                            .clickable { onChange { s -> s.copy(haptic = hp) }; Haptics.play(context, hp) }
                            .semantics { selected = on; role = Role.RadioButton },
                    ) { Text(hp.label, color = if (on) Color.White else Color(0xFFC9CED8), fontSize = 13.sp) }
                }
            }
        }

        SectionLabel("Receiving")
        SwitchRow(
            "Receive in the background",
            "Files from your PC arrive even when Flick is closed (shows a quiet ongoing notification)",
            settings.backgroundReceive,
        ) { v -> onChange { s -> s.copy(backgroundReceive = v) } }
        SwitchRow(
            "Auto-copy text from the PC",
            "Clipboard you send from FlickDesk goes straight to this phone's clipboard",
            settings.autoCopyFromPc,
        ) { v -> onChange { s -> s.copy(autoCopyFromPc = v) } }
        Text(
            "Photos save to Pictures › Flick, videos to Movies › Flick, everything else to Download › Flick.",
            color = Faint,
            fontSize = 12.sp,
            modifier = Modifier.padding(top = 6.dp),
        )

        if (pcs.isNotEmpty()) {
            SectionLabel("Your PCs")
            pcs.forEach { pc ->
                val state = pcStates[pc.id] ?: PcState.UNKNOWN
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(12.dp))
                        .clickable { onOpenPc(pc) }
                        .padding(vertical = 10.dp),
                ) {
                    MonitorGlyph(Color(0xFFCFE8FF))
                    Column(Modifier.weight(1f).padding(start = 12.dp)) {
                        Text(pc.name, color = Color.White, fontWeight = FontWeight.Medium)
                        Text("${stateLabel(state)} · flick ${pc.side.label.lowercase()} ${pc.side.arrow}", color = Muted, fontSize = 12.sp)
                    }
                    Box(Modifier.size(8.dp).clip(CircleShape).background(stateColor(state)))
                }
            }
        }

        SectionLabel("Security")
        Text(
            "Everything between this phone and your PCs is end-to-end encrypted (AES-256-GCM). " +
                "Keys are created when you pair with the QR code and never leave the two devices.",
            color = Muted,
            fontSize = 12.5.sp,
        )
        Text("This phone appears as “$deviceName”.", color = Faint, fontSize = 12.sp, modifier = Modifier.padding(top = 8.dp))
    }
}

@Composable
private fun SwitchRow(title: String, hint: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .fillMaxWidth()
            .toggleable(value = checked, role = Role.Switch, onValueChange = onChange)
            .padding(vertical = 10.dp),
    ) {
        Column(Modifier.weight(1f).padding(end = 12.dp)) {
            Text(title, color = Color.White, fontWeight = FontWeight.Medium)
            Text(hint, color = Muted, fontSize = 12.sp)
        }
        Switch(
            checked = checked,
            onCheckedChange = null,
            colors = SwitchDefaults.colors(checkedTrackColor = Color(0xFF3B82C4), checkedThumbColor = Color.White),
        )
    }
}
