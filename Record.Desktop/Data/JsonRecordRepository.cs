using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Record.Desktop.Models;

namespace Record.Desktop.Data;

public sealed class JsonRecordRepository : IRecordRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;

    public JsonRecordRepository()
    {
        var applicationDataDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        _filePath = Path.Combine(applicationDataDirectory, "Record", "records.json");
    }

    public IReadOnlyList<RecordEntry> Load()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<RecordEntry>();
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<List<RecordEntry>>(json, SerializerOptions)
               ?? new List<RecordEntry>();
    }

    public void Save(IEnumerable<RecordEntry> records)
    {
        var directory = Path.GetDirectoryName(_filePath)
                        ?? throw new InvalidOperationException("无法确定本地数据目录。");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(records, SerializerOptions);
        var temporaryFilePath = _filePath + ".tmp";
        File.WriteAllText(temporaryFilePath, json);
        File.Move(temporaryFilePath, _filePath, overwrite: true);
    }
}
