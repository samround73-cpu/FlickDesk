using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FlickDesk;

internal sealed class HistoryEntry
{
    public string Name { get; set; } = "";
    /// <summary>Full path for received files (to show in Explorer); source path for sent ones.</summary>
    public string? Path { get; set; }
    public bool Incoming { get; set; }
    public string Device { get; set; } = "";
    public long Size { get; set; }
    public DateTime Utc { get; set; } = DateTime.UtcNow;
    public bool IsClipboard { get; set; }
}

/// <summary>Last 100 transfers, persisted to %AppData%\FlickDesk\history.json. UI-thread only.</summary>
internal sealed class History
{
    private const int Max = 100;
    private static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlickDesk", "history.json");

    private readonly List<HistoryEntry> _items;

    public event Action? Changed;

    public History()
    {
        try
        {
            _items = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch (Exception) { _items = new(); }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<HistoryEntry> Items => _items;

    public IEnumerable<HistoryEntry> Today => _items.Where(i => i.Utc.ToLocalTime().Date == DateTime.Now.Date);

    public void Add(HistoryEntry entry)
    {
        _items.Insert(0, entry);
        if (_items.Count > Max) _items.RemoveRange(Max, _items.Count - Max);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items));
        }
        catch (Exception) { /* history is best-effort */ }
        Changed?.Invoke();
    }
}
