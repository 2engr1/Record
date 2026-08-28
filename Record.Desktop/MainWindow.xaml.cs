using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Record.Desktop.Models;

namespace Record.Desktop;

public partial class MainWindow : Window
{
    private enum RecordFilter
    {
        All,
        CurrentMonth,
        Expense,
        Income
    }

    public ObservableCollection<RecordEntry> RecentRecords { get; } = new();

    public ICollectionView RecordsView { get; private set; } = null!;

    private RecordFilter _activeRecordFilter = RecordFilter.All;
    private bool _isUpdatingTableColumns;
    private double _lastHomeTableWidth;

    public MainWindow()
    {
        InitializeComponent();

        RecordsView = CollectionViewSource.GetDefaultView(RecentRecords);
        RecordsView.Filter = FilterRecord;

        DataContext = this;
        LoadPreviewRecords();
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
            RecordsView.Refresh();
            RefreshSummary();
        }
    }

    private void HomeNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(showRecords: false);
    }

    private void RecordsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(showRecords: true);
    }

    private void AllRecordsFilterButton_Click(object sender, RoutedEventArgs e)
    {
        SetRecordFilter(RecordFilter.All);
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

    private void RefreshSummary()
    {
        var today = DateTime.Today;
        var monthRecords = RecentRecords.Where(record =>
            record.Date.Year == today.Year && record.Date.Month == today.Month);

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

        var visibleRecords = RecentRecords
            .Where(record => IsRecordVisible(record))
            .ToList();

        TotalExpenseTextBlock.Text = $"¥ {totalExpense:N2}";
        TotalIncomeTextBlock.Text = $"¥ {totalIncome:N2}";
        MonthExpenseTextBlock.Text = $"¥ {monthExpense:N2}";
        MonthIncomeTextBlock.Text = $"¥ {monthIncome:N2}";

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
    }

    private void ShowPage(bool showRecords)
    {
        HomePage.Visibility = showRecords ? Visibility.Collapsed : Visibility.Visible;
        RecordsPage.Visibility = showRecords ? Visibility.Visible : Visibility.Collapsed;

        HomeNavButton.Style = (Style)FindResource(
            showRecords ? "NavButtonStyle" : "ActiveNavButtonStyle");
        RecordsNavButton.Style = (Style)FindResource(
            showRecords ? "ActiveNavButtonStyle" : "NavButtonStyle");
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
        return _activeRecordFilter switch
        {
            RecordFilter.CurrentMonth => record.Date.Year == DateTime.Today.Year &&
                                          record.Date.Month == DateTime.Today.Month,
            RecordFilter.Expense => !record.IsIncome,
            RecordFilter.Income => record.IsIncome,
            _ => true
        };
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
}
