using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NykreditTransactionExporter.WpfSample.Api.Models;
using NykreditTransactionExporter.WpfSample.Api.Models.Watcher;
using NykreditTransactionExporter.WpfSample.Services;

namespace NykreditTransactionExporter.WpfSample.Views.Watcher;

public partial class WatcherView : UserControl
{
    #region Readonly Fields
    private readonly AppWorkspace _workspace;
    #endregion

    #region Create watcher view
    internal WatcherView(AppWorkspace workspace)
    {
        InitializeComponent();
        _workspace = workspace;
        _workspace.StateChanged += Workspace_StateChanged;
        UpdateAccountList();
        UpdateView();
    }
    #endregion

    #region Handle workspace state change
    private void Workspace_StateChanged(object? sender, EventArgs e)
    {
        UpdateAccountList();
        UpdateView();
    }
    #endregion

    #region Start watcher
    private async void StartWatcherButton_Click(object sender, RoutedEventArgs e)
    {
        await RunWatcherActionAsync(async () =>
        {
            await _workspace.ApiClient.StartWatcherAsync(
                new WatcherRequest { AccountUid = GetSelectedAccountUid() },
                _workspace.CancellationToken);
            await _workspace.RefreshWatcherAsync();
            _workspace.Log("Watcher started.");
        });
    }
    #endregion

    #region Stop watcher
    private async void StopWatcherButton_Click(object sender, RoutedEventArgs e)
    {
        await RunWatcherActionAsync(async () =>
        {
            await _workspace.ApiClient.StopWatcherAsync(_workspace.CancellationToken);
            await _workspace.RefreshWatcherAsync();
            _workspace.Log("Watcher stopped.");
        });
    }
    #endregion

    #region Run watcher once
    private async void PollOnceButton_Click(object sender, RoutedEventArgs e)
    {
        await RunWatcherActionAsync(async () =>
        {
            await _workspace.ApiClient.RunWatcherOnceAsync(
                new WatcherRequest { AccountUid = GetSelectedAccountUid() },
                _workspace.CancellationToken);
            await _workspace.RefreshWatcherAsync();
            _workspace.Log("Watcher poll completed.");
        });
    }
    #endregion

    #region Handle event selection
    private void WatcherEventsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WatcherEventsDataGrid.SelectedItem is not WatcherEventModel watcherEvent)
        {
            EventDetailTextBlock.Text = "Select a detected transaction to inspect it.";
            return;
        }

        WatcherTransactionModel transaction = watcherEvent.Transaction;
        string date = (transaction.BookingDate ?? transaction.TransactionDate)?.ToString(
            "dd-MM-yyyy",
            CultureInfo.InvariantCulture) ?? "-";
        EventDetailTextBlock.Text =
            $"Event ID\n{watcherEvent.EventId}\n\n" +
            $"Detected\n{watcherEvent.OccurredAt.ToLocalTime():dd-MM-yyyy HH:mm:ss}\n\n" +
            $"Date\n{date}\n\n" +
            $"Amount\n{transaction.Amount:N2} {transaction.Currency}\n\n" +
            $"Description\n{FirstNonEmpty(transaction.Description, "(no description)")}\n\n" +
            $"Counterparty\n{FirstNonEmpty(transaction.Counterparty, "-")}\n\n" +
            $"Account\n{FirstNonEmpty(transaction.Account, transaction.AccountUid)}";
    }
    #endregion

    #region Execute watcher action
    private async Task RunWatcherActionAsync(Func<Task> action)
    {
        SetButtonsEnabled(false);
        try
        {
            await action();
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
            UpdateView();
        }
    }
    #endregion

    #region Update watcher view
    private void UpdateView()
    {
        WatcherStatusModel status = _workspace.WatcherStatus;
        WatcherStateTextBlock.Text = status.Running ? "Watcher running" : "Watcher stopped";
        string scope = string.IsNullOrWhiteSpace(status.AccountUid) ? "all accounts" : status.AccountUid;
        string lastPoll = status.LastSuccessfulPollAt?.ToLocalTime().ToString(
            "dd-MM-yyyy HH:mm:ss",
            CultureInfo.InvariantCulture) ?? "never";
        string error = string.IsNullOrWhiteSpace(status.LastError) ? string.Empty : $" • {status.LastError}";
        WatcherDetailTextBlock.Text = $"Scope: {scope} • Last successful poll: {lastPoll}{error}";
        string? selectedEventId = (WatcherEventsDataGrid.SelectedItem as WatcherEventModel)?.EventId;
        WatcherEventsDataGrid.ItemsSource = _workspace.WatcherEvents;
        if (!string.IsNullOrWhiteSpace(selectedEventId))
        {
            WatcherEventsDataGrid.SelectedItem = _workspace.WatcherEvents.FirstOrDefault(item =>
                string.Equals(item.EventId, selectedEventId, StringComparison.Ordinal));
        }

        SetButtonsEnabled(true);
    }
    #endregion

    #region Update account selection
    private void UpdateAccountList()
    {
        string? selectedUid = GetSelectedAccountUid();
        WatcherAccountComboBox.Items.Clear();
        WatcherAccountComboBox.Items.Add(new ComboBoxItem
        {
            Content = "All accounts",
            Tag = null
        });
        foreach (AccountModel account in _workspace.Session.Accounts)
        {
            WatcherAccountComboBox.Items.Add(new ComboBoxItem
            {
                Content = account.DisplayName,
                Tag = account.Uid
            });
        }

        ComboBoxItem? selected = WatcherAccountComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag as string,
                selectedUid,
                StringComparison.OrdinalIgnoreCase));
        WatcherAccountComboBox.SelectedItem = selected ?? WatcherAccountComboBox.Items[0];
    }
    #endregion

    #region Get selected account uid
    private string? GetSelectedAccountUid()
    {
        return WatcherAccountComboBox.SelectedItem is ComboBoxItem item ? item.Tag as string : null;
    }
    #endregion

    #region Set watcher action state
    private void SetButtonsEnabled(bool isEnabled)
    {
        StartWatcherButton.IsEnabled = isEnabled && !_workspace.WatcherStatus.Running;
        PollOnceButton.IsEnabled = isEnabled && !_workspace.WatcherStatus.Running;
        StopWatcherButton.IsEnabled = isEnabled && _workspace.WatcherStatus.Running;
        WatcherAccountComboBox.IsEnabled = isEnabled && !_workspace.WatcherStatus.Running;
    }
    #endregion

    #region Select first non-empty text
    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
    #endregion
}
