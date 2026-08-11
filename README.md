# JobTimeline.NET

JobTimeline.NET is an early-stage .NET library for diagnosing background jobs. Its goal is to help an application answer two operational questions safely: where is this job now, and why did it fail?

## Planned architecture

The planned design keeps a queue-neutral core, with durable event history and a current-state projection. Rebus is intended as the first adapter, with SQL Server persistence and optional ASP.NET Core integration following it. The repository does not yet implement this architecture.

## Status

This project is pre-release and is not published to NuGet. Its public API, package layout, and operational guarantees are not established. Do not depend on it in production.

## Build locally

Requires the .NET 8 SDK:

```powershell
dotnet build JobTimeline.sln
```

Run the test suite with:

```powershell
dotnet test JobTimeline.sln
```

## Contributing

Contributions are welcome while the project takes shape. Please read [CONTRIBUTING.md](CONTRIBUTING.md), discuss material design changes before implementation, and include focused tests when behavior is added.

## Security

Please report potential vulnerabilities privately as described in [SECURITY.md](SECURITY.md). Do not open public issues for sensitive reports.

## License

Licensed under the [MIT License](LICENSE).
