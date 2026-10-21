package dev.flick.app.ui

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.AnimationSpec
import androidx.compose.animation.core.LinearOutSlowInEasing
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.VectorConverter
import androidx.compose.animation.core.animate
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.awaitLongPressOrCancellation
import androidx.compose.foundation.gestures.drag as dragPointer
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.Stable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.changedToUp
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.ui.input.pointer.util.VelocityTracker
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.Velocity
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import dev.flick.app.SendItem
import dev.flick.app.Side
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.launch
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.roundToInt

/** The item (or stack) currently lifted off the grid and following the finger. Drawn by [FloatingItem]. */
@Stable
class FlickDragState {
    var item by mutableStateOf<SendItem?>(null)
        private set
    var bitmap by mutableStateOf<ImageBitmap?>(null)
        private set
    var size by mutableStateOf(IntSize.Zero)
        private set
    var count by mutableIntStateOf(1)
        private set
    /** Top-left in root coordinates. */
    var offset by mutableStateOf(Offset.Zero)
    var scale by mutableFloatStateOf(1f)
    /** Size of the full-screen root the floating copy is drawn in (set by FlickApp). */
    var rootSize by mutableStateOf(IntSize.Zero)

    private var home = Offset.Zero

    fun lift(item: SendItem, bitmap: ImageBitmap?, origin: Offset, size: IntSize, count: Int) {
        this.item = item
        this.bitmap = bitmap
        this.size = size
        this.count = count
        home = origin
        offset = origin
        scale = 1.08f
    }

    val center: Offset get() = offset + Offset(size.width / 2f, size.height / 2f)

    suspend fun flyAway(direction: Offset, root: IntSize) {
        val len = direction.getDistance().coerceAtLeast(1f)
        val target = offset + direction / len * (max(root.width, root.height) * 1.3f)
        coroutineScope {
            launch { animateOffset(target, tween(280, easing = LinearOutSlowInEasing)) }
            launch { animate(scale, 0.6f, animationSpec = tween(280)) { v, _ -> scale = v } }
        }
        clear()
    }

    suspend fun returnHome() {
        coroutineScope {
            launch { animateOffset(home, spring(dampingRatio = 0.7f, stiffness = Spring.StiffnessMediumLow)) }
            launch { animate(scale, 1f, animationSpec = spring()) { v, _ -> scale = v } }
        }
        clear()
    }

    private suspend fun animateOffset(target: Offset, spec: AnimationSpec<Offset>) {
        Animatable(offset, Offset.VectorConverter).animateTo(target, spec) { offset = value }
    }

    private fun clear() {
        item = null
        bitmap = null
    }
}

// ---------------------------------------------------------------- direction maths

private fun dot(a: Offset, b: Offset) = a.x * b.x + a.y * b.y

/** A fast swipe mostly in [side]'s direction. */
fun isFlickToward(side: Side, v: Velocity, threshold: Float): Boolean {
    val vec = Offset(v.x, v.y)
    val toward = dot(vec, side.toward)
    val sideways = abs(dot(vec, Offset(side.toward.y, side.toward.x)))
    return toward > threshold && toward > sideways * 0.7f
}

/** Dragged slowly but let go right at that edge also counts. */
fun isAtEdge(side: Side, center: Offset, root: IntSize, margin: Float): Boolean = when (side) {
    Side.LEFT -> center.x < margin
    Side.RIGHT -> center.x > root.width - margin
    Side.ABOVE -> center.y < margin
}

/** Angle of the flick → where along the PC's screen edge the light appears (0..1). */
fun edgePosFor(side: Side, v: Velocity): Float {
    val vec = Offset(v.x, v.y)
    val toward = max(dot(vec, side.toward), 1f)
    val along = when (side) {
        Side.LEFT, Side.RIGHT -> v.y
        Side.ABOVE -> v.x
    }
    return (0.5f + along / toward * 0.35f).coerceIn(0.1f, 0.9f)
}

/** The result of letting go: which way the item should leave (or null to snap back). */
class FlickTarget(val side: Side, val edgePos: Float, val direction: Offset)

// ---------------------------------------------------------------- tiles

/**
 * Tap toggles selection. Long-press lifts the item — or the whole stack if it's selected —
 * then flick (or drag to an edge) towards a PC. Long-press and let go without moving selects.
 *
 * [onRelease] decides what happens: return a [FlickTarget] to send, or null to snap back.
 */
@Composable
fun FlickTile(
    item: SendItem,
    drag: FlickDragState,
    selectedIndex: Int,
    stackSize: Int,
    progress: Float?,
    onTap: (SendItem) -> Unit,
    onRelease: (SendItem, Velocity, Offset) -> FlickTarget?,
    modifier: Modifier = Modifier,
) {
    val scope = rememberCoroutineScope()
    val haptics = LocalHapticFeedback.current
    val density = LocalDensity.current
    val bitmap = rememberThumbnail(item.uri)
    val currentBitmap by rememberUpdatedState(bitmap)
    val currentOnTap by rememberUpdatedState(onTap)
    val currentOnRelease by rememberUpdatedState(onRelease)
    val currentStack by rememberUpdatedState(if (selectedIndex >= 0) stackSize else 1)
    var origin by remember { mutableStateOf(Offset.Zero) }
    var tileSize by remember { mutableStateOf(IntSize.Zero) }
    val lifted = drag.item?.key == item.key
    val selected = selectedIndex >= 0
    val tapSlop = with(density) { 12.dp.toPx() }

    Box(
        modifier
            .aspectRatio(1f)
            .clip(RoundedCornerShape(10.dp))
            .background(TileBackground)
            .onGloballyPositioned {
                origin = it.positionInRoot()
                tileSize = it.size
            }
            .graphicsLayer { alpha = if (lifted) 0.25f else 1f }
            .semantics {
                contentDescription = item.name + if (selected) ", selected" else ""
                this.selected = selected
            }
            .pointerInput(item.key) {
                val tracker = VelocityTracker()
                awaitEachGesture {
                    val down = awaitFirstDown(requireUnconsumed = false)
                    val longPress = awaitLongPressOrCancellation(down.id)
                    if (longPress == null) {
                        // Quick tap (not a scroll – the grid consumes those)?
                        val up = currentEvent.changes.firstOrNull { it.id == down.id }
                        if (up != null && up.changedToUp() && !up.isConsumed) {
                            up.consume()
                            currentOnTap(item)
                        }
                        return@awaitEachGesture
                    }

                    haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                    drag.lift(item, currentBitmap, origin, tileSize, currentStack)
                    tracker.resetTracking()
                    tracker.addPosition(longPress.uptimeMillis, longPress.position)
                    var moved = 0f
                    dragPointer(longPress.id) { change ->
                        val delta = change.positionChange()
                        moved += delta.getDistance()
                        drag.offset += delta
                        tracker.addPosition(change.uptimeMillis, change.position)
                        change.consume()
                    }

                    if (moved < tapSlop) {
                        // long-press without moving = select, like a gallery
                        currentOnTap(item)
                        scope.launch { drag.returnHome() }
                        return@awaitEachGesture
                    }

                    val v = tracker.calculateVelocity()
                    val target = currentOnRelease(item, v, drag.center)
                    scope.launch {
                        if (target != null) {
                            haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                            drag.flyAway(target.direction, drag.rootSize)
                        } else {
                            drag.returnHome()
                        }
                    }
                }
            },
    ) {
        TileContent(item, bitmap)
        if (selected) {
            Box(
                Modifier
                    .fillMaxSize()
                    .border(2.dp, FlickBlue, RoundedCornerShape(10.dp))
                    .background(FlickBlue.copy(alpha = 0.12f)),
            )
            Box(
                Modifier
                    .align(Alignment.TopEnd)
                    .padding(6.dp)
                    .size(22.dp)
                    .clip(CircleShape)
                    .background(FlickBlue),
                contentAlignment = Alignment.Center,
            ) {
                Text("${selectedIndex + 1}", color = Color(0xFF06121F), fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
            }
        }
        if (progress != null) {
            LinearProgressIndicator(
                progress = { progress },
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .fillMaxWidth()
                    .padding(6.dp),
            )
        }
    }
}

@Composable
fun TileContent(item: SendItem, bitmap: ImageBitmap?) {
    Box(Modifier.fillMaxSize()) {
        if (bitmap != null) {
            Image(
                bitmap = bitmap,
                contentDescription = null,
                contentScale = ContentScale.Crop,
                modifier = Modifier.fillMaxSize(),
            )
        } else {
            val ext = item.name.substringAfterLast('.', "").take(5).uppercase()
            Text(
                text = if (ext.isNotEmpty()) ".$ext" else "FILE",
                color = FlickBlue,
                fontWeight = FontWeight.Bold,
                fontSize = 18.sp,
                modifier = Modifier.align(Alignment.Center),
            )
            Text(
                text = item.name,
                color = Color.White.copy(alpha = 0.7f),
                fontSize = 10.sp,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                textAlign = TextAlign.Center,
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .fillMaxWidth()
                    .padding(horizontal = 6.dp, vertical = 8.dp),
            )
        }
        if (item.mime.startsWith("video/")) {
            Icon(
                Icons.Filled.PlayArrow,
                contentDescription = null,
                tint = Color.White,
                modifier = Modifier
                    .align(Alignment.BottomStart)
                    .padding(4.dp)
                    .size(18.dp),
            )
        }
    }
}

/** The lifted copy that follows the finger and flies off-screen. Place it in the root Box. */
@Composable
fun FloatingItem(drag: FlickDragState) {
    val item = drag.item ?: return
    val density = LocalDensity.current
    val w = with(density) { drag.size.width.toDp() }
    val h = with(density) { drag.size.height.toDp() }
    Box(
        Modifier
            .offset { IntOffset(drag.offset.x.roundToInt(), drag.offset.y.roundToInt()) }
            .size(w, h)
            .graphicsLayer {
                scaleX = drag.scale
                scaleY = drag.scale
            },
    ) {
        // a fanned stack behind the lifted item
        for (i in (minOf(drag.count, 3) - 1) downTo 1) {
            Box(
                Modifier
                    .fillMaxSize()
                    .graphicsLayer {
                        rotationZ = if (i % 2 == 0) 7f * i / 2 else -6f * i
                        translationX = 6f * i
                        shadowElevation = 12f
                        shape = RoundedCornerShape(12.dp)
                        clip = true
                    }
                    .background(Color(0xFF2A2E37)),
            )
        }
        Box(
            Modifier
                .fillMaxSize()
                .graphicsLayer {
                    shadowElevation = 24f
                    shape = RoundedCornerShape(12.dp)
                    clip = true
                }
                .background(TileBackground),
        ) {
            TileContent(item, drag.bitmap)
        }
        if (drag.count > 1) {
            Box(
                Modifier
                    .align(Alignment.TopEnd)
                    .offset(x = 8.dp, y = (-8).dp)
                    .size(28.dp)
                    .clip(CircleShape)
                    .background(FlickBlue),
                contentAlignment = Alignment.Center,
            ) {
                Text("${drag.count}", color = Color(0xFF06121F), fontSize = 13.sp, fontWeight = FontWeight.Bold)
            }
        }
    }
}
