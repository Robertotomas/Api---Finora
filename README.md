# Finora API

ASP.NET Core Web API with Clean Architecture.

## Project Structure (Clean Architecture)

```
src/
├── Finora.Domain/        # Entities, value objects (no dependencies)
├── Finora.Application/  # Use cases, interfaces, DTOs
│   ├── DTOs/
│   ├── Interfaces/
│   └── Services/
├── Finora.Infrastructure/ # Data access, external services
│   ├── Persistence/
│   └── Repositories/
└── Finora.Api/           # Web API, controllers
    └── Controllers/
```

## Requirements

- .NET 9 SDK

## Run

```bash
cd src/Finora.Api
dotnet run
```

API will be available at http://localhost:5000
Swagger UI at http://localhost:5000/swagger
