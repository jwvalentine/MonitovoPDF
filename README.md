# MonitovoPDF

A self-hostable HTTP service that fills template PDFs with your data, built on ASP.NET Core.

> **Status: early development.** The template-filling path described below works and is tested.
> Expect breaking changes to the API shape before a 1.0 release.

## What it does

You supply a template PDF whose placeholders are ordinary AcroForm fields, plus the text and
images to put in them. The service draws your values onto the page at the position each field
occupies, removes the fields, and returns a flat PDF.

The output is flat by design. Filling form fields and leaving them interactive would rely on the
viewer generating field appearances, and many do not — a label filled that way prints blank in
several common viewers and print paths. Drawing into the page content stream instead produces a
document that renders identically everywhere.

## Goals

* One job, done well: template plus data in, finished PDF out.
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
`Properties/launchSettings.json` for local development, and can be overridden in any environment
with the standard ASP.NET Core `ASPNETCORE_URLS` variable.

```bash
curl http://localhost:5155/health
```

## API

### `POST /v1/labels`

Fills a template and returns the finished document as `application/pdf`.

```json
{
  "template": "<base64-encoded template PDF>",
  "fields": {
    "part_number": "WIDGET-4471",
    "description": "Stainless bracket, 40mm"
  },
  "images": {
    "logo": "<base64-encoded PNG>"
  },
  "barcodes": {
    "barcode": { "type": "code128", "value": "WIDGET-4471" }
  }
}
```

* `template` — the template PDF, base64 encoded. The service stores nothing, so the template
  travels with each request and stays wherever you already keep it.
* `fields` — text values, keyed by the name of the template field to draw them into.
* `images` — images, base64 encoded, keyed by the name of the template field to draw them into.
* `barcodes` — barcodes for the service to generate, keyed the same way.

Every named field must exist in the template. If any name is unknown the whole request fails with
`400` rather than returning a partly populated document, and the response names the fields at
fault. A field given more than one value is rejected for the same reason.

Text is drawn at the size the template field asks for in its default-appearance string, and shrunk
to fit if the value is too wide, down to the configured floor. Images are scaled to fit their
field and centred, preserving aspect ratio.

Sending the result to a printer is the caller's job. The service returns bytes and never reaches
out to the network.

### Barcodes

Barcodes are drawn as **vector** graphics rather than rasterised, so the bar edges stay exact at
any print resolution. A scaled bitmap can blur enough at a label printer's resolution to cost a
scan. Linear symbologies fill their field; 2D symbologies are fitted and centred, keeping their
aspect ratio. The quiet zone is included in the symbol, so the template author does not have to
leave room for it around the field.

| `type` | Symbology | Accepts |
|---|---|---|
| `code128` | Code 128 | full ASCII |
| `code39` | Code 39 | uppercase, digits, `- . $ / + %`, space |
| `code93` | Code 93 | uppercase, digits, some punctuation |
| `codabar` | Codabar | digits, with `A`–`D` start/stop characters |
| `itf` | Interleaved 2 of 5 | digits, even count |
| `ean13` | EAN-13 | 12 or 13 digits |
| `ean8` | EAN-8 | 7 or 8 digits |
| `upca` | UPC-A | 11 or 12 digits |
| `upce` | UPC-E | 7 or 8 digits |
| `msi` | MSI | digits |
| `plessey` | Plessey | digits |
| `qr` | QR Code | any text |
| `datamatrix` | Data Matrix | any text |
| `aztec` | Aztec | any text |
| `pdf417` | PDF417 | any text |

A value the symbology cannot represent is rejected with `400` and an explanation — several are
digits-only, and the retail symbologies require an exact length.

Every symbology except MSI and Plessey is verified end to end by the integration suite: rendered,
rasterised, and read back by an independent decoder that must agree on both the symbology and the
value. MSI and Plessey encode and render, but no freely available decoder in the test image can
read them, so they are not scan-verified. Confirm them against your own scanners before relying
on them.

If you need a symbology that is not listed, or a check digit computed for you, generate the image
yourself and send it in `images` instead. The service does not compute check digits: a value is
encoded as given.

### `GET /health`

Returns `200` with `{"status":"ok"}`.

## Preparing a template

Any tool that can add named form fields to a PDF will do — Acrobat and LibreOffice Draw both work.
Place a field where each value belongs, size it to the space the value may occupy, and give it a
name. That name is the key you send in `fields` or `images`. The field type does not matter; only
its name, position and size are used.

## Configuration

Configuration follows the standard ASP.NET Core layering, in increasing order of precedence:

1. `appsettings.json` for defaults and non-secret structural settings.
2. `appsettings.{Environment}.json` for per-environment overrides.
3. Environment variables, using `__` as the separator for nested keys.

Do not put credentials in the `appsettings` files. Supply them as environment variables instead.

The `Rendering` section bounds every operation. A service that turns untrusted input into
documents needs explicit ceilings, or one request becomes a denial of service.

| Setting | Default | Purpose |
|---|---|---|
| `MaxRequestBytes` | 16777216 | Largest accepted request body, enforced before buffering. |
| `MaxTemplateBytes` | 5242880 | Largest accepted template, after base64 decoding. |
| `MaxImageBytes` | 2097152 | Largest accepted image, after base64 decoding. |
| `MaxFieldCount` | 100 | Most fields one request may populate. |
| `MaxTextLength` | 4096 | Longest single text value. |
| `MaxPages` | 10 | Templates with more pages than this are rejected. |
| `RenderTimeoutMilliseconds` | 15000 | Ceiling on how long a caller waits for a render. |
| `DefaultFontFamily` | `Arial` | Font used to draw text. |
| `DefaultFontSizePoints` | 10 | Size used when a field does not specify one. |
| `MinimumFontSizePoints` | 5 | Floor that shrink-to-fit will not go below. |
| `FontDirectory` | *(empty)* | Directory of `.ttf` files to draw text with. |

### Fonts

PDFsharp's cross-platform build loads no fonts on its own, and a Linux container normally has none
installed. **Set `Rendering__FontDirectory` to a directory of `.ttf` files in any Linux
deployment**, or text will fail to draw. Face names come from file names, so `Arial.ttf` serves the
`Arial` family, with optional `-Bold`, `-Italic` and `-BoldItalic` suffixes for those styles.

On Windows the host's installed fonts are used when no directory is configured, which is enough
for local development.

## Running in Docker

```bash
docker build -t monitovopdf .
docker run --rm -p 8080:8080 monitovopdf
```

The image installs DejaVu and points `Rendering__FontDirectory` at it, so text draws without
further configuration.

## Tests

```bash
dotnet test
```

Test fixtures are synthetic PDFs built in code, so the repository carries no binary documents.

### End-to-end check

The unit tests render templates they synthesise themselves, which proves the renderer but not that
a template from a real authoring tool can be filled. The `integration/` container closes that gap:
LibreOffice builds a PDF form through its own API, the service fills it, and poppler reads the
result back to confirm the values are really there.

```bash
docker compose -f integration/docker-compose.yml up --build --abort-on-container-exit
```

The generated template and the rendered label are written to `integration/out/` so they can be
opened and looked at. The run exits non-zero if any check fails.

## Contributing

Issues and pull requests are welcome. For anything substantial, please open an issue first so the
approach can be discussed before you spend time on it.

## License

Released under the [MIT License](LICENSE). The runtime dependencies are
[PDFsharp](https://github.com/empira/PDFsharp) (MIT) for the PDF work and
[ZXing.Net](https://github.com/micjahn/ZXing.Net) (Apache-2.0) for barcode encoding. Neither is
copyleft, and neither brings dependencies of its own.

Third-party works redistributed with the service, and their licences in full, are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). That file is copied into the container image,
because several of those licences require the notice to travel with the copies. The test harness
in `integration/` contains copyleft software (poppler is GPL); it is a local tool, is not linked
into the service and is not published — the notices file explains the distinction.
