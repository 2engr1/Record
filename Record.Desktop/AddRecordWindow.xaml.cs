using System;
using System.Globalization;
using System.Windows;
using Record.Desktop.Models;

namespace Record.Desktop;

public partial class AddRecordWindow : Window
{
    public AddRecordWindow()
    {
        InitializeComponent();
        DateTextBox.Text = DateTime.Today.ToString("yyyy-MM-dd");
    }

    public RecordEntry? CreatedRecord { get; private set; }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(
                AmountTextBox.Text.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var amount) || amount <= 0)
        {
            MessageBox.Show(
                "请输入大于 0 的金额。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            AmountTextBox.Focus();
            return;
        }

        if (!DateTime.TryParseExact(
                DateTextBox.Text.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            MessageBox.Show(
                "日期格式应为：年-月-日，例如 2026-08-28。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DateTextBox.Focus();
            return;
        }

        var category = GetSelectedText(CategoryComboBox) ?? "其他";
        var paymentMethod = GetSelectedText(PaymentComboBox) ?? "其他";
        var note = NoteTextBox.Text.Trim();

        CreatedRecord = new RecordEntry
        {
            Description = string.IsNullOrWhiteSpace(note) ? category : note,
            Category = category,
            PaymentMethod = paymentMethod,
            Date = date,
            Amount = amount,
            IsIncome = IncomeRadioButton.IsChecked == true
        };

        DialogResult = true;
    }

    private static string? GetSelectedText(System.Windows.Controls.ComboBox comboBox)
    {
        return (comboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
    }
}
