package dev.flick.app.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val FlickBlue = Color(0xFF7CC4FF)
val FlickPink = Color(0xFFFF78C8)
val FlickAmber = Color(0xFFFFC86E)
val TileBackground = Color(0xFF1C1C1E)
val Surface = Color(0xFF0E0F13)
val Line = Color(0xFF23262E)
val Muted = Color(0xFFA3A9B6)
val Faint = Color(0xFF8C93A1)
val Good = Color(0xFF4CD98A)

@Composable
fun FlickTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = FlickBlue,
            onPrimary = Color(0xFF06121F),
            secondary = FlickPink,
            background = Color.Black,
            surface = Color(0xFF16181E),
            surfaceVariant = TileBackground,
            outline = Color(0xFF3A3F49),
        ),
        content = content,
    )
}
