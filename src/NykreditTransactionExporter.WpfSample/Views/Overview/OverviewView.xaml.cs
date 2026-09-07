using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NykreditTransactionExporter.WpfSample.Api.Models.Watcher;
using NykreditTransactionExporter.WpfSample.Services;

namespace NykreditTransactionExporter.WpfSample.Views.Overview;

public partial class OverviewView : UserControl
{
    #region Readonly Fields
    private readonly AppWorkspace _workspace;
    #endregion

    #region Create overview view
    internal OverviewView(AppWorkspace workspace)
    {
        InitializeComponent();
        _workspace = workspace;
        ActivityListBox.ItemsSource = workspace.Activity;
        _workspace.StateChanged += Workspace_StateChanged;
        UpdateView();
    }
    #endregion

    #region Handle workspace state change
    private void Workspace_StateChanged(object? sender, EventArgs e)
    {
        UpdateView();
    }
    #endregion

    #region Update overview
    private void UpdateView()
    {
        SessionMetricTextBlock.Text = _workspace.Session.HasSession
            ? _workspace.Session.Status
            : "Not authorized";
        SessionDetailTextBlock.Text = _workspace.Session.ValidUntil.HasValue
            ? $"Valid until {FormatDateTime(_workspace.Session.ValidUntil)}"
            : "No active consent expiry available";
        AccountCountTextBlock.Text = _workspace.Session.Accounts.Count.ToString(CultureInfo.InvariantCulture);

        WatcherMetricTextBlock.Text = _workspace.WatcherStatus.Running ? "Running" : "Stopped";
        WatcherDetailTextBlock.Text = _workspace.WatcherStatus.LastSuccessfulPollAt.HasValue
            ? $"Last poll {FormatDateTime(_workspace.WatcherStatus.LastSuccessfulPollAt)}"
            : "No successful poll recorded";

        AccountsItemsControl.ItemsSource = _workspace.Session.Accounts;
        NoAccountsTextBlock.Visibility = _workspace.Session.Accounts.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        WatcherEventModel[] events = _workspace.WatcherEvents.Take(6).ToArray();
        RecentEventsListBox.ItemsSource = events;
        NoEventsTextBlock.Visibility = events.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    #endregion

    #region Format local date and time
    private static string FormatDateTime(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture) ?? "-";
    }
    #endregion
}
