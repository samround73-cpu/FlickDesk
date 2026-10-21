package dev.flick.app.ui

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.annotation.OptIn
import androidx.camera.core.CameraSelector
import androidx.camera.core.ExperimentalGetImage
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
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
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Done
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import dev.flick.app.PairingState
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

/** Camera viewfinder that pairs as soon as it sees a Flick QR code. Also offers "Enter IP". */
@Composable
fun ScanScreen(
    pairing: PairingState,
    onCode: (String) -> Boolean,
    onManual: (String) -> Unit,
    onClose: () -> Unit,
) {
    val context = LocalContext.current
    var hasCamera by remember {
        mutableStateOf(ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED)
    }
    val askCamera = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { hasCamera = it }
    LaunchedEffect(Unit) { if (!hasCamera) askCamera.launch(Manifest.permission.CAMERA) }

    var manual by remember { mutableStateOf("") }
    var showManual by remember { mutableStateOf(false) }
    var hint by remember { mutableStateOf("Point at the code on your PC") }
    val paused = pairing != PairingState.Idle && pairing !is PairingState.Failed

    Box(Modifier.fillMaxSize().background(Color.Black)) {
        if (hasCamera) {
            QrCamera(
                active = !paused,
                onCode = { raw -> if (!onCode(raw)) hint = "That isn't a Flick code – open FlickDesk → Pair a phone" },
                modifier = Modifier.fillMaxSize(),
            )
        } else {
            Text(
                "Flick needs the camera to scan your PC's pairing code.\nYou can enter the PC's IP address instead.",
                color = Muted,
                textAlign = TextAlign.Center,
                modifier = Modifier.align(Alignment.Center).padding(32.dp),
            )
        }

        Viewfinder(Modifier.align(Alignment.Center).size(260.dp))
        Text(
            hint,
            color = Color(0xFFE6E8EC),
            fontSize = 13.sp,
            textAlign = TextAlign.Center,
            modifier = Modifier
                .align(Alignment.Center)
                .padding(top = 310.dp, start = 24.dp, end = 24.dp),
        )

        Row(
            verticalAlignment = Alignment.CenterVertically,
            modifier = Modifier
                .statusBarsPadding()
                .fillMaxWidth()
                .padding(horizontal = 12.dp, vertical = 8.dp),
        ) {
            IconButton(onClick = onClose, modifier = Modifier.clip(CircleShape).background(Color(0x73000000))) {
                Icon(Icons.Filled.Close, contentDescription = "Close", tint = Color.White)
            }
            Text("Add a PC", color = Color.White, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, textAlign = TextAlign.Center, modifier = Modifier.weight(1f))
            Spacer(Modifier.width(48.dp))
        }

        if (pairing == PairingState.Idle || pairing is PairingState.Failed) {
            Column(
                Modifier
                    .align(Alignment.BottomCenter)
                    .navigationBarsPadding()
                    .padding(16.dp)
                    .fillMaxWidth(),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                if (showManual) {
                    Column(
                        Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(20.dp))
                            .background(Color(0xF0141619))
                            .padding(16.dp),
                    ) {
                        Text("PC's IP address", color = Color.White, fontWeight = FontWeight.Medium)
                        OutlinedTextField(
                            value = manual,
                            onValueChange = { manual = it },
                            singleLine = true,
                            placeholder = { Text("192.168.1.20") },
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                            modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp),
                        )
                        Text("You'll confirm a 6-digit number on both screens.", color = Muted, fontSize = 12.sp)
                        Button(
                            onClick = { onManual(manual.trim()) },
                            enabled = manual.isNotBlank(),
                            colors = ButtonDefaults.buttonColors(containerColor = FlickBlue, contentColor = Color(0xFF06121F)),
                            shape = RoundedCornerShape(12.dp),
                            modifier = Modifier.fillMaxWidth().padding(top = 10.dp),
                        ) { Text("Connect", fontWeight = FontWeight.SemiBold) }
                    }
                } else {
                    TextButton(onClick = { showManual = true }) { Text("Enter IP address instead", color = FlickBlue) }
                }
            }
        }
    }
}

@Composable
private fun Viewfinder(modifier: Modifier) {
    val sweep = rememberInfiniteTransition(label = "scan")
    val y by sweep.animateFloat(0.04f, 0.96f, infiniteRepeatable(tween(2400, easing = LinearEasing), RepeatMode.Reverse), label = "y")
    val spectrum = listOf(Color(0x00FF6B8B), Color(0xFFFF6B8B), Color(0xFFFFE46B), Color(0xFF7DFFB2), Color(0xFF6BD3FF), Color(0xFFA98BFF), Color(0x00A98BFF))
    Canvas(modifier) {
        val c = 40.dp.toPx()
        val r = 22.dp.toPx()
        val sw = 3.dp.toPx()
        val stroke = Stroke(width = sw)
        // four rounded corner brackets
        drawArc(Color.White, 180f, 90f, false, Offset(0f, 0f), Size(r * 2, r * 2), style = stroke)
        drawLine(Color.White, Offset(r, 0f), Offset(c, 0f), sw)
        drawLine(Color.White, Offset(0f, r), Offset(0f, c), sw)
        drawArc(Color.White, 270f, 90f, false, Offset(size.width - r * 2, 0f), Size(r * 2, r * 2), style = stroke)
        drawLine(Color.White, Offset(size.width - c, 0f), Offset(size.width - r, 0f), sw)
        drawLine(Color.White, Offset(size.width, r), Offset(size.width, c), sw)
        drawArc(Color.White, 90f, 90f, false, Offset(0f, size.height - r * 2), Size(r * 2, r * 2), style = stroke)
        drawLine(Color.White, Offset(r, size.height), Offset(c, size.height), sw)
        drawLine(Color.White, Offset(0f, size.height - c), Offset(0f, size.height - r), sw)
        drawArc(Color.White, 0f, 90f, false, Offset(size.width - r * 2, size.height - r * 2), Size(r * 2, r * 2), style = stroke)
        drawLine(Color.White, Offset(size.width - c, size.height), Offset(size.width - r, size.height), sw)
        drawLine(Color.White, Offset(size.width, size.height - c), Offset(size.width, size.height - r), sw)
        // spectral scan line with a soft glow
        val ly = size.height * y
        drawRoundRect(
            Brush.horizontalGradient(spectrum.map { it.copy(alpha = it.alpha * 0.35f) }),
            topLeft = Offset(8.dp.toPx(), ly - 9.dp.toPx()),
            size = Size(size.width - 16.dp.toPx(), 18.dp.toPx()),
            cornerRadius = CornerRadius(9.dp.toPx()),
        )
        drawRoundRect(
            Brush.horizontalGradient(spectrum),
            topLeft = Offset(8.dp.toPx(), ly - 1.dp.toPx()),
            size = Size(size.width - 16.dp.toPx(), 2.dp.toPx()),
            cornerRadius = CornerRadius(1.dp.toPx()),
        )
    }
}

/** CameraX preview + ML Kit QR detection. Calls [onCode] on the main thread. */
@Composable
private fun QrCamera(active: Boolean, onCode: (String) -> Unit, modifier: Modifier) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val currentOnCode by rememberUpdatedState(onCode)
    val currentActive by rememberUpdatedState(active)
    val previewView = remember { PreviewView(context).apply { scaleType = PreviewView.ScaleType.FILL_CENTER } }

    DisposableEffect(lifecycleOwner) {
        val executor = Executors.newSingleThreadExecutor()
        val scanner = BarcodeScanning.getClient(
            BarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).build(),
        )
        val providerFuture = ProcessCameraProvider.getInstance(context)
        val mainExecutor = ContextCompat.getMainExecutor(context)
        val busy = AtomicBoolean(false)

        val analyzer = QrAnalyzer(scanner, busy) { raw ->
            mainExecutor.execute { if (currentActive) currentOnCode(raw) }
        }

        providerFuture.addListener({
            val provider = providerFuture.get()
            val preview = Preview.Builder().build().also { it.setSurfaceProvider(previewView.surfaceProvider) }
            val analysis = ImageAnalysis.Builder()
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .build()
                .also { it.setAnalyzer(executor, analyzer) }
            try {
                provider.unbindAll()
                provider.bindToLifecycle(lifecycleOwner, CameraSelector.DEFAULT_BACK_CAMERA, preview, analysis)
            } catch (_: Exception) {
                // no back camera – the manual option is still there
            }
        }, mainExecutor)

        onDispose {
            runCatching { providerFuture.get().unbindAll() }
            executor.shutdown()
            scanner.close()
        }
    }

    AndroidView(factory = { previewView }, modifier = modifier)
}

private class QrAnalyzer(
    private val scanner: com.google.mlkit.vision.barcode.BarcodeScanner,
    private val busy: AtomicBoolean,
    private val onCode: (String) -> Unit,
) : ImageAnalysis.Analyzer {
    @OptIn(ExperimentalGetImage::class)
    override fun analyze(proxy: ImageProxy) {
        val media = proxy.image
        if (media == null || !busy.compareAndSet(false, true)) {
            proxy.close()
            return
        }
        val input = InputImage.fromMediaImage(media, proxy.imageInfo.rotationDegrees)
        scanner.process(input)
            .addOnSuccessListener { codes -> codes.firstNotNullOfOrNull { it.rawValue }?.let(onCode) }
            .addOnCompleteListener {
                busy.set(false)
                proxy.close()
            }
    }
}

/** Bottom sheet for every pairing step: connecting, compare the number, waiting for the PC, done. */
@Composable
fun PairSheet(state: PairingState, onConfirm: () -> Unit, onCancel: () -> Unit, onDone: () -> Unit) {
    if (state == PairingState.Idle) return
    Box(Modifier.fillMaxSize().background(Color(0x99000000))) {
        Column(
            Modifier
                .align(Alignment.BottomCenter)
                .navigationBarsPadding()
                .padding(10.dp)
                .fillMaxWidth()
                .clip(RoundedCornerShape(26.dp))
                .background(Color(0xFF14161C))
                .padding(18.dp),
        ) {
            when (state) {
                is PairingState.Connecting -> {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        CircularProgressIndicator(Modifier.size(22.dp), strokeWidth = 2.5.dp, color = FlickBlue)
                        Text("  Connecting to ${state.pcName}…", color = Color.White, fontSize = 15.sp)
                    }
                    TextButton(onClick = onCancel, modifier = Modifier.align(Alignment.End)) { Text("Cancel") }
                }
                is PairingState.Confirm -> {
                    Text("${state.pcName} found", color = Color.White, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
                    Text("Encrypted pairing · same Wi-Fi", color = Muted, fontSize = 12.5.sp)
                    Text("Does this match the number on your PC?", color = Color(0xFFC9CED8), fontSize = 13.sp, modifier = Modifier.padding(top = 14.dp))
                    CheckNumber(state.checkNumber)
                    if (state.needsPcApproval) {
                        Text("Your PC will ask you to confirm it too.", color = Faint, fontSize = 12.sp, modifier = Modifier.padding(top = 8.dp))
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(top = 14.dp)) {
                        OutlinedButton(onClick = onCancel, shape = RoundedCornerShape(14.dp), modifier = Modifier.weight(1f).height(48.dp)) {
                            Text("No, cancel", color = Color.White)
                        }
                        Button(
                            onClick = onConfirm,
                            colors = ButtonDefaults.buttonColors(containerColor = FlickBlue, contentColor = Color(0xFF06121F)),
                            shape = RoundedCornerShape(14.dp),
                            modifier = Modifier.weight(1f).height(48.dp),
                        ) { Text("Yes, pair", fontWeight = FontWeight.SemiBold) }
                    }
                }
                is PairingState.WaitingForPc -> {
                    Text("Now click Yes on ${state.pcName}", color = Color.White, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
                    CheckNumber(state.checkNumber)
                    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 10.dp)) {
                        CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp, color = FlickBlue)
                        Text("  Waiting for your PC…", color = Muted, fontSize = 13.sp)
                        Spacer(Modifier.weight(1f))
                        TextButton(onClick = onCancel) { Text("Cancel") }
                    }
                }
                is PairingState.Done -> {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Filled.Done, contentDescription = null, tint = Good)
                        Text("  Paired with ${state.pcName}", color = Color.White, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
                    }
                    Text("Everything you flick between you is encrypted.", color = Muted, fontSize = 13.sp, modifier = Modifier.padding(top = 6.dp))
                    Button(
                        onClick = onDone,
                        colors = ButtonDefaults.buttonColors(containerColor = FlickBlue, contentColor = Color(0xFF06121F)),
                        shape = RoundedCornerShape(14.dp),
                        modifier = Modifier.fillMaxWidth().padding(top = 14.dp).height(48.dp),
                    ) { Text("Start flicking", fontWeight = FontWeight.SemiBold) }
                }
                is PairingState.Failed -> {
                    Text("Couldn't pair", color = Color.White, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
                    Text(state.message, color = Color(0xFFFF8A80), fontSize = 13.sp, modifier = Modifier.padding(top = 6.dp))
                    TextButton(onClick = onCancel, modifier = Modifier.align(Alignment.End)) { Text("Try again") }
                }
                PairingState.Idle -> Unit
            }
        }
    }
}

@Composable
private fun CheckNumber(number: String) {
    Text(
        number,
        color = Color.White,
        fontFamily = FontFamily.Monospace,
        fontSize = 30.sp,
        letterSpacing = 4.sp,
        textAlign = TextAlign.Center,
        modifier = Modifier
            .padding(top = 8.dp)
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(Color(0xFF0C0E12))
            .padding(vertical = 10.dp),
    )
}
