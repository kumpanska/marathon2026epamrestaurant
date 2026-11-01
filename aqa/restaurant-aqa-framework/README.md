# Restaurant AQA framework

**Direction: Backend. Approach: BDD (Reqnroll/Gherkin) + Screenplay.** The suite uses the browser for customer and waiter workflows, with API calls for test-data setup and state verification.

End-to-end tests for the Green & Tasty restaurant application: Reqnroll (Gherkin) + TUnit + Playwright for .NET, with the Screenplay pattern.

## Group pre-order (Levels 1 and 2)

`Features/GroupPreOrder.feature` covers participant baskets and totals through the API. `Features/GroupPreOrderConfirmation.feature` exercises guest and owner baskets, owner review and confirmation, and waiter verification through the browser; API calls are limited to test-data setup and state verification. Confirmation scenarios cover unconfirmed and empty baskets, a missing order, reservation, or group, repeated confirmation, and a non-owner caller. Setup uses the configured test table and its assigned waiter, creates a sample dish, customer owner, reservation, and guest sessions. Unique customer accounts and reservation times are created per scenario; the application currently has no customer-delete endpoint, so cleanup removes deletable guest sessions, dishes, and reservations while unique accounts remain. The table must have an available 90-minute slot in the next 15 days and seat at least four guests.

Group pre-order scenarios require `TESTTABLE_ID`, `TESTTABLE_WAITER_USERNAME`, and `TESTTABLE_WAITER_PASSWORD` in `.env` or CI variables/secrets. These settings identify the existing table and waiter assignment needed by the backend to route a customer reservation. The configured table waiter is also used as the standing waiter account when `WAITER_USERNAME` / `WAITER_PASSWORD` are omitted.

The selected behavior for this suite is:

- An unconfirmed guest with no dishes remains in the participants response with an empty `items` list, a zero subtotal, and `isSubmitted: false`.
- A group with no joined guests returns `participants: []` and `total: 0`.
- Unauthenticated requests receive `401`, authenticated non-owners receive `403`, and a missing group receives `404`.
- The group total equals the sum of all participant subtotals; each subtotal is the sum of dish price multiplied by quantity.
- A confirmed empty basket is allowed, but group confirmation is rejected if the combined confirmed baskets contain no dishes.
- Only the reservation owner may confirm; the order and group reservation are submitted together, and unconfirmed baskets are excluded.

The empty-group scenario is tagged `@known-defect`: the current backend creates an owner guest session with a reservation, and `GetGroupSessionsAsync` includes that session in `participants`, contrary to the required empty-group response. Remove the tag when the API is changed to return only joined guests for this case.

Guidance for extending the suite (writing Gherkin, steps, Screenplay, test data, working with an AI assistant) is in [CLAUDE.md](CLAUDE.md).

## Run the tests

```bash
cp .env.example .env        # then fill in your own values (see "Configuration")
dotnet build
dotnet run --no-build -- --report-trx                          # everything
dotnet run --no-build -- --treenode-filter "/*/*/*/*[Category=smoke]"   # by tag
```

Everything a run produces lands in `TestResults/`: the TUnit report and TRX, the Reqnroll report in `reqnroll/`, per-scenario browser timing reports in `performance/`, and failure traces in `traces/` (open one with `npx playwright show-trace <file>.zip`). Performance JSON includes page-load and API-request durations with their configured thresholds. Failed UI scenarios retain screenshots and network/action details in their Playwright traces.

Some scenarios are expected to fail (`@known-defect`, bugs in the application) or are skipped (`@wip`, no admin account configured). That is intentional.

## Quality gates

| Where | What runs |
|---|---|
| Git hooks (local) | `pre-commit`: `dotnet format` check and Gherkin lint on staged files · `commit-msg`: Conventional Commits · `pre-push`: build with analyzers |
| `aqa-quality.yml` | on every push and pull request: no generated files tracked, format, build (warnings are errors), Gherkin lint, test discovery |
| `aqa-tests.yml` | the scenarios themselves, nightly and on demand (Actions → AQA Tests → Run workflow); reports and traces upload even when scenarios fail |

Enable the hooks once per clone, from the repository root:

```bash
dotnet tool restore --tool-manifest aqa/restaurant-aqa-framework/.config/dotnet-tools.json
git config core.hooksPath aqa/restaurant-aqa-framework/.husky
```

The hooks only act on commits and pushes that touch `aqa/restaurant-aqa-framework`. Gherkin lint needs Node.js.

## Configuration

There is no shared environment and no committed `.env`: copy `.env.example` to `.env` and fill it in with the data of **your own** deployment (see "Testing your own deployment"). `.env` is git-ignored; never commit it. `API_URL` is required; `UI_URL` can remain empty for a backend-only deployment, in which case UI scenarios fail fast with a clear configuration message.

Settings come from `.env` or real environment variables (variables win):

| Setting | Meaning |
|---|---|
| `UI_URL` | where the frontend is served; optional for API-only deployments |
| `API_URL` | base URL of the backend API, including the stage (for example `…/develop`) |
| `WAITER_USERNAME`, `WAITER_PASSWORD` | a waiter account that already exists in the environment under test |
| `TESTTABLE_ID`, `TESTTABLE_WAITER_*` | a table and the waiter assigned to it (only for scenarios that need them) |
| `ADMIN_USERNAME`, `ADMIN_PASSWORD` | an admin account; without it the admin scenarios are skipped |
| `PERF_PAGE_LOAD_MS`, `PERF_API_RESPONSE_MS` | page-load and API-request performance budgets included in each UI scenario's JSON report |

The full list, with defaults, is in `.env.example`.

## Testing your own deployment

### If you deployed the backend / frontend / DevOps homework

1. `cp .env.example .env` and set `UI_URL` and `API_URL` to your deployment (API URL includes the stage, for example `…/develop`).
2. Waiters cannot be created by signing up, so create a waiter (and, for the admin scenarios, an admin) in *your* environment and put its credentials in `WAITER_*` / `ADMIN_*`. Optionally add a table and its assigned waiter (`TESTTABLE_*`).
3. Run `dotnet build` and a small slice first (`--treenode-filter "/*/*/*/*[Category=smoke]"`).

### If you have not deployed your own homework

If you deployed the original application, test that: set `UI_URL` / `API_URL` as above. Otherwise point the framework at a comparable public site (Booking, Airbnb, OpenTable, …) and write all the layers for it yourself: Gherkin features, step definitions, Screenplay tasks and questions, page objects with locators, and API clients where the site has a usable API. Use only flows you are allowed to automate (no purchases, no real bookings, respect the site's terms), and set `UI_URL` to that site.

1. **Generate a skeleton with Playwright codegen** (see "Presetup instructions"): record one flow, for example search → open a result → check details. Save the generated code to a scratch file, it is only raw material.
2. **Split it across the layers**: locators and small actions go to `Ui/` page objects, the flow becomes a task in `Screenplay/Tasks/`, the read-backs become questions in `Screenplay/Questions/`, the business wording goes to `Features/` and thin glue to `StepDefinitions/`.
3. **Replace weak locators.** Codegen often emits positional or text-fragile selectors. Prefer role + accessible name, label, test id, then CSS (see [CLAUDE.md](CLAUDE.md)); check them in the Playwright inspector.
4. Tag scenarios `@ui` / `@api`, keep them independent, and run `npx gherkin-lint -c .gherkin-lintrc "Features/*.feature"`.

### CI on your own public repository

Publish your work in your own public Git repository (the link is the deliverable). Workflows are disabled in a fresh fork until you enable them. Add the same values under Settings → Secrets and variables → Actions:

- **Variables:** `AQA_UI_URL`, `AQA_API_URL`, `AQA_TESTTABLE_ID`
- **Secrets:** `AQA_WAITER_USERNAME`, `AQA_WAITER_PASSWORD`, `AQA_ADMIN_USERNAME`, `AQA_ADMIN_PASSWORD`, `AQA_TESTTABLE_WAITER_USERNAME`, `AQA_TESTTABLE_WAITER_PASSWORD`

`aqa-quality.yml` runs on every push and pull request; `aqa-tests.yml` (nightly and on demand) tests whatever those variables point at. Never put credentials in the repository, in `.env.example` or in test code.

## Presetup instructions

This framework contains PlaywriteInstaller script so you don't need to setup it manually.
For full BDD highlighting in your IDE you may need to install Reqnroll extension https://docs.reqnroll.net/latest/ide-integrations/index.html
To start the Playwright code generator open Powershell in the framework directory and enter the line:
```bash
bin/Debug/net10.0/playwright.ps1 codegen "<your UI_URL or the public site you test>"
```
Build first so `playwright.ps1` exists. The URL is optional.
