package dev.flick.app.ui

import android.graphics.BlendMode
import android.graphics.BlurMaskFilter
import android.graphics.ComposeShader
import android.graphics.LinearGradient
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.PorterDuff
import android.graphics.RadialGradient
import android.graphics.Shader
import android.graphics.SweepGradient
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.drawscope.drawIntoCanvas
import androidx.compose.ui.graphics.nativeCanvas
import dev.flick.app.EffectPreset
import dev.flick.app.Side
import kotlin.math.abs
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow

/** Palettes shared with FlickDesk (same hex values). Saturated so the light reads on any wallpaper. */
object FlickPalettes {
    val all: Map<String, IntArray> = linkedMapOf(
        "Spectrum" to colors(0xFF4D7D, 0xFF9F43, 0xFFD84D, 0x3DF5A0, 0x3DC7FF, 0x6F6BFF, 0xD65CFF),
        "Aurora" to colors(0x2FF5C4, 0x3DC7FF, 0x6F6BFF, 0xB45CFF),
        "Ember" to colors(0xFF6A3D, 0xFFB84D, 0xFF4D8A, 0xFF7F4D),
        "Glacier" to colors(0xFFFFFF, 0x8FD2FF, 0x9FB0FF, 0xD6EFFF),
    )

    fun get(name: String): IntArray = all[name] ?: all.getValue("Spectrum")

    private fun colors(vararg rgb: Int) = IntArray(rgb.size) { (0xFF000000 or rgb[it].toLong()).toInt() }
}

private const val DURATION = 2.8f

/**
 * The phone-side light, matching FlickDesk's presets. Plays from the edge facing [side] each
 * time [trigger] changes. Native paints give real blur and screen (additive-looking) blending.
 */
@Composable
fun EdgeEffect(trigger: Int, side: Side, preset: EffectPreset, palette: String, modifier: Modifier = Modifier) {
    val time = remember { Animatable(0f) }
    LaunchedEffect(trigger) {
        if (trigger == 0) return@LaunchedEffect
        time.snapTo(0.001f)
        time.animateTo(DURATION, tween((DURATION * 1000).toInt(), easing = LinearEasing))
        time.snapTo(0f)
    }
    val colors = remember(palette) { FlickPalettes.get(palette) }

    Canvas(modifier.fillMaxSize()) {
        val t = time.value
        if (t <= 0f || t >= DURATION) return@Canvas
        val dp = density
        drawIntoCanvas { canvas ->
            EffectPainter(canvas.nativeCanvas, size, side, colors, dp).draw(preset, t)
        }
    }
}

/**
 * Draws one frame at time [t] seconds. Layers (Spectral Surge): a brief radial dim, the screen's
 * frame igniting from the entry point and running round, a bright glass wave crossing the whole
 * screen, spectral rings riding it, a warm/cool caustic hairline, and a flare with a streak.
 */
class EffectPainter(
    private val canvas: android.graphics.Canvas,
    size: Size,
    private val side: Side,
    private val colors: IntArray,
    private val dp: Float,
) {
    private val w = size.width
    private val h = size.height
    private val ox: Float
    private val oy: Float
    private val ix: Float // inward unit vector
    private val iy: Float
    private val reach = hypot(w, h) * 1.02f // rings reach the far corner
    private val minDim = min(w, h)

    init {
        when (side) {
            Side.LEFT -> { ox = 0f; oy = h * 0.42f; ix = 1f; iy = 0f }
            Side.RIGHT -> { ox = w; oy = h * 0.42f; ix = -1f; iy = 0f }
            Side.ABOVE -> { ox = w / 2f; oy = 0f; ix = 0f; iy = 1f }
        }
    }

    fun draw(preset: EffectPreset, t: Float) {
        when (preset) {
            EffectPreset.SURGE -> {
                dim(t, 1f)
                frame(t, minDim * 0.16f, hold = 0.5f)
                wave(t, 0.03f, reach)
                ring(t, 0.04f, reach, crisp = true)
                ring(t, 0.18f, reach, crisp = false)
                hairlines(t, 0.04f, reach)
                flare(t, 0f, 1f, streak = true)
            }
            EffectPreset.RIPPLE, EffectPreset.PRISM -> {
                wave(t, 0f, reach * 0.85f)
                ring(t, 0f, reach * 0.85f, crisp = true)
                ring(t, 0.15f, reach * 0.85f, crisp = false)
                ring(t, 0.30f, reach * 0.85f, crisp = false)
                hairlines(t, 0f, reach * 0.85f)
                flare(t, 0f, 0.85f, streak = true)
            }
            EffectPreset.HALO, EffectPreset.AURORA -> {
                dim(t, 0.6f)
                frame(t, minDim * 0.2f, hold = 1.0f)
                flare(t, 0.06f, 0.7f, streak = false)
            }
            EffectPreset.GLASS -> {
                dim(t, 0.7f)
                glassRim(t, 0f, reach * 0.9f, 1f)
                glassRim(t, 0.14f, reach * 0.6f, 0.55f)
                hairlines(t, 0f, reach * 0.9f)
                flare(t, 0.04f, 0.8f, streak = false)
            }
            EffectPreset.QUIET -> {
                frame(t, minDim * 0.1f, hold = 0.4f, onlyEntryEdge = true)
                flare(t, 0f, 0.55f, streak = false)
            }
        }
    }

    // ---------------------------------------------------------------- envelopes

    private fun easeOutExpo(x: Float) = if (x >= 1f) 1f else 1f - 2f.pow(-10f * x)
    private fun easeOutCubic(x: Float) = 1f - (1f - x.coerceIn(0f, 1f)).pow(3)
    /** A gentle "settle": fast start, long glide (≈ cubic-bezier .16,.7,.24,1). */
    private fun glide(x: Float) = 1f - (1f - x.coerceIn(0f, 1f)).pow(4)

    /** Rise over [attack], hold until [holdUntil], fall to 0 by [end] — seconds after [start]. */
    private fun env(t: Float, start: Float, attack: Float, holdUntil: Float, end: Float): Float {
        val x = t - start
        if (x <= 0f || x >= end) return 0f
        if (x < attack) return easeOutCubic(x / attack)
        if (x < holdUntil) return 1f
        val f = (x - holdUntil) / (end - holdUntil)
        return (1f - f) * (1f - f)
    }

    private fun alpha(c: Int, a: Float): Int = (c and 0x00FFFFFF) or ((a.coerceIn(0f, 1f) * 255).toInt() shl 24)
    private fun screen() = Paint(Paint.ANTI_ALIAS_FLAG).apply { blendMode = BlendMode.SCREEN }

    private fun sample(u: Float): Int {
        val x = ((u % 1f) + 1f) % 1f * colors.size
        val i = x.toInt() % colors.size
        val j = (i + 1) % colors.size
        val f = x - x.toInt()
        val a = colors[i]
        val b = colors[j]
        fun ch(shift: Int) = (((a shr shift) and 0xFF) * (1 - f) + ((b shr shift) and 0xFF) * f).toInt()
        return (0xFF shl 24) or (ch(16) shl 16) or (ch(8) shl 8) or ch(0)
    }

    private fun sweep(rotate: Float): SweepGradient {
        val cs = colors + colors[0]
        return SweepGradient(ox, oy, cs, null).apply { setLocalMatrix(Matrix().apply { setRotate(rotate, ox, oy) }) }
    }

    // ---------------------------------------------------------------- layers

    /** A brief darkening around the entry point so the light reads on bright screens. */
    private fun dim(t: Float, strength: Float) {
        val a = env(t, 0f, 0.15f, 0.5f, 1.5f) * strength
        if (a <= 0f) return
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            shader = RadialGradient(ox, oy, max(w, h) * 1.15f,
                intArrayOf(alpha(0xFF04060C.toInt(), 0.36f * a), alpha(0xFF04060C.toInt(), 0.13f * a), 0),
                floatArrayOf(0f, 0.45f, 0.85f), Shader.TileMode.CLAMP)
        }
        canvas.drawRect(0f, 0f, w, h, p)
    }

    /** The frame of the screen glows in from the entry point and runs round the edges. */
    private fun frame(t: Float, thickness: Float, hold: Float, onlyEntryEdge: Boolean = false) {
        val perimeter = 2 * (w + h)
        // edge: (rect, vertical, bright side at start?, perimeter start, clockwise along +axis?)
        data class E(val name: String, val vertical: Boolean, val length: Float)
        val edges = listOf(E("top", false, w), E("right", true, h), E("bottom", false, w), E("left", true, h))
        for (e in edges) {
            val entry = if (e.vertical) oy.coerceIn(0f, h) else ox.coerceIn(0f, w)
            val ex = when (e.name) { "top", "bottom" -> entry; "left" -> 0f; else -> w }
            val ey = when (e.name) { "left", "right" -> entry; "top" -> 0f; else -> h }
            val distance = hypot(ex - ox, ey - oy)
            if (onlyEntryEdge && distance > 2f) continue

            val delay = distance / (w + h) * 0.42f
            val reveal = 0.5f + e.length / (w + h) * 0.35f
            val p01 = glide((t - delay) / reveal)
            if (p01 <= 0f) continue
            val a = env(t, delay, 0.06f, reveal + hold, reveal + hold + 0.9f)
            if (a <= 0f) continue
            val from = entry - p01 * entry
            val to = entry + p01 * (e.length - entry)

            // colour continues round the perimeter; alpha falls off from the screen edge inward
            val startS = when (e.name) { "top" -> 0f; "right" -> w; "bottom" -> w + h; else -> 2 * w + h }
            val forward = e.name == "top" || e.name == "right"
            val stops = 7
            val cs = IntArray(stops) { i ->
                val u = i / (stops - 1f)
                val s = startS + (if (forward) u else 1 - u) * e.length
                sample(s / perimeter * 1.5f)
            }
            val color = if (e.vertical) LinearGradient(0f, 0f, 0f, h, cs, null, Shader.TileMode.CLAMP)
            else LinearGradient(0f, 0f, w, 0f, cs, null, Shader.TileMode.CLAMP)
            val (mx0, my0, mx1, my1) = when (e.name) {
                "top" -> listOf(0f, 0f, 0f, thickness)
                "bottom" -> listOf(0f, h, 0f, h - thickness)
                "left" -> listOf(0f, 0f, thickness, 0f)
                else -> listOf(w, 0f, w - thickness, 0f)
            }
            val mask = LinearGradient(mx0, my0, mx1, my1,
                intArrayOf(alpha(-1, a), alpha(-1, a * 0.85f), alpha(-1, a * 0.35f), 0),
                floatArrayOf(0f, 0.08f, 0.35f, 1f), Shader.TileMode.CLAMP)
            val soft = screen().apply {
                shader = ComposeShader(color, mask, PorterDuff.Mode.DST_IN)
                maskFilter = BlurMaskFilter(6f * dp, BlurMaskFilter.Blur.NORMAL)
            }
            when (e.name) {
                "top" -> canvas.drawRect(from, 0f, to, thickness, soft)
                "bottom" -> canvas.drawRect(from, h - thickness, to, h, soft)
                "left" -> canvas.drawRect(0f, from, thickness, to, soft)
                else -> canvas.drawRect(w - thickness, from, w, to, soft)
            }
            // a crisp line of light right on the bezel
            val line = screen().apply {
                shader = color
                this.alpha = (a * 230).toInt()
            }
            val lw = 2.2f * dp
            when (e.name) {
                "top" -> canvas.drawRect(from, 0f, to, lw, line)
                "bottom" -> canvas.drawRect(from, h - lw, to, h, line)
                "left" -> canvas.drawRect(0f, from, lw, to, line)
                else -> canvas.drawRect(w - lw, from, w, to, line)
            }
        }
    }

    /** A bright, wide, almost-white band crossing the screen. */
    private fun wave(t: Float, start: Float, maxR: Float) {
        val x = (t - start) / 1.25f
        val a = env(t, start, 0.05f, 0.4f, 1.25f) * 0.5f
        if (a <= 0f || x <= 0f) return
        val radius = maxR * (0.02f + 0.98f * glide(x))
        val p = screen().apply {
            style = Paint.Style.STROKE
            strokeWidth = 44f * dp
            color = alpha(0xFFEBF5FF.toInt(), a)
            maskFilter = BlurMaskFilter(22f * dp, BlurMaskFilter.Blur.NORMAL)
        }
        canvas.drawCircle(ox, oy, radius, p)
    }

    private fun ring(t: Float, start: Float, maxR: Float, crisp: Boolean) {
        val x = (t - start) / 1.25f
        val a = env(t, start, 0.05f, 0.45f, 1.25f) * (if (crisp) 1f else 0.7f)
        if (a <= 0f || x <= 0f) return
        val radius = maxR * (0.02f + 0.98f * glide(x))
        val p = screen().apply {
            style = Paint.Style.STROKE
            strokeWidth = (if (crisp) 7f else 20f) * dp
            maskFilter = BlurMaskFilter((if (crisp) 2.5f else 11f) * dp, BlurMaskFilter.Blur.NORMAL)
            shader = sweep(200f + start * 120f)
            this.alpha = (a * 255).toInt()
        }
        canvas.drawCircle(ox, oy, radius, p)
    }

    /** The glassy caustic: a crisp white hairline flanked by warm and cool lines. */
    private fun hairlines(t: Float, start: Float, maxR: Float) {
        val x = (t - start) / 1.25f
        val a = env(t, start, 0.04f, 0.35f, 1.05f)
        if (a <= 0f || x <= 0f) return
        val radius = maxR * (0.02f + 0.98f * glide(x))
        val ax = abs(iy) * 3f * dp
        val ay = abs(ix) * 3f * dp
        val lines = listOf(
            Triple(0xFFFFFFFF.toInt(), 0f, 0.95f),
            Triple(0xFFFF96AA.toInt(), -1f, 0.75f),
            Triple(0xFF8CDCFF.toInt(), 1f, 0.75f),
        )
        for ((color, sign, k) in lines) {
            val p = screen().apply {
                style = Paint.Style.STROKE
                strokeWidth = 1.3f * dp
                this.color = alpha(color, a * k)
            }
            canvas.drawCircle(ox + ax * sign, oy + ay * sign, radius, p)
        }
    }

    private fun glassRim(t: Float, start: Float, maxR: Float, peak: Float) {
        val x = (t - start) / 1.4f
        val a = env(t, start, 0.08f, 0.6f, 1.4f) * peak
        if (a <= 0f || x <= 0f) return
        val radius = maxR * (0.05f + 0.95f * glide(x))
        val rim = screen().apply {
            style = Paint.Style.STROKE
            strokeWidth = 9f * dp
            maskFilter = BlurMaskFilter(4f * dp, BlurMaskFilter.Blur.NORMAL)
            shader = sweep(120f)
            this.alpha = (a * 200).toInt()
        }
        val white = screen().apply {
            style = Paint.Style.STROKE
            strokeWidth = 3f * dp
            color = alpha(0xFFEAF6FF.toInt(), a * 0.95f)
        }
        val sheen = screen().apply {
            shader = RadialGradient(ox, oy, radius.coerceAtLeast(1f),
                intArrayOf(0, 0, alpha(0xFFD0E8FF.toInt(), a * 0.12f)), floatArrayOf(0f, 0.72f, 1f), Shader.TileMode.CLAMP)
        }
        canvas.drawCircle(ox, oy, radius, sheen)
        canvas.drawCircle(ox, oy, radius, rim)
        canvas.drawCircle(ox, oy, radius, white)
    }

    /** White core where the light enters, plus an anamorphic streak along the edge. */
    private fun flare(t: Float, start: Float, scale: Float, streak: Boolean) {
        val a = env(t, start, 0.07f, 0.3f, 1.0f)
        if (a <= 0f) return
        val r = minDim * 0.36f * scale * (0.15f + 0.85f * easeOutCubic((t - start) / 0.4f))
        val core = screen().apply {
            shader = RadialGradient(ox, oy, r.coerceAtLeast(1f),
                intArrayOf(alpha(-1, a), alpha(-1, a * 0.75f), alpha(0xFFC8E4FF.toInt(), a * 0.2f), 0),
                floatArrayOf(0f, 0.16f, 0.45f, 1f), Shader.TileMode.CLAMP)
        }
        canvas.drawCircle(ox, oy, r, core)
        if (!streak) return

        val len = (if (ix != 0f) h else w) * 0.5f * (0.2f + 0.8f * easeOutCubic((t - start) / 0.45f))
        val sp = screen().apply {
            shader = RadialGradient(0f, 0f, 1f,
                intArrayOf(alpha(-1, a * 0.95f), alpha(0xFFA0D7FF.toInt(), a * 0.5f), 0),
                floatArrayOf(0f, 0.4f, 1f), Shader.TileMode.CLAMP)
        }
        canvas.save()
        canvas.translate(ox + ix * 10f * dp, oy + iy * 10f * dp)
        if (ix != 0f) canvas.scale(14f * dp, len) else canvas.scale(len, 14f * dp)
        canvas.drawCircle(0f, 0f, 1f, sp)
        canvas.restore()
    }
}
