# Nykredit Transaction Exporter (.NET 10)

A small .NET 10 console application for retrieving booked account transactions from a private Nykredit account through Enable Banking, exporting them to CSV, and emitting outbound webhooks when newly booked transactions are detected.

The application uses only the .NET platform libraries. No third-party NuGet package is required.

The repository includes `NykreditTransactionExporter.slnx`, the modern XML solution format used by .NET 10.

## What it does

- Starts an Enable Banking account-information authorization for `Nykredit Bank` in Denmark.
- Uses a personal PSU flow and redirects to Nykredit/MitID for Strong Customer Authentication.
- Stores the resulting Enable Banking session locally.
- Retrieves all booked transactions for one calendar month, including paginated results.
- Exports all authorized accounts by default, or one account with `--account`.
- Uses semicolon-separated CSV, `dd-MM-yyyy` dates, and Danish decimal formatting.
- Defaults to the previous calendar month, making it suitable for Windows Task Scheduler or cron.
- Provides a `watch` command that polls recent booked transactions and sends a `transaction.booked` HTTP webhook for newly detected entries.
- Persists watcher state locally so already acknowledged booked transactions are not emitted again.

Enable Banking does not provide a native account-transaction webhook. The `watch` feature therefore polls the account-transactions endpoint and creates application-level webhook events when a booked transaction appears for the first time.

## Prerequisites

1. Install the .NET 10 SDK.
2. Create an Enable Banking account.
3. Register a production API application in the Enable Banking control panel.
4. For individual non-commercial use, activate the production application in restricted mode by linking your own account.
5. Add this redirect URL to the application's allowed redirect URLs:

   `http://localhost:53682/callback/`

6. Keep the private PEM key outside source control.

## Configuration

Copy:

`src/NykreditTransactionExporter/appsettings.example.json`

to:

`src/NykreditTransactionExporter/appsettings.json`

Then set:

- `EnableBanking.ApplicationId`: the Enable Banking application id.
- `EnableBanking.PrivateKeyPath`: the absolute path to the application's private PEM key.
- `Watcher.WebhookUrl`: the endpoint that should receive newly booked transaction events when `watch` is used.
- `Watcher.WebhookSecret`: optional HMAC secret used to sign webhook request bodies.

Watcher settings:

- `StateFile`: local JSON state containing transaction identities that have already been acknowledged.
- `IntervalMinutes`: continuous polling interval. Default: `360` minutes.
- `LookbackDays`: inclusive rolling date window requested from the bank on every poll. Default: `7` days.
- `NotifyExistingOnFirstRun`: when `false`, the first successful watcher poll for each account establishes a baseline without sending webhooks. Default: `false`.

The following environment variables can override sensitive or machine-specific values:

- `NYKREDIT_APPLICATION_ID`
- `NYKREDIT_PRIVATE_KEY_PATH`
- `NYKREDIT_API_BASE_URL`
- `NYKREDIT_SESSION_FILE`
- `NYKREDIT_EXPORT_DIRECTORY`
- `NYKREDIT_CONFIG_PATH`
- `NYKREDIT_WATCH_STATE_FILE`
- `NYKREDIT_WEBHOOK_URL`
- `NYKREDIT_WEBHOOK_SECRET`
- `NYKREDIT_WATCH_INTERVAL_MINUTES`
- `NYKREDIT_WATCH_LOOKBACK_DAYS`
- `NYKREDIT_WATCH_NOTIFY_EXISTING`

## First authorization

From the repository root:

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- authorize
```

The application fetches the current Nykredit ASPSP metadata, chooses the shorter of the configured consent period and the bank's current maximum consent validity, starts authorization, opens the browser, and waits for the local callback.

Complete the Nykredit/MitID flow. The resulting session is written to `data/session.json` by default.

If the configured redirect URL is not a local HTTP address, the application falls back to asking you to paste the complete redirect URL after authorization.

## Check session status

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- status
```

## Export the previous calendar month

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- export
```

Default output:

`exports/Posteringer-YYYY-MM.csv`

## Export a specific month

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- export --month 2026-08
```

## Export one account only

The `authorize` command prints each account uid. Use one of them:

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- export --month 2026-08 --account ACCOUNT-UID
```

## Choose another output file

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- export --output C:\\Exports\\Posteringer.csv
```

## Watch for newly booked transactions

Configure `Watcher.WebhookUrl`, then run:

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- watch
```

The watcher performs a poll immediately and then waits `Watcher.IntervalMinutes` before polling again. Only booked (`BOOK`) transactions are considered.

By default, the first successful poll for each account stores the existing transactions in the configured lookback window without notifying them. This prevents an initial burst of historical events, including when another account is selected later. Set `NotifyExistingOnFirstRun` to `true` if the first poll for an account should emit every booked transaction currently returned in the lookback window.

Use `Ctrl+C` to stop the continuous watcher. Only one watcher process can use a given `Watcher.StateFile` at a time; a second process fails fast instead of racing the deduplication state.

### Run one watcher poll

For testing or an external scheduler:

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- watch --once
```

A one-shot run returns an error if any new transaction could not be delivered. Successfully delivered transactions are still persisted, while failed events remain unacknowledged and are retried on the next run.

### Watch one account only

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- watch --account ACCOUNT-UID
```

## Webhook payload

Each newly detected booked transaction is sent as an HTTP `POST` with `Content-Type: application/json`.

Example:

```json
{
  "eventId": "8f03f0e6c5f2...",
  "type": "transaction.booked",
  "occurredAt": "2026-09-06T12:00:00+00:00",
  "transaction": {
    "accountUid": "ACCOUNT-UID",
    "account": "Private account",
    "iban": "DK...",
    "bookingDate": "2026-09-06",
    "transactionDate": "2026-09-06",
    "valueDate": "2026-09-06",
    "amount": -149.95,
    "currency": "DKK",
    "direction": "Debit",
    "status": "BOOK",
    "description": "Example merchant",
    "counterparty": "Example merchant",
    "reference": "",
    "entryReference": "123456789",
    "transactionId": "...",
    "balanceAfter": 1234.56
  }
}
```

The following request headers are included:

- `X-Nykredit-Event: transaction.booked`
- `X-Nykredit-Event-Id: <stable event id>`
- `X-Nykredit-Signature: sha256=<hex HMAC-SHA256>` when `Watcher.WebhookSecret` is configured.

The HMAC is calculated over the exact UTF-8 JSON request body. The receiving endpoint should calculate HMAC-SHA256 with the same secret and compare the result using a constant-time comparison.

A transaction is only marked as acknowledged after the webhook returns a successful HTTP status code (`2xx`), and successful acknowledgements are persisted immediately. Failed deliveries are therefore retried during later polls. Delivery should still be treated as at-least-once: the receiver should use the stable `eventId` or `X-Nykredit-Event-Id` header for idempotency in case a process or network failure occurs between the receiver accepting a request and local state being persisted.

## Transaction deduplication

Booked transactions normally use Enable Banking's `entry_reference` as the primary stable identity together with the account uid.

If `entry_reference` is missing, the watcher falls back to a deterministic fingerprint of stable transaction fields such as dates, amount, currency, direction, description, counterparty, and reference. Duplicate fingerprints are assigned deterministic occurrence numbers within each poll.

The resulting identities are stored in `Watcher.StateFile`. Do not delete this file unless you intentionally want the watcher to establish a new baseline.

## Publish for Windows Task Scheduler

A single-file publish is convenient for scheduled tasks:

```text
dotnet publish src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Copy `appsettings.json` next to the published executable, or set `NYKREDIT_CONFIG_PATH` to an absolute configuration path.

For monthly exports, create a monthly scheduled task that runs:

`export`

Because no `--month` is supplied, it exports the previous calendar month.

For transaction notifications without a continuously running process, schedule:

`watch --once`

at an interval compatible with the bank/API access limits for your account and consent.

## Security notes

- Never commit the Enable Banking private PEM key.
- Prefer setting `NYKREDIT_WEBHOOK_SECRET` through the process environment or your host's secret store instead of committing it to `appsettings.json`.
- Use HTTPS for non-local webhook endpoints.
- `appsettings.json`, PEM/key files, session data, watcher state, and exports are ignored by the supplied `.gitignore` when stored in the default directories.
- The saved session id grants access to account information while the session is authorized. Protect the session file with your normal user account permissions.
- The watcher state can reveal transaction timing and should be protected with the same user account permissions.
- This project only implements account-information retrieval. It does not implement payment initiation.

## CSV columns

The export contains:

`AccountUid;Account;IBAN;BookingDate;TransactionDate;ValueDate;Amount;Currency;Direction;Status;Description;Counterparty;Reference;EntryReference;TransactionId;BalanceAfter`

Debit amounts are negative and credit amounts are positive.

The file is intentionally a normalized Open Banking export, not a byte-for-byte recreation of Nykredit's manual netbank CSV export. Some fields available in Nykredit's own export are not exposed by PSD2/Open Banking.
