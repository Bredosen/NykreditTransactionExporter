using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NykreditTransactionExporter.WpfSample.Api.Models;
using NykreditTransactionExporter.WpfSample.Formatting;
using NykreditTransactionExporter.WpfSample.Services;

namespace NykreditTransactionExporter.WpfSample.Views.DataExplorer;

public partial class DataExplorerView : UserControl
{
    #region Readonly Fields
    private readonly AppWorkspace _workspace;
    #endregion

    #region Create data explorer view
    internal DataExplorerView(AppWorkspace workspace)
    {
        InitializeComponent();
        _workspace = workspace;
        _workspace.StateChanged += Workspace_StateChanged;
        AccountComboBox.DisplayMemberPath = nameof(AccountModel.DisplayName);
        UpdateAccounts();
        UpdateSourceOptions();
    }
    #endregion

    #region Handle workspace state change
    private void Workspace_StateChanged(object? sender, EventArgs e)
    {
        UpdateAccounts();
    }
    #endregion

    #region Handle data source selection
    private void DataSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        UpdateSourceOptions();
    }
    #endregion

    #region Load selected data source
    private async void LoadDataButton_Click(object sender, RoutedEventArgs e)
    {
        LoadDataButton.IsEnabled = false;
        try
        {
            JsonElement data = await LoadSelectedDataAsync();
            RawJsonTextBox.Text = JsonFormatting.Format(data);
            _workspace.Log($"Loaded {GetSourceDescription().ToLowerInvariant()}.");
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
            LoadDataButton.IsEnabled = true;
        }
    }
    #endregion

    #region Copy JSON to clipboard
    private void CopyJsonButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RawJsonTextBox.Text))
        {
            return;
        }

        Clipboard.SetText(RawJsonTextBox.Text);
        _workspace.Log("Copied raw JSON to the clipboard.");
    }
    #endregion

    #region Load data source
    private async Task<JsonElement> LoadSelectedDataAsync()
    {
        string source = GetSelectedSource();
        CancellationToken cancellationToken = _workspace.CancellationToken;
        return source switch
        {
            "application" => await _workspace.ApiClient.GetApplicationDataAsync(cancellationToken),
            "aspsps" => await _workspace.ApiClient.GetAspspsDataAsync(
                NullIfWhiteSpace(CountryTextBox.Text),
                NullIfWhiteSpace(PsuTypeTextBox.Text),
                NullIfWhiteSpace(ServiceTextBox.Text),
                NullIfWhiteSpace(PaymentTypeTextBox.Text),
                cancellationToken),
            "session" => await _workspace.ApiClient.GetRawSessionDataAsync(cancellationToken),
            "authorization" => await _workspace.ApiClient.GetAuthorizationDataAsync(cancellationToken),
            "account-details" => await _workspace.ApiClient.GetAccountDetailsAsync(
                GetSelectedAccountUid(),
                cancellationToken),
            "account-balances" => await _workspace.ApiClient.GetAccountBalancesAsync(
                GetSelectedAccountUid(),
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported data source '{source}'.")
        };
    }
    #endregion

    #region Update data source options
    private void UpdateSourceOptions()
    {
        string source = GetSelectedSource();
        bool needsAccount = source is "account-details" or "account-balances";
        bool needsAspspOptions = source == "aspsps";
        AccountOptionsPanel.Visibility = needsAccount ? Visibility.Visible : Visibility.Collapsed;
        AspspOptionsPanel.Visibility = needsAspspOptions ? Visibility.Visible : Visibility.Collapsed;
        SourceDescriptionTextBlock.Text = GetSourceDescription();
        LoadDataButton.IsEnabled = !needsAccount || AccountComboBox.SelectedItem is AccountModel;
    }
    #endregion

    #region Update account selection
    private void UpdateAccounts()
    {
        string? selectedUid = (AccountComboBox.SelectedItem as AccountModel)?.Uid;
        AccountComboBox.ItemsSource = _workspace.Session.Accounts;
        if (_workspace.Session.Accounts.Count == 0)
        {
            AccountComboBox.SelectedItem = null;
            UpdateSourceOptions();
            return;
        }

        AccountModel? selected = _workspace.Session.Accounts.FirstOrDefault(account =>
            string.Equals(account.Uid, selectedUid, StringComparison.OrdinalIgnoreCase));
        AccountComboBox.SelectedItem = selected ?? _workspace.Session.Accounts[0];
        UpdateSourceOptions();
    }
    #endregion

    #region Get selected data source
    private string GetSelectedSource()
    {
        return DataSourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string source
            ? source
            : "application";
    }
    #endregion

    #region Get selected account uid
    private string GetSelectedAccountUid()
    {
        return AccountComboBox.SelectedItem is AccountModel account
            ? account.Uid
            : throw new InvalidOperationException("Select an account first.");
    }
    #endregion

    #region Get source description
    private string GetSourceDescription()
    {
        return GetSelectedSource() switch
        {
            "application" => "Enable Banking application metadata",
            "aspsps" => "ASPSP and bank capability metadata",
            "session" => "Raw current Enable Banking session",
            "authorization" => "Saved original authorization response",
            "account-details" => "Full account resource exposed by the bank",
            "account-balances" => "All balance types exposed by the bank",
            _ => "Raw API data"
        };
    }
    #endregion

    #region Normalize optional text
    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
    #endregion
}
