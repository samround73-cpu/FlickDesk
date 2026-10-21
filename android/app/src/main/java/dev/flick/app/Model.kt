package dev.flick.app

import android.net.Uri
import androidx.compose.ui.geometry.Offset

/** A direction you can flick in. Each paired PC owns one. */
enum class Side(val label: String, val arrow: String) {
    LEFT("Left", "←"),
    ABOVE("Above", "↑"),
    RIGHT("Right", "→");

    /** Unit vector pointing from the phone towards the PC. */
    val toward: Offset
        get() = when (this) {
            LEFT -> Offset(-1f, 0f)
            RIGHT -> Offset(1f, 0f)
            ABOVE -> Offset(0f, -1f)
        }
}

/** Something on the phone that can be flicked. size = -1 when unknown. */
data class SendItem(val uri: Uri, val name: String, val mime: String, val size: Long) {
    val key: String get() = uri.toString()
}

data class ReceivedFile(val uri: Uri, val name: String, val mime: String, val fromPc: String, val side: Side, val size: Long)

/** A PC this phone has paired with. [key] is the base64 long-term AES key. */
data class PairedPc(
    val id: String,
    val name: String,
    val host: String,
    val port: Int,
    val key: String,
    val side: Side,
)

/** A FlickDesk PC seen on the network (beacon). */
data class NearbyPc(val id: String, val name: String, val address: String, val port: Int, val paused: Boolean)

enum class PcState { UNKNOWN, ONLINE, OFFLINE, PAUSED }

data class HistoryItem(
    val name: String,
    val uri: String?,
    val mime: String,
    val incoming: Boolean,
    val pcName: String,
    val size: Long,
    val time: Long,
    val clipboard: Boolean = false,
)

enum class EffectPreset(val label: String, val blurb: String) {
    RIPPLE("Spectral Ripple", "Rings across the screen"),
    AURORA("Aurora Veil", "Soft curtains"), // legacy – plays as Halo
    PRISM("Prism Sweep", "A shard of spectrum"), // legacy – plays as Ripple
    GLASS("Liquid Glass", "A lens swells"),
    QUIET("Quiet", "A soft glow at the edge"),
    SURGE("Spectral Surge", "The whole screen answers"),
    HALO("Halo", "The frame glows"),
    ;

    companion object {
        /** What the settings screen offers. */
        val choices = listOf(SURGE, RIPPLE, HALO, GLASS, QUIET)
    }
}

enum class Haptic(val label: String) { TICK("Tick"), RIPPLE("Ripple"), THUD("Thud"), OFF("Off") }

data class FlickSettings(
    val effect: EffectPreset = EffectPreset.SURGE,
    val palette: String = "Spectrum",
    /** 0 = a light swipe sends, 1 = needs a firm throw. */
    val sensitivity: Float = 0.45f,
    val haptic: Haptic = Haptic.RIPPLE,
    val backgroundReceive: Boolean = true,
    val autoCopyFromPc: Boolean = true,
)

/** Where a pairing attempt is up to (drives the confirm sheet). */
sealed interface PairingState {
    data object Idle : PairingState
    data class Connecting(val pcName: String) : PairingState
    data class Confirm(val pcName: String, val checkNumber: String, val needsPcApproval: Boolean) : PairingState
    data class WaitingForPc(val pcName: String, val checkNumber: String) : PairingState
    data class Done(val pcName: String) : PairingState
    data class Failed(val message: String) : PairingState
}
