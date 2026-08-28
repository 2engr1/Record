using System;
using System.Windows.Media;

namespace Record.Desktop.Models;

public sealed class RecordEntry
{
    private static readonly Brush ExpenseBrush = CreateBrush("#E7775E");
    private static readonly Brush IncomeBrush = CreateBrush("#758B4E");

    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public decimal Amount { get; init; }
    public bool IsIncome { get; init; }

    public string DisplayDate => Date.Date == DateTime.Today
        ? "今天"
        : Date.Date == DateTime.Today.AddDays(-1)
            ? "昨天"
            : Date.ToString("MM-dd");

    public string DisplayAmount => $"{(IsIncome ? "+" : "-")} ¥ {Amount:N2}";

    public Brush AmountBrush => IsIncome ? IncomeBrush : ExpenseBrush;

    private static Brush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
