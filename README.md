# MonitovoPDF

A self-hostable HTTP service for generating PDF documents, built on ASP.NET Core.

> **Status: early development.** The project is at the scaffolding stage. The
> service currently exposes a single placeholder endpoint, and the PDF
> generation API is not implemented yet. Expect breaking changes.

## Goals

* A small, self-contained HTTP API for turning source content into PDF documents.
* No external service dependencies and no per-document licensing costs.
* Straightforward to run locally, in Docker, or behind a reverse proxy.
* Permissively licensed so it can be embedded in commercial products.

## Requirements

* [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Running locally

```bash
git clone https://github.com/jwvalentine/MonitovoPDF.git
cd MonitovoPDF
dotnet run
```

The service listens on `http://localhost:5155` by default. Ports are defined in
`Properties/launchSettings.json` for local development, and can be overridden in
any environment with the standard ASP.NET Core `ASPNETCORE_URLS` variable.

Verify it is up:

```bash
curl http://localhost:5155/
```

## Configuration

Configuration follows the standard ASP.NET Core layering, in increasing order of
precedence:

1. `appsettings.json` for defaults and non-secret structural settings.
2. `appsettings.{Environment}.json` for per-environment overrides.
3. Environment variables, using `__` as the separator for nested keys.

Do not put credentials in the `appsettings` files. Supply them as environment
variables instead.

## Contributing

Issues and pull requests are welcome. For anything substantial, please open an
issue first so the approach can be discussed before you spend time on it.

## License

Released under the [MIT License](LICENSE).
