using System.Collections.Generic;
using Record.Desktop.Models;

namespace Record.Desktop.Data;

public interface IRecordRepository
{
    IReadOnlyList<RecordEntry> Load();

    void Save(IEnumerable<RecordEntry> records);
}
