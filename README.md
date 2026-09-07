# Nykredit Transaction Exporter (.NET 10)

A local .NET 10 application for retrieving booked transactions from private Nykredit accounts through Enable Banking, exporting them to CSV, and detecting newly booked transactions locally with optional outbound webhooks.

The solution contains:

- `NykreditTransactionExporter` — the existing command-line application plus a local HTTPS REST API mode.
- `NykreditTransactionExporter.WpfSample` — a custom multi-page Windows WPF client for exploring and controlling the REST API.

The implementation uses only .NET platform frameworks. No third-party NuGet package is required.

## Features

- Nykredit/Enable Banking account-information authorization.
- MitID Strong Customer Authentication in the browser.
- Local persisted Enable Banking session.
- Session and account status.
- Monthly booked-transaction export to semicolon-separated CSV.
- Optional export of one account only.
- Poll-based detection of newly booked transactions with a persistent local recent-event feed.
- Optional outbound `transaction.booked` webhooks with independent retry tracking.
- Optional HMAC-SHA256 webhook signing.
- Local HTTPS REST API for authorize, status, rich account data, export, and watcher control.
- Multi-page WPF workspace for accounts, balances, transactions, watcher events, exports, and raw API data.

Enable Banking does not provide a native account-transaction webhook. The watcher therefore polls booked transactions, records newly detected transactions locally, and optionally emits application-level webhooks when `Watcher.WebhookUrl` is configured.

## Solution structure

```text
NykreditTransactionExporter.slnx
src/
  NykreditTransactionExporter/
    Application/
    Configuration/
    EnableBanking/
    Export/
    Storage/
    NykreditTransactionExporter.csproj
    appsettings.example.json
  NykreditTransactionExporter.WpfSample/
    Api/
    Formatting/
    Services/
    Themes/
    Views/
    App.xaml
    MainWindow.xaml
    NykreditTransactionExporter.WpfSample.csproj
```

## Prerequisites

1. Install the .NET 10 SDK.
2. Create an Enable Banking account.
3. Register a Production API application in Enable Banking.
4. For private non-commercial use, activate the Production application by linking your own Nykredit accounts.
5. Register the following redirect URL on the Enable Banking application:

   `https://localhost:53682/callback/`

6. Store the downloaded private PEM key outside the repository.
7. Trust the local ASP.NET Core development HTTPS certificate before using the REST API/WPF sample:

```text
dotnet dev-certs https --trust
```

The REST API intentionally binds only to the HTTPS loopback origin derived from `EnableBanking.RedirectUrl`. It does not expose the banking API on the LAN.

## Configuration

Copy:

`src/NykreditTransactionExporter/appsettings.example.json`

to:

`src/NykreditTransactionExporter/appsettings.json`

Example:

```json
{
  "EnableBanking": {
    "ApiBaseUrl": "https://api.enablebanking.com",
    "ApplicationId": "PUT-YOUR-ENABLE-BANKING-APPLICATION-ID-HERE",
    "PrivateKeyPath": "C:\\Users\\YOUR-USER\\Documents\\EnableBanking\\private.pem",
    "AspspName": "Nykredit Bank",
    "AspspCountry": "DK",
    "PsuType": "personal",
    "RedirectUrl": "https://localhost:53682/callback/",
    "PreferredConsentDays": 180
  },
  "Storage": {
    "SessionFile": "data/session.json",
    "ExportDirectory": "exports"
  },
  "Watcher": {
    "StateFile": "data/watcher-state.json",
    "WebhookUrl": "",
    "WebhookSecret": "",
    "IntervalMinutes": 360,
    "LookbackDays": 7,
    "NotifyExistingOnFirstRun": false
  }
}
```

Set:

- `EnableBanking.ApplicationId` to the application id shown by Enable Banking.
- `EnableBanking.PrivateKeyPath` to the downloaded private RSA PEM key.
- `EnableBanking.RedirectUrl` to exactly the redirect URL registered in Enable Banking.
- `Watcher.WebhookUrl` only when the watcher should also deliver transaction events to an external HTTP endpoint. Leave it empty for local-only watching.
- `Watcher.WebhookSecret` when configured webhook requests should be HMAC signed. It is ignored when `WebhookUrl` is empty.

Watcher settings:

- `StateFile` — persistent watcher deduplication, recent local events, and webhook-delivery state.
- `IntervalMinutes` — continuous polling interval. Default: `360`.
- `LookbackDays` — inclusive rolling transaction window. Default: `7`.
- `NotifyExistingOnFirstRun` — when `false`, each account establishes a baseline without sending historical events. Default: `false`.

Environment-variable overrides:

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

## Command-line application

From the repository root, commands use:

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- COMMAND
```

### Authorize

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- authorize
```

The CLI opens the Enable Banking/Nykredit authorization URL. Because the Production redirect is HTTPS, the console does not create its own HTTPS listener. After MitID authorization, copy the complete browser redirect URL and paste it into the console when prompted. The console validates the `state`, exchanges the returned code for a session, and stores the session locally.

For a fully automatic localhost callback, use the REST API or WPF sample instead.

### Status

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- status
```

### Export the previous calendar month

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- export
```

Default output:

`exports/Posteringer-YYYY-MM.csv`

### Export a specific month

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- export --month 2026-08
```

### Export one account

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- export --month 2026-08 --account ACCOUNT-UID
```

### Choose another output path

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- export --output C:\Exports\Posteringer.csv
```

### Continuous watcher

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- watch
```

`Watcher.WebhookUrl` is optional. With an empty URL the watcher still detects, deduplicates, and stores new booked transactions locally; it simply skips outbound HTTP delivery.

### One watcher poll

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- watch --once
```

### Watch one account

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- watch --account ACCOUNT-UID
```

## Local REST API

Start the local HTTPS API:

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- api
```

With the default redirect URL, the API listens on:

`https://localhost:53682`

The callback endpoint is the same URL registered in Enable Banking:

`https://localhost:53682/callback/`

The API is deliberately loopback-only. There is no CORS policy because the sample WPF client is a native HTTP client, not a browser application.

### REST endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| `GET` | `/api/health` | Verify that the local API is running. |
| `GET` | `/api/session` | Return saved/live session status and accessible accounts. |
| `GET` | `/api/accounts` | Return accessible accounts. |
| `POST` | `/api/authorization/start` | Start Nykredit/Enable Banking authorization and return the browser URL. |
| `GET` | `/callback/` | Enable Banking browser callback; validates state and saves the session. |
| `POST` | `/api/export` | Export booked transactions to CSV. |
| `GET` | `/api/watcher/status` | Return local watcher state. |
| `GET` | `/api/watcher/events` | Return up to the 100 most recently detected local transaction events, newest first. |
| `POST` | `/api/watcher/start` | Start continuous watcher polling. |
| `POST` | `/api/watcher/stop` | Stop the watcher started by this API process. |
| `POST` | `/api/watcher/once` | Run one watcher poll. |
| `GET` | `/api/data/application` | Return raw Enable Banking application metadata. |
| `GET` | `/api/data/aspsps` | Return raw ASPSP/bank capability metadata. |
| `GET` | `/api/data/session` | Return the raw current Enable Banking session. |
| `GET` | `/api/data/session/authorization` | Return the original `POST /sessions` authorization response captured when the session was created. |
| `GET` | `/api/data/accounts/{accountUid}/details` | Return the full account resource exposed by the bank. |
| `GET` | `/api/data/accounts/{accountUid}/balances` | Return all balance types exposed by the bank. |
| `GET` | `/api/data/accounts/{accountUid}/transactions` | Return one raw transaction page with upstream filters and continuation key. |
| `GET` | `/api/data/accounts/{accountUid}/transactions/all` | Follow continuation keys and return all matching transactions. |
| `GET` | `/api/data/accounts/{accountUid}/transactions/{transactionId}` | Return full details for one transaction when supported by the ASPSP. |

### Authorization flow through the API

Call:

```text
POST https://localhost:53682/api/authorization/start
```

The response contains `url`, `state`, `redirectUrl`, and `validUntil`. Open `url` in the browser and complete Nykredit/MitID. Enable Banking then redirects the browser to `/callback/`; the API validates the pending state, exchanges the code, stores the session, and shows a completion page.

Authorization states are kept in memory for 30 minutes. If the API is restarted during authorization, start a new authorization flow.

The authorization request now asks Enable Banking for both balance and transaction access. Re-authorize an older saved session once after updating if it was originally created with transaction-only access.

### Broad account-data API

The `/api/data` surface is intentionally read-only and returns Enable Banking JSON with optional fields intact instead of reducing the response to the CSV model. This makes account-specific data such as account identifiers, account servicer information, usage/type, credit limit, postal address, balance variants, merchant category codes, counterparties, remittance information, bank transaction codes, references, exchange-rate data, and other fields available whenever Nykredit/BEC actually supplies them. Optional fields vary by ASPSP and transaction.

The full response from `POST /sessions` is now retained in the protected local session file because Enable Banking documents that some authorization-time account information can be shown only once. `/api/data/session/authorization` exposes that captured response. Existing session files remain compatible, but this endpoint requires one fresh authorization before historical authorization data is available.

ASPSP metadata accepts the current Enable Banking filters: `country`, `psu_type`, `service`, and `payment_type`. Example:

```text
GET /api/data/aspsps?country=DK&psu_type=personal&service=AIS
```

Transaction endpoints mirror Enable Banking query names:

- `date_from=yyyy-MM-dd`
- `date_to=yyyy-MM-dd`
- `continuation_key=...`
- `transaction_status=BOOK|CNCL|HOLD|OTHR|PDNG|RJCT|SCHD`
- `strategy=default|longest`

`/transactions` returns one upstream page including its `continuation_key`. `/transactions/all` follows continuation keys until the complete matching result has been retrieved and returns `transactions` plus `count`.

The account details, balances, transaction-list, and transaction-detail endpoints also forward these optional Enable Banking PSU headers when the local caller provides them:

- `Psu-Ip-Address`
- `Psu-User-Agent`
- `Psu-Referer`
- `Psu-Accept`
- `Psu-Accept-Charset`
- `Psu-Accept-Encoding`
- `Psu-Accept-Language`
- `Psu-Geo-Location`

Only provide PSU headers for a request that is genuinely initiated while the payment-service user is actively using the client. The background watcher deliberately sends no PSU headers.

### Export request

```json
{
  "month": "2026-08",
  "accountUid": null,
  "outputPath": null
}
```

`month`, `accountUid`, and `outputPath` are optional. Omitting `month` exports the previous calendar month. Omitting `accountUid` exports all accounts. Omitting `outputPath` uses the configured export directory.

### Watcher request

Both `/api/watcher/start` and `/api/watcher/once` accept:

```json
{
  "accountUid": null
}
```

Use `null` for all authorized accounts.

No webhook URL is required to start the watcher. New transactions are always recorded in the local watcher state first. If `Watcher.WebhookUrl` is configured, delivery is attempted separately and failed deliveries are retried while the transaction remains in the configured lookback window.

The API can manage one continuous watcher started by that API process. The watcher state-file lock still prevents a second CLI/API process from using the same watcher state concurrently.

## WPF sample

The WPF project is a native .NET 10 Windows client for the local REST API. It does not access the private RSA key or Enable Banking directly.

Start the API first:

```text
dotnet run --project src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -- api
```

Then start the sample:

```text
dotnet run --project src/NykreditTransactionExporter.WpfSample/NykreditTransactionExporter.WpfSample.csproj
```

The sample uses a custom sidebar workspace with five views:

- **Overview** — connection/session status, account summary, watcher state, recent detected transactions, and activity history.
- **Accounts** — account selection plus explicit loading of the full account resource and all balance data exposed by the bank.
- **Transactions** — account/date/status/strategy filters, a transaction grid, raw transaction payloads, optional full transaction details, and monthly CSV export for one or all accounts.
- **Watcher** — continuous watcher start/stop, one-shot polling, account scope, last successful poll, and the persistent recent-event feed.
- **Data explorer** — raw application metadata, ASPSP capability data, current session data, captured authorization response, account details, and balances.

The shell keeps authorization and refresh actions available globally while page-specific actions stay next to the data they affect. Watcher status and locally detected events are refreshed every five seconds; this local UI refresh does not poll Nykredit every five seconds. The bank watcher continues to use the configured `Watcher.IntervalMinutes`.

The default API URL in the sample is `https://localhost:53682/`. The sample accepts HTTPS loopback origins only. It uses normal Windows/.NET certificate validation and does not bypass TLS validation, so the local development certificate must be trusted.

## Webhook payload

When `Watcher.WebhookUrl` is configured, each newly detected booked transaction is sent as HTTP `POST` with `Content-Type: application/json`. Local detection does not depend on successful webhook delivery.

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

Webhook headers:

- `X-Nykredit-Event: transaction.booked`
- `X-Nykredit-Event-Id: <stable event id>`
- `X-Nykredit-Signature: sha256=<hex HMAC-SHA256>` when `Watcher.WebhookSecret` is configured.

The HMAC is calculated over the exact UTF-8 JSON request body. A transaction is acknowledged only after the receiver returns a `2xx` status. Failed deliveries remain unacknowledged and are retried later. Receivers should still use `eventId` for idempotency because delivery is at-least-once.

## Transaction deduplication

Booked transactions normally use Enable Banking `entry_reference` together with the account uid as the primary stable identity.

When `entry_reference` is absent, the watcher creates a deterministic fingerprint from stable transaction fields including dates, amount, currency, direction, description, counterparty, and reference. Duplicate fingerprints receive deterministic occurrence numbers within each poll.

The resulting identities are stored in `Watcher.StateFile`. The same file keeps up to the 100 most recently detected local events for the REST API/WPF feed and tracks webhook completion separately from local detection. Existing version-1 watcher state is upgraded in place without replaying transactions that were already considered seen. Deleting the state file intentionally resets the watcher baseline and local event history.

## CSV columns

The export contains:

`AccountUid;Account;IBAN;BookingDate;TransactionDate;ValueDate;Amount;Currency;Direction;Status;Description;Counterparty;Reference;EntryReference;TransactionId;BalanceAfter`

Debit amounts are negative and credit amounts are positive.

The CSV is a normalized Open Banking export and is not a byte-for-byte recreation of Nykredit's manual Netbank export. Some fields in Nykredit's own export are not exposed through PSD2/Open Banking.

## Publish

A single-file CLI/API publish can be created with:

```text
dotnet publish src/NykreditTransactionExporter/NykreditTransactionExporter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Copy `appsettings.json` next to the published executable, or set `NYKREDIT_CONFIG_PATH` to an absolute configuration path.

The WPF sample can be published independently with normal WPF publish settings.

## Security notes

- Never commit the Enable Banking private PEM key.
- Do not embed the private key into the WPF application.
- Keep `appsettings.json`, PEM/key files, session data, watcher state, and exports outside source control.
- `Watcher.StateFile` now contains the recent local transaction event feed, including transaction details; protect it with the same care as exported account data.
- Prefer `NYKREDIT_WEBHOOK_SECRET` or another local secret store instead of committing a webhook secret.
- Use HTTPS for non-local webhook endpoints.
- The REST API intentionally accepts requests only on loopback. Do not change it to a LAN/public binding without adding an authentication/authorization layer and reviewing the security model.
- The saved Enable Banking session id grants account-information access while the session is valid. The session file also retains the original authorization response and may contain detailed account metadata; protect it with normal operating-system user permissions.
- This project implements account-information retrieval only. It does not initiate payments.
