using System.Windows;
using System.Windows.Controls;
using NykreditTransactionExporter.WpfSample.Api.Models;
using NykreditTransactionExporter.WpfSample.Formatting;
using NykreditTransactionExporter.WpfSample.Services;

namespace NykreditTransactionExporter.WpfSample.Views.Accounts;

public partial class AccountsView : UserControl
{
    #region Readonly Fields
    private readonly AppWorkspace _workspace;
    #endregion

    #region Create accounts view
    internal AccountsView(AppWorkspace workspace)
    {
        InitializeComponent();
        _workspace = workspace;
        _workspace.StateChanged += Workspace_StateChanged;
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
    private void AccountListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedAccount();
    }
    #endregion

    #region Load account details
    private async void LoadDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAccountActionAsync(async account =>
        {
            var data = await _workspace.ApiClient.GetAccountDetailsAsync(
                account.Uid,
                _workspace.CancellationToken);
            DetailsJsonTextBox.Text = JsonFormatting.Format(data);
            _workspace.Log($"Loaded account details for {account.DisplayName}.");
        });
    }
    #endregion

    #region Load account balances
    private async void LoadBalancesButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAccountActionAsync(async account =>
        {
            var data = await _workspace.ApiClient.GetAccountBalancesAsync(
                account.Uid,
                _workspace.CancellationToken);
            BalancesJsonTextBox.Text = JsonFormatting.Format(data);
            _workspace.Log($"Loaded balances for {account.DisplayName}.");
        });
    }
    #endregion

    #region Execute selected account action
    private async Task RunAccountActionAsync(Func<AccountModel, Task> action)
    {
        AccountModel? account = AccountListBox.SelectedItem as AccountModel;
        if (account is null)
        {
            _workspace.Log("Select an account first.");
            return;
        }

        SetButtonsEnabled(false);
        try
        {
            await action(account);
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
            SetButtonsEnabled(true);
        }
    }
    #endregion

    #region Update account list
    private void UpdateAccounts()
    {
        string? selectedUid = (AccountListBox.SelectedItem as AccountModel)?.Uid;
        AccountListBox.ItemsSource = _workspace.Session.Accounts;
        if (_workspace.Session.Accounts.Count == 0)
        {
            UpdateSelectedAccount();
            return;
        }

        AccountModel? selected = _workspace.Session.Accounts.FirstOrDefault(account =>
            string.Equals(account.Uid, selectedUid, StringComparison.OrdinalIgnoreCase));
        AccountListBox.SelectedItem = selected ?? _workspace.Session.Accounts[0];
    }
    #endregion

    #region Update selected account
    private void UpdateSelectedAccount()
    {
        if (AccountListBox.SelectedItem is not AccountModel account)
        {
            AccountTitleTextBlock.Text = "No account selected";
            AccountSubtitleTextBlock.Text = "Authorize the application or refresh the session.";
            LoadDetailsButton.IsEnabled = false;
            LoadBalancesButton.IsEnabled = false;
            return;
        }

        AccountTitleTextBlock.Text = account.DisplayName;
        string iban = string.IsNullOrWhiteSpace(account.Iban) ? "No IBAN" : account.Iban;
        string currency = string.IsNullOrWhiteSpace(account.Currency) ? "Currency unknown" : account.Currency;
        AccountSubtitleTextBlock.Text = $"{iban}  •  {currency}  •  UID {account.Uid}";
        LoadDetailsButton.IsEnabled = true;
        LoadBalancesButton.IsEnabled = true;
    }
    #endregion

    #region Set account action state
    private void SetButtonsEnabled(bool isEnabled)
    {
        bool hasSelection = AccountListBox.SelectedItem is AccountModel;
        LoadDetailsButton.IsEnabled = isEnabled && hasSelection;
        LoadBalancesButton.IsEnabled = isEnabled && hasSelection;
    }
    #endregion
}
