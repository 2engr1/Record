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

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<RecordEntry>>(json, SerializerOptions)
                   ?? new List<RecordEntry>();
        }
        catch (JsonException exception)
        {
            var recoveryPath = CreateRecoveryCopy();
            throw new InvalidDataException(
                $"本地记录文件格式损坏，原文件已保留为：{Path.GetFileName(recoveryPath)}。",
                exception);
        }
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

    private string CreateRecoveryCopy()
    {
        var recoveryPath = _filePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var suffix = 1;
        while (File.Exists(recoveryPath))
        {
            recoveryPath = _filePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}-{suffix}.json";
            suffix++;
        }

        File.Copy(_filePath, recoveryPath);
        return recoveryPath;
    }
}
