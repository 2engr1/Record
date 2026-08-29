using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Record.Desktop.Models;

namespace Record.Desktop.Import;

public sealed class CsvRecordImporter
{
    private static readonly string[] DateHeaders = ["交易时间", "日期", "时间"];
    private static readonly string[] AmountHeaders = ["金额(元)", "金额（元）", "金额", "交易金额"];
    private static readonly string[] DirectionHeaders = ["收/支", "收支", "类型"];
    private static readonly string[] CategoryHeaders = ["交易分类", "交易类型", "分类"];
    private static readonly string[] DescriptionHeaders = ["商品说明", "商品", "交易对方", "备注", "说明"];
    private static readonly string[] PaymentHeaders = ["支付方式", "付款方式", "资金渠道"];

    public IReadOnlyList<RecordEntry> Import(string filePath)
    {
        var rows = ReadRows(filePath);
        if (rows.Count == 0)
        {
            throw new InvalidDataException("文件中没有可读取的内容。");
        }

        var headerIndex = rows.FindIndex(row =>
            HasHeader(row, DateHeaders) && HasHeader(row, AmountHeaders));
        if (headerIndex < 0)
        {
            throw new InvalidDataException("没有找到包含日期和金额的账单表头。");
        }

        var headers = rows[headerIndex]
            .Select(NormalizeHeader)
            .ToArray();
        var result = new List<RecordEntry>();

        for (var rowIndex = headerIndex + 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var values = CreateValueMap(headers, row);
            var dateText = Pick(values, DateHeaders);
            var amountText = Pick(values, AmountHeaders);

            if (!TryParseDate(dateText, out var date) || !TryParseAmount(amountText, out var amount))
            {
                continue;
            }

            var directionText = Pick(values, DirectionHeaders);
            var isIncome = IsIncome(directionText, amountText);
            result.Add(new RecordEntry
            {
                Description = Pick(values, DescriptionHeaders) is { Length: > 0 } description
                    ? description
                    : "未命名记录",
                Category = Pick(values, CategoryHeaders) is { Length: > 0 } category
                    ? category
                    : "其他",
                PaymentMethod = Pick(values, PaymentHeaders) is { Length: > 0 } payment
                    ? payment
                    : "其他",
                Date = date,
                Amount = amount,
                IsIncome = isIncome
            });
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("没有找到可导入的账单记录，请检查日期和金额列。");
        }

        return result;
    }

    private static List<string[]> ReadRows(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var text = Decode(bytes);
        var rows = new List<string[]>();
        var delimiter = DetectDelimiter(text);
        var currentRow = new List<string>();
        var currentValue = new StringBuilder();
        var insideQuotes = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (insideQuotes && index + 1 < text.Length && text[index + 1] == '"')
                {
                    currentValue.Append('"');
                    index++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }

                continue;
            }

            if (!insideQuotes && character == delimiter)
            {
                currentRow.Add(currentValue.ToString().Trim());
                currentValue.Clear();
                continue;
            }

            if (!insideQuotes && (character == '\r' || character == '\n'))
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                currentRow.Add(currentValue.ToString().Trim());
                currentValue.Clear();
                if (currentRow.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    rows.Add(currentRow.ToArray());
                }

                currentRow.Clear();
                continue;
            }

            currentValue.Append(character);
        }

        currentRow.Add(currentValue.ToString().Trim());
        if (currentRow.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            rows.Add(currentRow.ToArray());
        }

        return rows;
    }

    private static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Default.GetString(bytes);
        }
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return firstLine.Count(character => character == '\t') > firstLine.Count(character => character == ',')
            ? '\t'
            : ',';
    }

    private static Dictionary<string, string> CreateValueMap(string[] headers, string[] values)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Length; index++)
        {
            map[headers[index]] = index < values.Length ? values[index].Trim() : string.Empty;
        }

        return map;
    }

    private static bool HasHeader(string[] row, IEnumerable<string> candidates)
    {
        var normalized = row.Select(NormalizeHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.Any(candidate => normalized.Contains(NormalizeHeader(candidate)));
    }

    private static string Pick(Dictionary<string, string> values, IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (values.TryGetValue(NormalizeHeader(candidate), out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string NormalizeHeader(string value)
    {
        return value.Trim().Trim('\uFEFF').Replace(" ", string.Empty);
    }

    private static bool TryParseDate(string value, out DateTime date)
    {
        var normalized = value.Trim().Replace('/', '-').Replace('年', '-').Replace('月', '-').Replace("日", string.Empty);
        var formats = new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd", "yyyy-M-d", "M-d-yyyy" };
        return DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)
               || DateTime.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date);
    }

    private static bool TryParseAmount(string value, out decimal amount)
    {
        var normalized = value.Trim()
            .Replace(",", string.Empty)
            .Replace("¥", string.Empty)
            .Replace("￥", string.Empty)
            .Trim();
        return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount)
               && (amount = Math.Abs(amount)) > 0;
    }

    private static bool IsIncome(string direction, string amount)
    {
        if (direction.Contains("收入", StringComparison.OrdinalIgnoreCase)
            || direction.Contains("转入", StringComparison.OrdinalIgnoreCase)
            || direction.Contains("退款", StringComparison.OrdinalIgnoreCase)
            || direction.Contains("入账", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return direction.Contains("支出", StringComparison.OrdinalIgnoreCase) is false
               && amount.TrimStart().StartsWith('+');
    }
}
