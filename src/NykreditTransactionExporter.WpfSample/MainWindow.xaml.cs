using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NykreditTransactionExporter.WpfSample.Api.Models;
using NykreditTransactionExporter.WpfSample.Services;
using NykreditTransactionExporter.WpfSample.Views.Accounts;
using NykreditTransactionExporter.WpfSample.Views.DataExplorer;
using NykreditTransactionExporter.WpfSample.Views.Overview;
using NykreditTransactionExporter.WpfSample.Views.Transactions;
using NykreditTransactionExporter.WpfSample.Views.Watcher;

namespace NykreditTransactionExporter.WpfSample;

public partial class MainWindow : Window
{
    #region Members
    #region Readonly Fields
    private readonly CancellationTokenSource _windowCancellation = new();
    private readonly AppWorkspace _workspace;
    private readonly DispatcherTimer _watcherRefreshTimer;
    private readonly OverviewView _overviewView;
    private readonly AccountsView _accountsView;
    private readonly TransactionsView _transactionsView;
    private readonly WatcherView _watcherView;
    private readonly DataExplorerView _dataExplorerView;
    #endregion

    #region Fields
    private bool _watcherRefreshInProgress;
    private string? _lastWatcherRefreshError;
    #endregion
    #endregion

    #region Create main window
    public MainWindow()
    {
        InitializeComponent();
        _workspace = new AppWorkspace(_windowCancellation.Token);
        _overviewView = new OverviewView(_workspace);
        _accountsView = new AccountsView(_workspace);
        _transactionsView = new TransactionsView(_workspace);
        _watcherView = new WatcherView(_workspace);
        _dataExplorerView = new DataExplorerView(_workspace);
        _workspace.StateChanged += Workspace_StateChanged;
        _workspace.ActivityAdded += Workspace_ActivityAdded;

        _watcherRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _watcherRefreshTimer.Tick += WatcherRefreshTimer_Tick;
        Loaded += Window_Loaded;
        NavigationListBox.SelectedIndex = 0;
    }
    #endregion

    #region Handle initial window load
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ConnectAndRefreshAsync();
        _watcherRefreshTimer.Start();
    }
    #endregion

    #region Handle window close
    protected override void OnClosed(EventArgs e)
    {
        _watcherRefreshTimer.Stop();
        _windowCancellation.Cancel();
        _workspace.Dispose();
        _windowCancellation.Dispose();
        base.OnClosed(e);
    }
    #endregion

    #region Handle navigation selection
    private void NavigationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavigationListBox.SelectedItem is not ListBoxItem item || item.Tag is not string page)
        {
            return;
        }

        NavigateTo(page);
    }
    #endregion

    #region Connect to local API
    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        await ConnectAndRefreshAsync();
    }
    #endregion

    #region Refresh application state
    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RunShellActionAsync(_workspace.RefreshAllAsync);
    }
    #endregion

    #region Start Nykredit authorization
    private async void AuthorizeButton_Click(object sender, RoutedEventArgs e)
    {
        await RunShellActionAsync(async () =>
        {
            AuthorizationStartModel authorization = await _workspace.ApiClient.StartAuthorizationAsync(
                _workspace.CancellationToken);
            Process.Start(new ProcessStartInfo(authorization.Url)
            {
                UseShellExecute = true
            });
            _workspace.Log("Authorization opened in the browser. Complete MitID and press Refresh when finished.");
        });
    }
    #endregion

    #region Refresh watcher timer
    private async void WatcherRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (_watcherRefreshInProgress)
        {
            return;
        }

        _watcherRefreshInProgress = true;
        try
        {
            await _workspace.RefreshWatcherAsync();
            _lastWatcherRefreshError = null;
        }
        catch (OperationCanceledException) when (_windowCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!string.Equals(_lastWatcherRefreshError, exception.Message, StringComparison.Ordinal))
            {
                _lastWatcherRefreshError = exception.Message;
                _workspace.Log($"Watcher UI refresh paused: {exception.Message}");
            }
        }
        finally
        {
            _watcherRefreshInProgress = false;
        }
    }
    #endregion

    #region Handle workspace state change
    private void Workspace_StateChanged(object? sender, EventArgs e)
    {
        UpdateShellState();
    }
    #endregion

    #region Handle activity message
    private void Workspace_ActivityAdded(string message)
    {
        LastActivityTextBlock.Text = message;
    }
    #endregion

    #region Connect and refresh
    private async Task ConnectAndRefreshAsync()
    {
        await RunShellActionAsync(async () =>
        {
            _workspace.ConfigureApi(ApiUrlTextBox.Text);
            await _workspace.RefreshAllAsync();
        });
    }
    #endregion

    #region Execute shell action
    private async Task RunShellActionAsync(Func<Task> action)
    {
        SetShellBusy(true);
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (_windowCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _workspace.Log(exception.Message);
        }
        finally
        {
            SetShellBusy(false);
            UpdateShellState();
        }
    }
    #endregion

    #region Navigate to workspace page
    private void NavigateTo(string page)
    {
        switch (page)
        {
            case "accounts":
                ContentHost.Content = _accountsView;
                PageTitleTextBlock.Text = "Accounts";
                PageSubtitleTextBlock.Text = "Inspect account metadata and balances exposed by Nykredit.";
                break;
            case "transactions":
                ContentHost.Content = _transactionsView;
                PageTitleTextBlock.Text = "Transactions";
                PageSubtitleTextBlock.Text = "Query, inspect and export account transactions.";
                break;
            case "watcher":
                ContentHost.Content = _watcherView;
                PageTitleTextBlock.Text = "Watcher";
                PageSubtitleTextBlock.Text = "Monitor newly booked transactions and review detected events.";
                break;
            case "data":
                ContentHost.Content = _dataExplorerView;
                PageTitleTextBlock.Text = "Data explorer";
                PageSubtitleTextBlock.Text = "Inspect the raw Enable Banking data behind the normalized views.";
                break;
            default:
                ContentHost.Content = _overviewView;
                PageTitleTextBlock.Text = "Overview";
                PageSubtitleTextBlock.Text = "Session, accounts, watcher state and recent activity.";
                break;
        }
    }
    #endregion

    #region Update shell state
    private void UpdateShellState()
    {
        if (_workspace.IsConnected)
        {
            ConnectionStatusTextBlock.Text = "Connected";
            ConnectionStatusTextBlock.Foreground = GetBrush("SuccessBrush");
            ConnectionStatusBorder.Background = GetBrush("SuccessSurfaceBrush");
            string session = _workspace.Session.HasSession ? _workspace.Session.Status : "not authorized";
            ConnectionDetailTextBlock.Text = $"Connected • Session {session}";
        }
        else
        {
            ConnectionStatusTextBlock.Text = "Offline";
            ConnectionStatusTextBlock.Foreground = GetBrush("WarningBrush");
            ConnectionStatusBorder.Background = GetBrush("WarningSurfaceBrush");
            ConnectionDetailTextBlock.Text = "API unavailable";
        }

        AuthorizeButton.IsEnabled = _workspace.IsConnected && !IsShellBusy();
        RefreshButton.IsEnabled = !IsShellBusy();
        ConnectButton.IsEnabled = !IsShellBusy();
    }
    #endregion

    #region Set shell busy state
    private void SetShellBusy(bool isBusy)
    {
        BusyProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        ApiUrlTextBox.IsEnabled = !isBusy;
        ConnectButton.IsEnabled = !isBusy;
        RefreshButton.IsEnabled = !isBusy;
        AuthorizeButton.IsEnabled = !isBusy && _workspace.IsConnected;
    }
    #endregion

    #region Check shell busy state
    private bool IsShellBusy()
    {
        return BusyProgressBar.Visibility == Visibility.Visible;
    }
    #endregion

    #region Resolve application brush
    private Brush GetBrush(string resourceKey)
    {
        return (Brush)FindResource(resourceKey);
    }
    #endregion
}
