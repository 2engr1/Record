using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using Record.Desktop.Data;
using Record.Desktop.Import;
using Record.Desktop.Models;

namespace Record.Desktop;

public partial class MainWindow : Window
{
    private enum AppPage
    {
        Home,
        Records,
        Import,
        Analytics
    }

    private enum RecordFilter
    {
        All,
        CurrentMonth,
        Expense,
        Income
    }

    private enum AnalyticsRange
    {
        CurrentMonth,
        LastThreeMonths,
        CurrentYear
    }

    public ObservableCollection<RecordEntry> RecentRecords { get; } = new();

    public ObservableCollection<RecordEntry> ImportPreviewRecords { get; } = new();

    public ICollectionView RecentRecordsView { get; private set; } = null!;

    public ICollectionView RecordsView { get; private set; } = null!;

    private readonly IRecordRepository _recordRepository = new JsonRecordRepository();
    private readonly CsvRecordImporter _csvRecordImporter = new();
    private readonly List<RecordEntry> _pendingImportRecords = new();
    private RecordFilter _activeRecordFilter = RecordFilter.All;
    private string _selectedCategory = "全部类别";
    private string _recordSearchText = string.Empty;
    private AnalyticsRange _analyticsRange = AnalyticsRange.CurrentMonth;
    private bool _isUpdatingTableColumns;
    private double _lastHomeTableWidth;

    public MainWindow()
    {
        InitializeComponent();

        RecentRecordsView = new ListCollectionView(RecentRecords);
        RecentRecordsView.SortDescriptions.Add(
            new SortDescription(nameof(RecordEntry.Date), ListSortDirection.Descending));

        RecordsView = CollectionViewSource.GetDefaultView(RecentRecords);
        RecordsView.SortDescriptions.Add(
            new SortDescription(nameof(RecordEntry.Date), ListSortDirection.Descending));
        RecordsView.Filter = FilterRecord;

        DataContext = this;
        LoadRecords();
        RefreshSummary();
    }

    private void AddRecordButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new AddRecordWindow
        {
            Owner = this
        };

        if (window.ShowDialog() == true && window.CreatedRecord is { } record)
        {
            RecentRecords.Insert(0, record);
            SaveRecords();
            RecordsView.Refresh();
            RefreshSummary();
        }
    }

    private void HomeNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(AppPage.Home);
    }

    private void RecordsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(AppPage.Records);
    }

    private void ImportNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(AppPage.Import);
    }

    private void AnalyticsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(AppPage.Analytics);
    }

    private void AnalyticsCurrentMonthButton_Click(object sender, RoutedEventArgs e)
    {
        SetAnalyticsRange(AnalyticsRange.CurrentMonth);
    }

    private void AnalyticsLastThreeMonthsButton_Click(object sender, RoutedEventArgs e)
    {
        SetAnalyticsRange(AnalyticsRange.LastThreeMonths);
    }

    private void AnalyticsCurrentYearButton_Click(object sender, RoutedEventArgs e)
    {
        SetAnalyticsRange(AnalyticsRange.CurrentYear);
    }

    private void ImportSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var sourceName = (sender as Button)?.Tag?.ToString() ?? "账单";
        SelectImportFile($"选择{sourceName}账单文件", "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*");
    }

    private void ManualImportButton_Click(object sender, RoutedEventArgs e)
    {
        SelectImportFile(
            "选择要导入的 CSV 账单文件",
            "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*");
    }

    private void ExportRecordsButton_Click(object sender, RoutedEventArgs e)
    {
        if (RecentRecords.Count == 0)
        {
            ImportStatusPanel.Visibility = Visibility.Visible;
            ImportStatusTextBlock.Text = "当前没有可导出的记录";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出 Record 账单备份",
            FileName = $"Record账单_{DateTime.Now:yyyyMMdd_HHmm}",
            Filter = "CSV 文件 (*.csv)|*.csv",
            AddExtension = true,
            DefaultExt = ".csv",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var lines = new List<string>
            {
                "日期,说明,分类,支付方式,金额,收支"
            };
            lines.AddRange(RecentRecords
                .OrderByDescending(record => record.Date)
                .Select(record => string.Join(
                    ",",
                    EscapeCsv(record.Date.ToString("yyyy-MM-dd HH:mm:ss")),
                    EscapeCsv(record.Description),
                    EscapeCsv(record.Category),
                    EscapeCsv(record.PaymentMethod),
                    EscapeCsv((record.IsIncome ? record.Amount : -record.Amount).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)),
                    EscapeCsv(record.IsIncome ? "收入" : "支出"))));

            File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            ImportPreviewPanel.Visibility = Visibility.Collapsed;
            ImportStatusPanel.Visibility = Visibility.Visible;
            ImportStatusTextBlock.Text = $"已导出 {RecentRecords.Count} 笔记录：{Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception exception)
        {
            ImportStatusPanel.Visibility = Visibility.Visible;
            ImportStatusTextBlock.Text = $"账单导出失败：{exception.Message}";
        }
    }

    private static string EscapeCsv(string value)
    {
        var normalized = value.Replace("\"", "\"\"");
        return normalized.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{normalized}\""
            : normalized;
    }

    private void SelectImportFile(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                var importedRecords = _csvRecordImporter.Import(dialog.FileName);
                _pendingImportRecords.Clear();
                _pendingImportRecords.AddRange(importedRecords);
                ImportPreviewRecords.Clear();
                foreach (var record in importedRecords.Take(5))
                {
                    ImportPreviewRecords.Add(record);
                }

                var fileName = Path.GetFileName(dialog.FileName);
                var duplicateCount = CountDuplicateImportRecords(importedRecords);
                var importableCount = importedRecords.Count - duplicateCount;
                ImportStatusTextBlock.Text = $"已选择：{fileName}";
                ImportSelectedFileTextBlock.Text = $"{fileName} · 共识别 {importedRecords.Count} 笔";
                ImportPreviewHintTextBlock.Text = duplicateCount > 0
                    ? $"已发现 {duplicateCount} 笔重复记录，导入时会自动跳过"
                    : "请先核对预览内容，再导入到记录明细";
                ImportPreviewButton.Content = importableCount > 0
                    ? $"导入 {importableCount} 笔新记录"
                    : "全部记录已存在";
                ImportPreviewButton.IsEnabled = importableCount > 0;
                ImportStatusPanel.Visibility = Visibility.Collapsed;
                ImportPreviewPanel.Visibility = Visibility.Visible;
            }
            catch (Exception exception)
            {
                ImportPreviewRecords.Clear();
                _pendingImportRecords.Clear();
                ImportPreviewPanel.Visibility = Visibility.Collapsed;
                ImportStatusPanel.Visibility = Visibility.Visible;
                ImportStatusTextBlock.Text = $"账单读取失败：{exception.Message}";
            }
        }
    }

    private void ImportPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingImportRecords.Count == 0)
        {
            return;
        }

        var knownKeys = RecentRecords
            .Select(GetRecordKey)
            .ToHashSet(StringComparer.Ordinal);
        var importedCount = 0;
        var duplicateCount = 0;

        foreach (var record in _pendingImportRecords.OrderBy(record => record.Date))
        {
            if (!knownKeys.Add(GetRecordKey(record)))
            {
                duplicateCount++;
                continue;
            }

            RecentRecords.Insert(0, record);
            importedCount++;
        }

        if (importedCount > 0)
        {
            SaveRecords();
        }

        RecordsView.Refresh();
        RefreshSummary();

        _pendingImportRecords.Clear();
        ImportPreviewRecords.Clear();
        ImportPreviewPanel.Visibility = Visibility.Collapsed;
        ImportStatusPanel.Visibility = Visibility.Visible;
        ImportPreviewButton.IsEnabled = true;
        ImportStatusTextBlock.Text = duplicateCount > 0
            ? $"已导入 {importedCount} 笔记录，跳过 {duplicateCount} 笔重复记录，可以继续选择其他账单"
            : $"已导入 {importedCount} 笔记录，可以继续选择其他账单";
    }

    private int CountDuplicateImportRecords(IEnumerable<RecordEntry> records)
    {
        var knownKeys = RecentRecords
            .Select(GetRecordKey)
            .ToHashSet(StringComparer.Ordinal);
        var duplicateCount = 0;

        foreach (var record in records)
        {
            if (!knownKeys.Add(GetRecordKey(record)))
            {
                duplicateCount++;
            }
        }

        return duplicateCount;
    }

    private static string GetRecordKey(RecordEntry record)
    {
        return string.Join(
            "\u001F",
            record.Date.Ticks,
            record.Amount.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture),
            record.IsIncome ? "income" : "expense",
            record.Category.Trim(),
            record.PaymentMethod.Trim(),
            record.Description.Trim());
    }

    private void AllRecordsFilterButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedCategory = "全部类别";
        SetRecordFilter(RecordFilter.All);
        UpdateCategoryFilterStyles();
    }

    private void CurrentMonthFilterButton_Click(object sender, RoutedEventArgs e)
    {
        SetRecordFilter(RecordFilter.CurrentMonth);
    }

    private void ExpenseFilterButton_Click(object sender, RoutedEventArgs e)
    {
        SetRecordFilter(RecordFilter.Expense);
    }

    private void IncomeFilterButton_Click(object sender, RoutedEventArgs e)
    {
        SetRecordFilter(RecordFilter.Income);
    }

    private void RecordSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _recordSearchText = RecordSearchTextBox.Text.Trim();
        RecordSearchPlaceholderTextBlock.Visibility = string.IsNullOrEmpty(_recordSearchText)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecordsView.Refresh();
        RefreshSummary();
    }

    private void CategoryFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string category)
        {
            _selectedCategory = category;
            RecordsView.Refresh();
            RefreshSummary();
            UpdateCategoryFilterStyles();
        }
    }

    private void DeleteRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RecordEntry record })
        {
            return;
        }

        var result = MessageBox.Show(
            $"确定删除“{record.Description}”这笔记录吗？",
            "删除记录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        RecentRecords.Remove(record);
        SaveRecords();
        RecordsView.Refresh();
        RefreshSummary();
    }

    private void EditRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RecordEntry record })
        {
            return;
        }

        var window = new AddRecordWindow(record)
        {
            Owner = this
        };
        if (window.ShowDialog() != true || window.CreatedRecord is not { } updatedRecord)
        {
            return;
        }

        var index = RecentRecords.IndexOf(record);
        if (index < 0)
        {
            return;
        }

        RecentRecords[index] = updatedRecord;
        SaveRecords();
        RecordsView.Refresh();
        RefreshSummary();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateTableColumnWidths(HomeRecordsListView);
    }

    private void RecordsListView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ListView listView)
        {
            UpdateTableColumnWidths(listView);
        }
    }

    private void LoadPreviewRecords()
    {
        var today = DateTime.Today;

        RecentRecords.Add(new RecordEntry
        {
            Description = "午餐 · 拉面",
            Category = "餐饮",
            PaymentMethod = "微信支付",
            Date = today,
            Amount = 28m
        });

        RecentRecords.Add(new RecordEntry
        {
            Description = "地铁通勤",
            Category = "交通",
            PaymentMethod = "支付宝",
            Date = today.AddDays(-1),
            Amount = 6m
        });

        RecentRecords.Add(new RecordEntry
        {
            Description = "八月工资",
            Category = "工资",
            PaymentMethod = "银行卡",
            Date = new DateTime(today.Year, today.Month, 1),
            Amount = 18000m,
            IsIncome = true
        });
    }

    private void LoadRecords()
    {
        try
        {
            var savedRecords = _recordRepository.Load();
            if (savedRecords.Count > 0)
            {
                foreach (var record in savedRecords)
                {
                    RecentRecords.Add(record);
                }

                return;
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"本地记录读取失败，将暂时显示示例数据。\n\n{exception.Message}",
                "Record",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        LoadPreviewRecords();
    }

    private void SaveRecords()
    {
        try
        {
            _recordRepository.Save(RecentRecords);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"记录保存失败，请检查本地存储权限。\n\n{exception.Message}",
                "Record",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RefreshSummary()
    {
        var today = DateTime.Today;
        var monthRecords = RecentRecords.Where(record =>
            record.Date.Year == today.Year && record.Date.Month == today.Month);
        var analyticsStart = GetAnalyticsStartDate(today);
        var analyticsRecords = RecentRecords.Where(record => record.Date >= analyticsStart && record.Date <= today);

        var totalExpense = RecentRecords
            .Where(record => !record.IsIncome)
            .Sum(record => record.Amount);
        var totalIncome = RecentRecords
            .Where(record => record.IsIncome)
            .Sum(record => record.Amount);
        var monthExpense = monthRecords
            .Where(record => !record.IsIncome)
            .Sum(record => record.Amount);
        var monthIncome = monthRecords
            .Where(record => record.IsIncome)
            .Sum(record => record.Amount);
        var analyticsExpense = analyticsRecords
            .Where(record => !record.IsIncome)
            .Sum(record => record.Amount);
        var analyticsIncome = analyticsRecords
            .Where(record => record.IsIncome)
            .Sum(record => record.Amount);
        var analyticsBalance = analyticsIncome - analyticsExpense;
        var analyticsRecordList = analyticsRecords.ToList();

        var visibleRecords = RecentRecords
            .Where(record => IsRecordVisible(record))
            .ToList();

        TotalExpenseTextBlock.Text = $"¥ {totalExpense:N2}";
        TotalIncomeTextBlock.Text = $"¥ {totalIncome:N2}";
        MonthExpenseTextBlock.Text = $"¥ {monthExpense:N2}";
        MonthIncomeTextBlock.Text = $"¥ {monthIncome:N2}";
        AnalyticsExpenseTextBlock.Text = $"¥ {analyticsExpense:N2}";
        AnalyticsIncomeTextBlock.Text = $"¥ {analyticsIncome:N2}";
        AnalyticsBalanceTextBlock.Text = $"¥ {analyticsBalance:N2}";
        AnalyticsBalanceTextBlock.Foreground = analyticsBalance >= 0
            ? (System.Windows.Media.Brush)FindResource("GreenBrush")
            : (System.Windows.Media.Brush)FindResource("CoralBrush");

        RecordsCountTextBlock.Text = $"{visibleRecords.Count} 笔";
        RecordsExpenseTextBlock.Text = $"¥ {visibleRecords
            .Where(record => !record.IsIncome)
            .Sum(record => record.Amount):N2}";
        RecordsIncomeTextBlock.Text = $"¥ {visibleRecords
            .Where(record => record.IsIncome)
            .Sum(record => record.Amount):N2}";

        HomeEmptyStatePanel.Visibility = RecentRecords.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecordsEmptyStatePanel.Visibility = visibleRecords.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecordsEmptyTitleTextBlock.Text = visibleRecords.Count == 0 && RecentRecords.Count > 0
            ? "没有匹配的记录"
            : "还没有记账记录";
        RecordsEmptyHintTextBlock.Text = visibleRecords.Count == 0 && RecentRecords.Count > 0
            ? "试试调整筛选条件或搜索关键词"
            : "从右上角记下一笔，让生活轨迹慢慢长出来";

        var monthText = $"{today:yyyy 年 M 月}";
        MonthExpenseLabelTextBlock.Text = monthText;
        MonthIncomeLabelTextBlock.Text = monthText;
        AnalyticsPeriodTextBlock.Text = $"{GetAnalyticsRangeLabel()} · {monthText}";
        UpdateAnalyticsTrend(analyticsRecordList, analyticsStart, today);
        UpdateAnalyticsCategories(analyticsRecordList);
        UpdateAnalyticsOverview(analyticsExpense, analyticsIncome);
    }

    private void ShowPage(AppPage page)
    {
        HomePage.Visibility = page == AppPage.Home ? Visibility.Visible : Visibility.Collapsed;
        RecordsPage.Visibility = page == AppPage.Records ? Visibility.Visible : Visibility.Collapsed;
        ImportPage.Visibility = page == AppPage.Import ? Visibility.Visible : Visibility.Collapsed;
        AnalyticsPage.Visibility = page == AppPage.Analytics ? Visibility.Visible : Visibility.Collapsed;

        HomeNavButton.Style = (Style)FindResource(
            page == AppPage.Home ? "ActiveNavButtonStyle" : "NavButtonStyle");
        RecordsNavButton.Style = (Style)FindResource(
            page == AppPage.Records ? "ActiveNavButtonStyle" : "NavButtonStyle");
        ImportNavButton.Style = (Style)FindResource(
            page == AppPage.Import ? "ActiveNavButtonStyle" : "NavButtonStyle");
        AnalyticsNavButton.Style = (Style)FindResource(
            page == AppPage.Analytics ? "ActiveNavButtonStyle" : "NavButtonStyle");
    }

    private void UpdateTableColumnWidths(ListView listView)
    {
        if (_isUpdatingTableColumns)
        {
            return;
        }

        var currentWidth = listView.ActualWidth;
        if (currentWidth <= 0)
        {
            return;
        }

        if (listView != HomeRecordsListView)
        {
            return;
        }

        if (listView == HomeRecordsListView)
        {
            if (Math.Abs(currentWidth - _lastHomeTableWidth) < 0.5)
            {
                return;
            }

            _lastHomeTableWidth = currentWidth;
        }

        _isUpdatingTableColumns = true;

        try
        {
            var availableWidth = Math.Max(0, currentWidth - 18);
            HomeDescriptionColumn.Width = availableWidth * 0.52;
            HomeCategoryColumn.Width = availableWidth * 0.20;
            HomeDateColumn.Width = availableWidth * 0.13;
            HomeAmountColumn.Width = availableWidth * 0.15;
        }
        finally
        {
            _isUpdatingTableColumns = false;
        }
    }

    private bool FilterRecord(object item)
    {
        return item is RecordEntry record && IsRecordVisible(record);
    }

    private bool IsRecordVisible(RecordEntry record)
    {
        if (_selectedCategory != "全部类别" && record.Category != _selectedCategory)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_recordSearchText)
            && !record.Description.Contains(_recordSearchText, StringComparison.OrdinalIgnoreCase)
            && !record.PaymentMethod.Contains(_recordSearchText, StringComparison.OrdinalIgnoreCase)
            && !record.Category.Contains(_recordSearchText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _activeRecordFilter switch
        {
            RecordFilter.CurrentMonth => record.Date.Year == DateTime.Today.Year &&
                                          record.Date.Month == DateTime.Today.Month,
            RecordFilter.Expense => !record.IsIncome,
            RecordFilter.Income => record.IsIncome,
            _ => true
        };
    }

    private DateTime GetAnalyticsStartDate(DateTime today)
    {
        return _analyticsRange switch
        {
            AnalyticsRange.LastThreeMonths => new DateTime(today.Year, today.Month, 1).AddMonths(-2),
            AnalyticsRange.CurrentYear => new DateTime(today.Year, 1, 1),
            _ => new DateTime(today.Year, today.Month, 1)
        };
    }

    private string GetAnalyticsRangeLabel()
    {
        return _analyticsRange switch
        {
            AnalyticsRange.LastThreeMonths => "近三月",
            AnalyticsRange.CurrentYear => "今年",
            _ => "本月"
        };
    }

    private void UpdateAnalyticsTrend(
        IReadOnlyList<RecordEntry> records,
        DateTime startDate,
        DateTime endDate)
    {
        var totalDays = Math.Max(1, (endDate.Date - startDate.Date).Days + 1);
        var bucketDays = Math.Max(1, (int)Math.Ceiling(totalDays / 7d));
        var expenseValues = new decimal[7];
        var incomeValues = new decimal[7];

        foreach (var record in records)
        {
            var bucket = Math.Min(6, Math.Max(0, (record.Date.Date - startDate.Date).Days / bucketDays));
            if (record.IsIncome)
            {
                incomeValues[bucket] += record.Amount;
            }
            else
            {
                expenseValues[bucket] += record.Amount;
            }
        }

        var maximum = Math.Max(expenseValues.Concat(incomeValues).DefaultIfEmpty(0m).Max(), 1m);
        var expensePoints = CreateChartPoints(expenseValues, maximum);
        var incomePoints = CreateChartPoints(incomeValues, maximum);
        AnalyticsExpenseLine.Points = expensePoints;
        AnalyticsIncomeLine.Points = incomePoints;
        Canvas.SetLeft(AnalyticsExpenseEndPoint, expensePoints[^1].X - 5);
        Canvas.SetTop(AnalyticsExpenseEndPoint, expensePoints[^1].Y - 5);
        Canvas.SetLeft(AnalyticsIncomeEndPoint, incomePoints[^1].X - 5);
        Canvas.SetTop(AnalyticsIncomeEndPoint, incomePoints[^1].Y - 5);

        AnalyticsStartDateTextBlock.Text = startDate.ToString("MM-dd");
        AnalyticsMiddleDateTextBlock.Text = startDate.AddDays(totalDays / 2).ToString("MM-dd");
        AnalyticsEndDateTextBlock.Text = endDate.ToString("MM-dd");
    }

    private static PointCollection CreateChartPoints(decimal[] values, decimal maximum)
    {
        var points = new PointCollection();
        for (var index = 0; index < values.Length; index++)
        {
            var x = 28 + index * (452d / 6d);
            var y = 118 - (double)(values[index] / maximum) * 82;
            points.Add(new Point(x, y));
        }

        return points;
    }

    private void UpdateAnalyticsCategories(IReadOnlyList<RecordEntry> records)
    {
        var groups = records
            .Where(record => !record.IsIncome)
            .GroupBy(record => record.Category)
            .Select(group => new { Category = group.Key, Amount = group.Sum(record => record.Amount) })
            .OrderByDescending(group => group.Amount)
            .ToList();
        var total = groups.Sum(group => group.Amount);
        var rows = groups.Take(3)
            .Select(group => (group.Category, group.Amount))
            .ToList();
        var remaining = groups.Skip(3).Sum(group => group.Amount);

        // 空数据时仍保留稳定的四行视觉结构，避免统计页在首次使用时越界。
        var defaultCategories = new[] { "餐饮", "交通", "购物" };
        while (rows.Count < 3)
        {
            rows.Add((defaultCategories[rows.Count], 0m));
        }

        rows.Add(("其他", remaining));

        var maxAmount = Math.Max(rows.Max(row => row.Amount), 1m);
        SetAnalyticsCategoryRow(AnalyticsCategory1TextBlock, AnalyticsCategory1Bar, AnalyticsCategory1PercentTextBlock, rows[0], total, maxAmount);
        SetAnalyticsCategoryRow(AnalyticsCategory2TextBlock, AnalyticsCategory2Bar, AnalyticsCategory2PercentTextBlock, rows[1], total, maxAmount);
        SetAnalyticsCategoryRow(AnalyticsCategory3TextBlock, AnalyticsCategory3Bar, AnalyticsCategory3PercentTextBlock, rows[2], total, maxAmount);
        SetAnalyticsCategoryRow(AnalyticsCategory4TextBlock, AnalyticsCategory4Bar, AnalyticsCategory4PercentTextBlock, rows[3], total, maxAmount);
    }

    private static void SetAnalyticsCategoryRow(
        TextBlock label,
        Border bar,
        TextBlock percentage,
        (string Category, decimal Amount) row,
        decimal total,
        decimal maximum)
    {
        label.Text = row.Category;
        bar.Width = (double)(row.Amount / maximum) * 112;
        percentage.Text = total <= 0 ? "0%" : $"{row.Amount / total:P0}";
    }

    private void UpdateAnalyticsOverview(decimal expense, decimal income)
    {
        AnalyticsOverviewTextBlock.Text = $"支出 ¥ {expense:N2} · 收入 ¥ {income:N2}";

        if (income <= 0)
        {
            AnalyticsOverviewBar.Width = 0;
            AnalyticsOverviewPercentTextBlock.Text = expense <= 0 ? "暂无数据" : "暂无收入";
            AnalyticsOverviewPercentTextBlock.Foreground = (Brush)FindResource("MutedBrush");
            return;
        }

        var coverage = Math.Clamp((double)(expense / income), 0, 1);
        AnalyticsOverviewBar.Width = 210 * coverage;
        AnalyticsOverviewPercentTextBlock.Text = expense > income
            ? "支出超出收入"
            : $"支出 {expense / income:P0}";
        AnalyticsOverviewPercentTextBlock.Foreground = expense > income
            ? (Brush)FindResource("CoralBrush")
            : (Brush)FindResource("GreenBrush");
        AnalyticsOverviewBar.Background = expense > income
            ? (Brush)FindResource("CoralBrush")
            : (Brush)FindResource("GreenBrush");
    }

    private void SetAnalyticsRange(AnalyticsRange range)
    {
        _analyticsRange = range;
        RefreshSummary();

        AnalyticsCurrentMonthButton.Style = (Style)FindResource(
            range == AnalyticsRange.CurrentMonth ? "ActiveFilterChipStyle" : "FilterChipStyle");
        AnalyticsLastThreeMonthsButton.Style = (Style)FindResource(
            range == AnalyticsRange.LastThreeMonths ? "ActiveFilterChipStyle" : "FilterChipStyle");
        AnalyticsCurrentYearButton.Style = (Style)FindResource(
            range == AnalyticsRange.CurrentYear ? "ActiveFilterChipStyle" : "FilterChipStyle");
    }

    private void SetRecordFilter(RecordFilter filter)
    {
        _activeRecordFilter = filter;
        RecordsView.Refresh();
        RefreshSummary();

        AllRecordsFilterButton.Style = (Style)FindResource(
            filter == RecordFilter.All ? "ActiveFilterChipStyle" : "FilterChipStyle");
        CurrentMonthFilterButton.Style = (Style)FindResource(
            filter == RecordFilter.CurrentMonth ? "ActiveFilterChipStyle" : "FilterChipStyle");
        ExpenseFilterButton.Style = (Style)FindResource(
            filter == RecordFilter.Expense ? "ActiveFilterChipStyle" : "FilterChipStyle");
        IncomeFilterButton.Style = (Style)FindResource(
            filter == RecordFilter.Income ? "ActiveFilterChipStyle" : "FilterChipStyle");
    }

    private void UpdateCategoryFilterStyles()
    {
        var activeStyle = (Style)FindResource("ActiveFilterChipStyle");
        var inactiveStyle = (Style)FindResource("FilterChipStyle");

        CategoryAllFilterButton.Style = _selectedCategory == "全部类别" ? activeStyle : inactiveStyle;
        CategoryFoodFilterButton.Style = _selectedCategory == "餐饮" ? activeStyle : inactiveStyle;
        CategoryTransportFilterButton.Style = _selectedCategory == "交通" ? activeStyle : inactiveStyle;
        CategoryShoppingFilterButton.Style = _selectedCategory == "购物" ? activeStyle : inactiveStyle;
        CategorySalaryFilterButton.Style = _selectedCategory == "工资" ? activeStyle : inactiveStyle;
        CategoryOtherFilterButton.Style = _selectedCategory == "其他" ? activeStyle : inactiveStyle;
    }
}
