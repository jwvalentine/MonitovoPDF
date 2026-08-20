# MonitovoPDF

A .NET library that fills template PDFs with text, images and barcodes — in process, with no
per-document licensing.

> **Status: early development.** The template-filling path described below works and is tested.
> Expect breaking changes to the API before a 1.0 release.

It exists to replace the commercial PDF components that do this — Aspose.PDF, IronPDF and
similar — for the narrow job of populating a template. Those run inside your application at
runtime, which is what this does; they are also expensive per document and restrictively licensed
for redistribution, which this is not.

## What it does

You supply a template PDF whose placeholders are ordinary AcroForm fields, plus the values to put
in them. The library draws your values onto the page at the position each field occupies, removes
the fields, and hands back a flat PDF.

The output is flat by design. Filling form fields and leaving them interactive relies on the
viewer generating field appearances, and many do not — a label filled that way prints blank in
several common viewers and print paths. Drawing into the page content stream instead produces a
document that renders identically everywhere.

## Install

```bash
dotnet add package MonitovoPDF --prerelease
```

The `--prerelease` flag is needed for now: releases carry a pre-release suffix while the API is
still settling, and will drop it at 1.0.

Targets `net8.0` and `net10.0`. Two dependencies, neither copyleft and neither bringing
dependencies of its own: [PDFsharp](https://github.com/empira/PDFsharp) (MIT) and
[ZXing.Net](https://github.com/micjahn/ZXing.Net) (Apache-2.0).

## Using it

```csharp
using MonitovoPDF;

byte[] pdf = MonitovoPdf.Fill(templateBytes, fill =>
{
    fill.SetText("part_number", "WIDGET-4471");
    fill.SetText("description", "Stainless bracket, 40mm");
    fill.SetImage("logo", logoBytes);
    fill.SetBarcode("barcode", BarcodeType.Code128, "WIDGET-4471");
});
```

The string keys are the names of the form fields in your template. Overloads take a `Stream` or a
file path, and `FillFile(templatePath, outputPath, ...)` writes the result straight to disk.

Every named field must exist in the template. If one does not, the whole call throws rather than
returning a partly populated document, and the message names the fields at fault. A field given
more than one value is refused for the same reason.

```csharp
try
{
    var pdf = MonitovoPdf.Fill(template, fill => fill.SetBarcode("barcode", BarcodeType.Itf, "ABC"));
}
catch (TemplateRenderException exception)
{
    // "The value for field 'barcode' is not valid for itf: Requested contents should only
    //  contain digits, but got 'A'"
}
```

`TemplateRenderException` is the one to catch: it means the input was rejected. Anything else
escaping a call is a fault. Where the underlying cause carries detail, it is kept as
`InnerException`.

### Limits

`RenderingOptions` bounds every operation, because a template is often untrusted even in process.
Pass an instance to any call to change them.

| Setting | Default | Purpose |
|---|---|---|
| `MaxTemplateBytes` | 5 MB | Largest accepted template. |
| `MaxImageBytes` | 2 MB | Largest accepted image. |
| `MaxFieldCount` | 100 | Most fields one render may populate. |
| `MaxTextLength` | 4096 | Longest single text or barcode value. |
| `MaxPages` | 10 | Templates with more pages are rejected. |
| `DefaultFontFamily` | `Arial` | Font used to draw text. |
| `DefaultFontSizePoints` | 10 | Size used when a field does not specify one. |
| `MinimumFontSizePoints` | 5 | Floor that shrink-to-fit will not go below. |

Text is drawn at the size the field's default-appearance string asks for, shrinking to fit rather
than clipping, down to the floor. Images scale to fit their field and centre, keeping aspect
ratio, using pixel dimensions so a DPI value embedded in the image cannot change how large it
lands.

### Fonts

**On Windows the host's installed fonts are used automatically**, so nothing is needed to get
started. On Linux, PDFsharp's cross-platform build loads no fonts at all and a slim container
ships none, so text will fail to draw until you point the library at some:

```csharp
MonitovoPdf.UseFontDirectory("/usr/share/fonts/truetype/dejavu", fallbackFamily: "DejaVuSans");
```

Face names come from file names, so `Arial.ttf` serves the `Arial` family, with optional `-Bold`,
`-Italic` and `-BoldItalic` suffixes.

⚠️ **This changes process-wide state.** PDFsharp resolves fonts through a single global hook, so a
resolver installed here applies to everything in the process that uses PDFsharp, not only to this
library. If your application already uses PDFsharp with its own resolver, `UseFontDirectory`
throws rather than silently displacing it; pass `force: true` to take over deliberately. Call it
once at start-up.

## Barcodes

Barcodes are drawn as **vector** graphics, not rasterised, so the bar edges stay exact at any
print resolution. A scaled bitmap can blur enough at a label printer's resolution to cost a scan.
Linear symbologies fill their field; 2D symbologies are fitted and centred. The quiet zone is
included in the symbol, so a template author does not have to leave room for it.

| `BarcodeType` | Symbology | Accepts |
|---|---|---|
| `Code128` | Code 128 | full ASCII |
| `Code39` | Code 39 | uppercase, digits, `- . $ / + %`, space |
| `Code93` | Code 93 | uppercase, digits, some punctuation |
| `Codabar` | Codabar | digits, with `A`–`D` start/stop characters |
| `Itf` | Interleaved 2 of 5 | digits, even count |
| `Ean13` | EAN-13 | 12 or 13 digits |
| `Ean8` | EAN-8 | 7 or 8 digits |
| `UpcA` | UPC-A | 11 or 12 digits |
| `UpcE` | UPC-E | 7 or 8 digits |
| `Msi` | MSI | digits |
| `Plessey` | Plessey | digits |
| `QrCode` | QR Code | any text |
| `DataMatrix` | Data Matrix | any text |
| `Aztec` | Aztec | any text |
| `Pdf417` | PDF417 | any text |

**No check digit is calculated.** A value is encoded exactly as given, so a caller using a
symbology that carries one — the retail codes, or ITF-14 — must supply a correct digit, or the
result is a barcode that scans cleanly and carries the wrong number.

Every symbology except MSI and Plessey is verified end to end: rendered, rasterised, and read back
by an independent decoder that must agree on both the symbology and the value. MSI and Plessey
encode and render, but no freely available decoder in the test image can read them, so they are
not scan-verified. Confirm them against your own scanners before relying on them.

For a symbology that is not listed, generate the image yourself and pass it to `SetImage`.

## Preparing a template

Any tool that can add named form fields to a PDF will do — Acrobat and LibreOffice Draw both work.
Place a field where each value belongs, size it to the space the value may occupy, and give it a
name. That name is the key you pass to `SetText`, `SetImage` or `SetBarcode`. The field type does
not matter; only its name, position and size are used.

## Running it as a service

`MonitovoPDF.Server` is an optional ASP.NET Core host for callers that want this over HTTP rather
than in process. It is not required, and the library does not depend on it.

```bash
docker build -t monitovopdf .
docker run --rm -p 8080:8080 monitovopdf
```

`POST /v1/labels` takes the same values as JSON, with the template and images base64 encoded, and
returns `application/pdf`:

```json
{
  "template": "<base64-encoded template PDF>",
  "fields": { "part_number": "WIDGET-4471" },
  "images": { "logo": "<base64-encoded PNG>" },
  "barcodes": { "barcode": { "type": "code128", "value": "WIDGET-4471" } }
}
```

`GET /health` returns `200`. The service stores nothing and never reaches the network. Sending the
result to a printer is the caller's job.

Configuration follows the standard ASP.NET Core layering — `appsettings.json`, then
`appsettings.{Environment}.json`, then environment variables with `__` for nested keys. The
`Rendering` section maps to `RenderingOptions` above; the `Server` section adds `MaxRequestBytes`
and `RenderTimeoutMilliseconds`, which bound the network boundary rather than the render. Do not
put credentials in the `appsettings` files.

The image installs DejaVu and sets the font directory, so text draws without further
configuration.

**There is no authentication.** Do not expose the service to an untrusted network as it stands.

## Tests

```bash
dotnet test
```

Fixtures are synthetic PDFs built in code, so the repository carries no binary documents.

### End-to-end check

The unit tests render templates they synthesise themselves, which proves the renderer but not that
a template from a real authoring tool can be filled. The `integration/` container closes that gap:
LibreOffice builds PDF forms through its own API, the service fills them, poppler reads the text
back, and zbar, libdmtx and zxing-cpp read the barcodes back.

```bash
docker compose -f integration/docker-compose.yml up --build --abort-on-container-exit
```

Artefacts land in `integration/out/`. The run exits non-zero if any check fails.

### The public API is pinned

`MonitovoPDF.Tests/PublicApi.approved.txt` holds a rendering of the library's entire public
surface, and a test fails if the two drift apart. Once the package is published that surface is a
contract, and this makes an accidental break show up in review as a diff rather than in a
consumer's build. When a change is intended, the failure names a `.received.txt` file to copy over
the approved one — reviewing that diff is the point.

## Versioning and releases

The package follows [Semantic Versioning](https://semver.org). A release is cut by pushing a tag:

```bash
git tag v0.2.0
git push origin v0.2.0
```

The release workflow takes the version from the tag, builds, tests, packs, pushes to nuget.org and
opens a GitHub release.

It authenticates with [NuGet Trusted
Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing): GitHub issues a
short-lived OIDC token, nuget.org validates it against a policy and returns a key valid for one
hour. No publishing key is stored in this repository, so there is nothing here to leak or rotate.
The only stored value is `NUGET_USER`, the nuget.org profile name, which is an identifier rather
than a credential.

While the project is pre-1.0 the usual 0.x caveat applies: a minor bump may break the API. From
1.0, a breaking change to the public surface requires a major bump.

## Contributing

Issues and pull requests are welcome. For anything substantial, please open an issue first so the
approach can be discussed before you spend time on it.

Security problems are different: please report them privately rather than in an issue. See
[SECURITY.md](SECURITY.md).

## License

Released under the [MIT License](LICENSE).

Third-party works redistributed with the library, and their licences in full, are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), which ships inside the NuGet package and the
container image because several of those licences require the notice to travel with the copies.
That file also records a copyleft audit: nothing GPL, LGPL or AGPL is compiled into, linked into,
or derived from this library.
