package dev.flick.app.core

import android.content.Context
import dev.flick.app.EffectPreset
import dev.flick.app.FlickSettings
import dev.flick.app.Haptic
import dev.flick.app.HistoryItem
import dev.flick.app.PairedPc
import dev.flick.app.Side
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.UUID

/** Small persistence layer: identity, settings and paired PCs in SharedPreferences, history in a file. */
class Store(context: Context) {
    private val prefs = context.getSharedPreferences("flick", Context.MODE_PRIVATE)
    private val historyFile = File(context.filesDir, "history.json")

    val deviceId: String = prefs.getString("deviceId", null)
        ?: UUID.randomUUID().toString().also { prefs.edit().putString("deviceId", it).apply() }

    // ------------------------------------------------------------------ paired PCs

    fun loadPcs(): List<PairedPc> {
        val raw = prefs.getString("pcs", null) ?: return emptyList()
        return runCatching {
            val arr = JSONArray(raw)
            (0 until arr.length()).map { i ->
                val o = arr.getJSONObject(i)
                PairedPc(
                    id = o.getString("id"),
                    name = o.optString("name", "PC"),
                    host = o.getString("host"),
                    port = o.optInt("port", 47801),
                    key = o.getString("key"),
                    side = runCatching { Side.valueOf(o.optString("side")) }.getOrDefault(Side.LEFT),
                )
            }
        }.getOrDefault(emptyList())
    }

    fun savePcs(pcs: List<PairedPc>) {
        val arr = JSONArray()
        pcs.forEach {
            arr.put(JSONObject().put("id", it.id).put("name", it.name).put("host", it.host)
                .put("port", it.port).put("key", it.key).put("side", it.side.name))
        }
        prefs.edit().putString("pcs", arr.toString()).apply()
    }

    // ------------------------------------------------------------------ settings

    fun loadSettings(): FlickSettings {
        val d = FlickSettings()
        if (prefs.getInt("effectVersion", 0) < 3) {
            // the default look changed – existing installs get Spectral Surge once
            prefs.edit().putString("effect", EffectPreset.SURGE.name).putInt("effectVersion", 3).apply()
        }
        return FlickSettings(
            effect = runCatching { EffectPreset.valueOf(prefs.getString("effect", d.effect.name)!!) }.getOrDefault(d.effect),
            palette = prefs.getString("palette", d.palette) ?: d.palette,
            sensitivity = prefs.getFloat("sensitivity", d.sensitivity),
            haptic = runCatching { Haptic.valueOf(prefs.getString("haptic", d.haptic.name)!!) }.getOrDefault(d.haptic),
            backgroundReceive = prefs.getBoolean("backgroundReceive", d.backgroundReceive),
            autoCopyFromPc = prefs.getBoolean("autoCopyFromPc", d.autoCopyFromPc),
        )
    }

    fun saveSettings(s: FlickSettings) {
        prefs.edit()
            .putString("effect", s.effect.name)
            .putString("palette", s.palette)
            .putFloat("sensitivity", s.sensitivity)
            .putString("haptic", s.haptic.name)
            .putBoolean("backgroundReceive", s.backgroundReceive)
            .putBoolean("autoCopyFromPc", s.autoCopyFromPc)
            .apply()
    }

    // ------------------------------------------------------------------ history

    fun loadHistory(): List<HistoryItem> = runCatching {
        if (!historyFile.exists()) return emptyList()
        val arr = JSONArray(historyFile.readText())
        (0 until arr.length()).map { i ->
            val o = arr.getJSONObject(i)
            HistoryItem(
                name = o.getString("name"),
                uri = if (o.isNull("uri")) null else o.optString("uri"),
                mime = o.optString("mime"),
                incoming = o.optBoolean("incoming"),
                pcName = o.optString("pc"),
                size = o.optLong("size"),
                time = o.optLong("time"),
                clipboard = o.optBoolean("clip"),
            )
        }
    }.getOrDefault(emptyList())

    fun saveHistory(items: List<HistoryItem>) {
        val arr = JSONArray()
        items.take(200).forEach {
            arr.put(JSONObject().put("name", it.name).put("uri", it.uri ?: JSONObject.NULL).put("mime", it.mime)
                .put("incoming", it.incoming).put("pc", it.pcName).put("size", it.size)
                .put("time", it.time).put("clip", it.clipboard))
        }
        runCatching { historyFile.writeText(arr.toString()) }
    }
}
