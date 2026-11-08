# Zain Recharge API

## What this is
The Zain Recharge API lets a client recharge a Zain mobile number. It records every attempt in its own database, then asks an external payment provider (the Mock Payment Provider, a separate solution with its own database) to process the payment.

```
Client  -->  Zain Recharge API (:5200)  -->  Mock Payment Provider (:5100)
                  |                                  |
           ZainRechargeDb                  MockPaymentProviderDb
                  \__________ logs to Seq (:5341) ____/
```

The two systems talk only over HTTP. They share no code and no database.

## Prerequisites
- .NET 10 SDK
- SQL Server LocalDB (installed with Visual Studio)
- `dotnet-ef` tool: `dotnet tool install --global dotnet-ef`
- Seq for logs (the Windows installer or Docker), listening on http://localhost:5341
- The Mock Payment Provider repository, running on http://localhost:5100

## Setup
1. Clone this repository and open a terminal in its folder.
2. Start Seq and check http://localhost:5341 opens.
3. Create the database:
   ```
   dotnet ef database update --project src/ZainRecharge.Infrastructure --startup-project src/ZainRecharge.Api
   ```
4. Start the Mock Payment Provider first (see its README).
5. Run this API:
   ```
   dotnet run --project src/ZainRecharge.Api
   ```
6. Open Swagger at http://localhost:5200/swagger

## Configuration
Real secrets never go into Git. `appsettings.json` has empty values; `appsettings.Development.json` has fake values that only work on a developer laptop.

| Setting | Meaning | Example |
|---|---|---|
| ConnectionStrings:Default | The Zain database | Server=(localdb)\MSSQLLocalDB;Database=ZainRechargeDb;Trusted_Connection=True;TrustServerCertificate=True |
| PaymentProvider:BaseUrl | Where the provider runs | http://localhost:5100 |
| PaymentProvider:ApiKey | Sent as X-Api-Key to the provider | dev-only-provider-api-key |
| PaymentProvider:ClientId | Used to get a bearer token | zain-recharge-dev |
| PaymentProvider:ClientSecret | Used to get a bearer token | dev-only-client-secret |
| PaymentProvider:PaymentTimeoutSeconds | How long to wait for a payment answer | 5 |
| PaymentProvider:TokenTimeoutSeconds | How long to wait for a token | 5 |
| PaymentProvider:TokenRefreshMarginSeconds | Ask for a new token this long before expiry | 30 |
| Serilog:WriteTo (Seq serverUrl) | Where logs are sent | http://localhost:5341 |

In a real environment the values come from environment variables or a secret store (for local work: `dotnet user-secrets`).

## Ports
| Service | Port |
|---|---|
| Mock Payment Provider | 5100 |
| Zain Recharge API | 5200 |
| Seq | 5341 |

## API contract
| Method | URL | Purpose | Success |
|---|---|---|---|
| POST | /api/recharge | Submit a recharge | 201 |
| GET | /api/recharge/{id} | Get one recharge | 200 |
| GET | /api/recharge/history/{mobileNumber}?page=1&pageSize=20 | History, newest first | 200 |
| GET | /health | Service and database up | 200 |

Request:
```json
{ "mobileNumber": "00962791234567", "amount": 5.00 }
```

Response:
```json
{
  "id": 1,
  "transactionId": "TXN-1001",
  "mobileNumber": "0791234567",
  "amount": 5.00,
  "status": "SUCCESS",
  "paymentReference": "PAY-50001",
  "failureReason": null,
  "message": "Recharge completed successfully",
  "createdAt": "2026-11-04T09:04:10Z",
  "updatedAt": "2026-11-04T09:04:11Z"
}
```

History response: `{ "mobileNumber", "page", "pageSize", "totalCount", "items": [ ... ] }`.

Every error uses the same shape (ProblemDetails) and includes a `traceId`:
```json
{
  "type": "https://httpstatuses.io/400",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "traceId": "00-3f2b8c1e9d4a4b7e8a6f0c5d2e1b9a7c-1a2b3c4d5e6f7a8b-00",
  "errors": { "mobileNumber": ["Mobile number must start with 077, 078 or 079 and have 10 digits."] }
}
```

## Status codes
| Endpoint | Case | Code | Saved status |
|---|---|---|---|
| POST /api/recharge | Provider says SUCCESS | 201 | SUCCESS |
| | Provider declined (amount above 20) | 200 | FAILED |
| | Provider timed out after 5 seconds | 202 | UNKNOWN |
| | Connection broke in the middle of the payment call | 202 | UNKNOWN |
| | Provider returned 503, or could not be reached | 503 | FAILED |
| | Token request failed, payment never sent | 503 | FAILED |
| | Provider returned 401 even after a token refresh | 502 | FAILED |
| | Provider returned 400 or another 4xx | 502 | FAILED |
| | Provider returned 500, or a 200 we cannot read | 502 | UNKNOWN |
| | Bad mobile number or bad amount | 400 | nothing saved |
| | Body is not JSON | 415 | nothing saved |
| | Our database is down | 503 | nothing saved |
| | Unexpected exception | 500 | whatever was saved |
| GET /api/recharge/{id} | Found / not found / id below 1 | 200 / 404 / 400 | |
| GET /api/recharge/history/{mobileNumber} | Valid number / invalid number, page below 1, pageSize above 100 | 200 / 400 | |

A declined payment is HTTP 200 with `status: FAILED`: the request was valid and handled correctly; the business answer was no.

## Business rules
**Mobile number cleaning** (always done before validation, and the cleaned value is used everywhere):
1. Remove spaces, dashes and brackets.
2. A leading `00962` or `+962` is replaced by `0`.
3. The result must match `^07[789][0-9]{7}$`.

| Input | Result |
|---|---|
| 0791234567 | 0791234567 |
| 00962791234567 | 0791234567 |
| +962791234567 | 0791234567 |
| 079 123 4567 | 0791234567 |
| 0701234567 | rejected (400) |

**Transaction id:** generated by the database (`TXN-` plus the identity number plus 1000). The caller cannot choose it.

**Amount:** greater than 0, at most 1000, at most 2 decimals.

**Status lifecycle:** a recharge is saved as PENDING before the provider is called, then moves exactly once to SUCCESS, FAILED or UNKNOWN. UNKNOWN means the payment may have gone through: it is never retried blindly, so there is no double charge.

## Logging
Serilog writes to the console and to Seq (http://localhost:5341). Every request has a correlation id (`X-Correlation-Id`) that Zain forwards to the provider. Mobile numbers are masked (0791***567). Secrets are never logged. Useful searches:
```
TransactionId = 'TXN-1001'
CorrelationId = '<id from any line>'
@Level = 'Error'
```

## Testing
- Unit tests: `dotnet test`
- Scenario tests: import `postman/ZainRecharge.postman_collection.json` and `postman/Local.postman_environment.json` into Postman, select the Local environment, run the folders in order.
- Evidence: screenshots in the `evidence` folder.
- Database script: `docs/schema.sql`

## Assumptions
- Declined payment is returned as 200 with status FAILED (not 422).
- A mobile number written with +962 is treated like 00962.
- The maximum recharge amount is 1000; the provider declines amounts above 20.
- The provider treats a repeated `transactionId` as the same payment.
- The Zain API itself has no authentication in this assignment.

## Known limitations
- No reconciliation job yet: rows left PENDING or UNKNOWN are not resolved automatically (the provider's status inquiry endpoint is ready for it).
- No authentication on the Zain API.
- No retry of timed-out payments, by design.

