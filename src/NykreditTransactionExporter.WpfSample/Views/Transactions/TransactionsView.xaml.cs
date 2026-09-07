using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NykreditTransactionExporter.WpfSample.Api.Models;
using NykreditTransactionExporter.WpfSample.Api.Models.Data;
using NykreditTransactionExporter.WpfSample.Formatting;
using NykreditTransactionExporter.WpfSample.Services;

namespace NykreditTransactionExporter.WpfSample.Views.Transactions;

public partial class TransactionsView : UserControl
{
    #region Readonly Fields
    private readonly AppWorkspace _workspace;
    #endregion

    #region Create transactions view
    internal TransactionsView(AppWorkspace workspace)
    {
        InitializeComponent();
        _workspace = workspace;
        _workspace.StateChanged += Workspace_StateChanged;
        AccountComboBox.DisplayMemberPath = nameof(AccountModel.DisplayName);
        DateToPicker.SelectedDate = DateTime.Today;
        DateFromPicker.SelectedDate = DateTime.Today.AddDays(-30);
        ExportMonthTextBox.Text = DateTime.Today.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        UpdateAccounts();
    }
    #endregion

    #region Handle workspace state change
    private void Workspace_StateChanged(object? sender, EventArgs e)
    {
        UpdateAccounts();
    }
    #endregion


    #region Handle account selection
    private void AccountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TransactionsDataGrid.ItemsSource = null;
        TransactionCountTextBlock.Text = "0 transactions";
        TransactionJsonTextBox.Clear();
        SelectedTransactionTextBlock.Text = "Select a transaction to inspect its raw payload.";
        LoadTransactionDetailsButton.IsEnabled = false;
    }
    #endregion

    #region Load transactions
    private async void LoadTransactionsButton_Click(object sender, RoutedEventArgs e)
    {
        AccountModel? account = GetSelectedAccount();
        if (account is null)
        {
            _workspace.Log("Select an account before loading transactions.");
            return;
        }

        SetBusy(true);
        try
        {
            var query = new TransactionQueryModel
            {
                DateFrom = ToDateOnly(DateFromPicker.SelectedDate),
                DateTo = ToDateOnly(DateToPicker.SelectedDate),
                TransactionStatus = GetSelectedTag(StatusComboBox),
                Strategy = GetSelectedTag(StrategyComboBox)
            };
            TransactionCollectionModel result = await _workspace.ApiClient.GetTransactionsAsync(
                account.Uid,
                query,
                _workspace.CancellationToken);
            TransactionListItem[] items = result.Transactions
                .Select(TransactionListItem.FromJson)
                .OrderByDescending(item => item.Date, StringComparer.Ordinal)
                .ToArray();

            TransactionsDataGrid.ItemsSource = items;
            TransactionCountTextBlock.Text = $"{items.Length} transaction(s)";
            TransactionJsonTextBox.Clear();
            SelectedTransactionTextBlock.Text = "Select a transaction to inspect its raw payload.";
            LoadTransactionDetailsButton.IsEnabled = false;
            _workspace.Log($"Loaded {items.Length} transaction(s) for {account.DisplayName}.");
        }
        catch (OperationCanceledException) when (_workspace.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _workspace.Log(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }
    #endregion

    #region Handle transaction selection
    private void TransactionsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TransactionsDataGrid.SelectedItem is not TransactionListItem transaction)
        {
            TransactionJsonTextBox.Clear();
            SelectedTransactionTextBlock.Text = "Select a transaction to inspect its raw payload.";
            LoadTransactionDetailsButton.IsEnabled = false;
            return;
        }

        TransactionJsonTextBox.Text = transaction.RawJson;
        SelectedTransactionTextBlock.Text = string.IsNullOrWhiteSpace(transaction.TransactionId)
            ? "Raw transaction payload • no transaction_id exposed"
            : $"Transaction {transaction.TransactionId}";
        LoadTransactionDetailsButton.IsEnabled = !string.IsNullOrWhiteSpace(transaction.TransactionId);
    }
    #endregion

    #region Load selected transaction details
    private async void LoadTransactionDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        AccountModel? account = GetSelectedAccount();
        TransactionListItem? transaction = TransactionsDataGrid.SelectedItem as TransactionListItem;
        if (account is null || transaction is null || string.IsNullOrWhiteSpace(transaction.TransactionId))
        {
            return;
        }

        SetBusy(true);
        try
        {
            var data = await _workspace.ApiClient.GetTransactionDetailsAsync(
                account.Uid,
                transaction.TransactionId,
                _workspace.CancellationToken);
            TransactionJsonTextBox.Text = JsonFormatting.Format(data);
            SelectedTransactionTextBlock.Text = $"Full transaction details • {transaction.TransactionId}";
            _workspace.Log("Loaded full transaction details.");
        }
        catch (OperationCanceledException) when (_workspace.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _workspace.Log(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }
    #endregion

    #region Browse CSV export path
    private void BrowseExportButton_Click(object sender, RoutedEventArgs e)
    {
        string month = string.IsNullOrWhiteSpace(ExportMonthTextBox.Text)
            ? DateTime.Today.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture)
            : ExportMonthTextBox.Text.Trim();
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"Posteringer-{month}.csv",
            Title = "Choose CSV export file"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            ExportPathTextBox.Text = dialog.FileName;
        }
    }
    #endregion

    #region Export selected account
    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        AccountModel? account = GetSelectedAccount();
        if (account is null && ExportAllAccountsCheckBox.IsChecked != true)
        {
            _workspace.Log("Select an account or choose All accounts before exporting.");
            return;
        }

        SetBusy(true);
        try
        {
            var request = new ExportRequest
            {
                Month = NullIfWhiteSpace(ExportMonthTextBox.Text),
                AccountUid = ExportAllAccountsCheckBox.IsChecked == true ? null : account?.Uid,
                OutputPath = NullIfWhiteSpace(ExportPathTextBox.Text)
            };
            ExportResultModel result = await _workspace.ApiClient.ExportAsync(request, _workspace.CancellationToken);
            ExportPathTextBox.Text = result.OutputPath;
            _workspace.Log(
                $"Exported {result.TransactionCount} transaction(s) for {result.Month} to {result.OutputPath}");
        }
        catch (OperationCanceledException) when (_workspace.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _workspace.Log(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }
    #endregion

    #region Update account selection
    private void UpdateAccounts()
    {
        string? selectedUid = GetSelectedAccount()?.Uid;
        AccountComboBox.ItemsSource = _workspace.Session.Accounts;
        if (_workspace.Session.Accounts.Count == 0)
        {
            AccountComboBox.SelectedItem = null;
            LoadTransactionsButton.IsEnabled = false;
            ExportButton.IsEnabled = false;
            return;
        }

        AccountModel? selected = _workspace.Session.Accounts.FirstOrDefault(account =>
            string.Equals(account.Uid, selectedUid, StringComparison.OrdinalIgnoreCase));
        AccountComboBox.SelectedItem = selected ?? _workspace.Session.Accounts[0];
        LoadTransactionsButton.IsEnabled = true;
        ExportButton.IsEnabled = true;
    }
    #endregion

    #region Get selected account
    private AccountModel? GetSelectedAccount()
    {
        return AccountComboBox.SelectedItem as AccountModel;
    }
    #endregion

    #region Set busy state
    private void SetBusy(bool isBusy)
    {
        LoadTransactionsButton.IsEnabled = !isBusy && GetSelectedAccount() is not null;
        ExportButton.IsEnabled = !isBusy && GetSelectedAccount() is not null;
        LoadTransactionDetailsButton.IsEnabled = !isBusy &&
            TransactionsDataGrid.SelectedItem is TransactionListItem item &&
            !string.IsNullOrWhiteSpace(item.TransactionId);
    }
    #endregion

    #region Read combo box tag
    private static string? GetSelectedTag(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem item ? item.Tag as string : null;
    }
    #endregion

    #region Convert selected date
    private static DateOnly? ToDateOnly(DateTime? value)
    {
        return value.HasValue ? DateOnly.FromDateTime(value.Value) : null;
    }
    #endregion

    #region Normalize optional text
    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
    #endregion
}
