# Contributing to BulkSharp

We welcome contributions to BulkSharp! This document provides guidelines for contributing to the project.

## Getting Started

1. Fork the repository
2. Clone your fork locally
3. Create a feature branch from `main`
4. Make your changes
5. Add tests for your changes
6. Ensure all tests pass
7. Submit a pull request

## Development Setup

### Prerequisites

- .NET 8.0 SDK or later
- Visual Studio 2022 or VS Code with C# extension

### Building the Project

```bash
dotnet restore
dotnet build
```

### Running Tests

```bash
dotnet test                              # All tests
dotnet test --filter Category=Unit       # Unit tests only
dotnet test --filter Category=Integration # Integration tests only
```

E2E tests require Docker with SQL Server. They are excluded from CI.

### Running Samples

```bash
dotnet run --project samples/BulkSharp.Sample.UserImport
dotnet run --project samples/BulkSharp.Sample.Dashboard
```

## Project Structure

```
src/
  BulkSharp/                          # Meta-package: entry point, builders, DI
  BulkSharp.Core/                     # Abstractions, domain models, attributes
  BulkSharp.Processing/               # Processing engine, data formats, storage, scheduling
  BulkSharp.Data.EntityFramework/  # EF Core repositories for SQL persistence
  BulkSharp.Files.S3/               # Amazon S3 file storage provider
  BulkSharp.Dashboard/                # Blazor Server monitoring UI (Razor Class Library)

samples/
  BulkSharp.Sample.UserImport/        # Console app with regular and step-based operations
  BulkSharp.Sample.Dashboard/         # ASP.NET Core app with dashboard integration
  BulkSharp.Sample.Production/        # Production-like app with Aspire, EF, S3, OpenTelemetry
  BulkSharp.Sample.Production.AppHost/ # Aspire AppHost orchestrator

tests/
  BulkSharp.UnitTests/
  BulkSharp.IntegrationTests/
  BulkSharp.E2ETests/
  BulkSharp.Dashboard.Tests/
  BulkSharp.ArchitectureTests/
```

## Key Abstractions

| Interface | Purpose |
|-----------|---------|
| `IBulkRowOperation<TMetadata, TRow>` | Define validation and processing for a bulk operation |
| `IBulkPipelineOperation<TMetadata, TRow>` | Multi-step operation with ordered steps and retry |
| `IBulkStep<TMetadata, TRow>` | A single processing step with name and retry count |
| `IBulkMetadata` | Marker interface for operation metadata |
| `IBulkRow` | Marker interface for row data |
| `IBulkOperationService` | Create, query, and cancel operations |
| `IFileStorageProvider` | File storage abstraction |
| `IBulkScheduler` | Operation scheduling abstraction |

## Code Style

- Follow Microsoft's C# coding conventions
- Use the provided `.editorconfig` settings
- Ensure code analysis warnings are addressed
- Add XML documentation for public APIs

## Pull Request Guidelines

1. **Keep PRs focused**: One feature or bug fix per PR
2. **Write clear commit messages**: Use conventional commit format
3. **Add tests**: All new functionality should have corresponding tests
4. **Update documentation**: Update relevant documentation for your changes
5. **Follow the template**: Use the provided PR template

## Commit Message Format

We follow the [Conventional Commits](https://www.conventionalcommits.org/) specification:

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

Types:
- `feat`: A new feature
- `fix`: A bug fix
- `docs`: Documentation only changes
- `style`: Changes that do not affect the meaning of the code
- `refactor`: A code change that neither fixes a bug nor adds a feature
- `perf`: A code change that improves performance
- `test`: Adding missing tests or correcting existing tests
- `chore`: Changes to the build process or auxiliary tools

## Testing Guidelines

- Write unit tests for all business logic
- Write integration tests for end-to-end scenarios
- Use meaningful test names that describe the scenario
- Follow the Arrange-Act-Assert pattern
- Mock external dependencies

## Documentation

- Update README.md for user-facing changes
- Add XML documentation for public APIs
- Include code examples in documentation

## Release Process

1. Update `Version` in `Directory.Build.props`
2. Update CHANGELOG.md
3. Create a release PR
4. Tag the release after merging (e.g., `git tag v0.9.0-beta.2`)
5. CI publishes NuGet packages to GitHub Packages automatically

## Questions?

If you have questions about contributing, please check existing issues or create a new one.