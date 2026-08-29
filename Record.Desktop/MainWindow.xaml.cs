using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

    public ICollectionView RecordsView { get; private set; } = null!;

    private readonly IRecordRepository _recordRepository = new JsonRecordRepository();
    private readonly CsvRecordImporter _csvRecordImporter = new();
    private readonly List<RecordEntry> _pendingImportRecords = new();
    private RecordFilter _activeRecordFilter = RecordFilter.All;
    private string _selectedCategory = "全部类别";
    private AnalyticsRange _analyticsRange = AnalyticsRange.CurrentMonth;
    private bool _isUpdatingTableColumns;
    private double _lastHomeTableWidth;

    public MainWindow()
    {
        InitializeComponent();

        RecordsView = CollectionViewSource.GetDefaultView(RecentRecords);
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
                ImportStatusTextBlock.Text = $"已选择：{fileName}";
                ImportSelectedFileTextBlock.Text = $"{fileName} · 共识别 {importedRecords.Count} 笔";
                ImportPreviewHintTextBlock.Text = "请先核对预览内容，再导入到记录明细";
                ImportPreviewButton.Content = $"导入 {importedRecords.Count} 笔记录";
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

        foreach (var record in _pendingImportRecords)
        {
            RecentRecords.Insert(0, record);
        }

        SaveRecords();
        RecordsView.Refresh();
        RefreshSummary();

        var importedCount = _pendingImportRecords.Count;
        _pendingImportRecords.Clear();
        ImportPreviewRecords.Clear();
        ImportPreviewPanel.Visibility = Visibility.Collapsed;
        ImportStatusPanel.Visibility = Visibility.Visible;
        ImportStatusTextBlock.Text = $"已导入 {importedCount} 笔记录，可以继续选择其他账单";
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

        var monthText = $"{today:yyyy 年 M 月}";
        MonthExpenseLabelTextBlock.Text = monthText;
        MonthIncomeLabelTextBlock.Text = monthText;
        AnalyticsPeriodTextBlock.Text = $"{GetAnalyticsRangeLabel()} · {monthText}";
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
