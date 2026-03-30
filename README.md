# Finora API

ASP.NET Core Web API with Clean Architecture and JWT Authentication.

## Project Structure (Clean Architecture)

```
src/
├── Finora.Domain/        # Entities, value objects (no dependencies)
├── Finora.Application/  # Use cases, interfaces, DTOs
│   ├── DTOs/
│   ├── Interfaces/
│   └── Options/
├── Finora.Infrastructure/ # Data access, external services
│   ├── Persistence/
│   ├── Repositories/
│   └── Services/
└── Finora.Api/           # Web API, controllers
    └── Controllers/
```

## Requirements

- .NET 9 SDK
- PostgreSQL (Supabase)

## Setup

1. **Configure secrets** (recommended for production):
   ```bash
   cd src/Finora.Api
   dotnet user-secrets set "ConnectionStrings:Finora" "postgresql://postgres:YOUR_PASSWORD@db.wypsmmjulsjtevoamvml.supabase.co:5432/postgres"
   dotnet user-secrets set "Jwt:Secret" "your-secure-32-char-minimum-secret-key"
   ```

2. Or edit `appsettings.Development.json` and replace `[YOUR-PASSWORD]` with your Supabase database password.

## Run

```bash
cd src/Finora.Api
dotnet run
```

API: http://localhost:5000 | Swagger: http://localhost:5000/swagger

## Auth Endpoints

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/auth/register` | No | Register new user |
| POST | `/api/auth/login` | No | Login, returns JWT |
| GET | `/api/auth/me` | Bearer | Get current user |

## Monthly PDF reports (Playwright)

Monthly reports are rendered with **Playwright** (Chromium). After the first `dotnet build`, install browsers once (from the API output folder or project directory):

```bash
cd src/Finora.Api
dotnet build
pwsh bin/Debug/net9.0/playwright.ps1 install chromium
```

On Windows you can also run `playwright.ps1` from `bin/Debug/net9.0/` after build. PDFs are stored under `uploads/reports/` (ignored by git).

In **Development**, you can trigger a report manually: `POST /api/reports/generate?year=2025&month=3` (Bearer token, Pro/Couple household).

## Reports API

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/reports` | Bearer | List PDF metadata (optional `?year=&month=`) |
| GET | `/api/reports/{id}/download` | Bearer | Download PDF |
| POST | `/api/reports/generate` | Bearer | **Development only** — generate for `year`/`month` |
