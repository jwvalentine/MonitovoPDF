# MonitovoPDF

A .NET library that fills template PDFs with text, images and barcodes — in process, with no
per-document licensing.

> **Status: 1.0.** The public API is settled and pinned by an approval test — breaking it now
> requires a major version. Every symbology except MSI and Plessey is verified end to end by
> rendering it, rasterising with two independent renderers at 300 and 203 dpi, and decoding it
> back. What that does not establish is *your* content at *your* size on *your* printer: print
> one and scan it before committing a label design to production.

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
dotnet add package MonitovoPDF
```

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

**A field that appears more than once is filled everywhere it appears.** That covers both shapes a
template can take: one field shown in several places, and separate fields sharing a name.

When one set of values feeds templates that do not all carry every field, that strictness gets in
the way. `OnMissingField` relaxes it, and `FillWithReport` tells you what did not land:

```csharp
var result = MonitovoPdf.FillWithReport(template, fill =>
{
    fill.SetText("part_number", "WIDGET-4471");
    fill.SetText("only_on_some_templates", "…");
}, new RenderingOptions { OnMissingField = MissingFieldBehaviour.Ignore });

// result.Pdf, and result.UnmatchedFields naming anything the template did not define.
```

Ignoring silently would turn the wrong template into a plausible-looking wrong document, so the
names that did not match always come back rather than being swallowed.

### Templates whose placeholders are images

Not every template marks its placeholders with form fields. A large class of them — particularly
those authored for commercial fill libraries — use ordinary **image XObjects** instead: the page
draws an image at a fixed spot, and filling means exchanging that image for another. Those
templates are often customer-owned or contractually fixed, so re-authoring them is not an option.

They can be filled by addressing a placeholder's position on the page:

```csharp
byte[] pdf = MonitovoPdf.Fill(templateBytes, fill =>
{
    fill.SetImageAt(1, 1, logoBytes);                              // page 1, first placeholder
    fill.SetBarcodeAt(1, 2, BarcodeType.Code128, "WIDGET-4471");   // page 1, second
});
```

**The replacement inherits the placeholder's geometry exactly.** Only the image is exchanged; the
page's own drawing instructions are untouched, so the replacement lands where the placeholder did,
at its size. A replacement of different proportions is **stretched to fill** the placeholder rather
than fitted or letterboxed — the geometry belongs to the template, not to the image.

Placeholders not addressed are left completely alone, which matters because templates routinely
carry fixed artwork in slots a caller has no interest in.

`SetBarcodeAt` keeps bars as vector graphics rather than substituting a picture, so edges stay
exact at any resolution while still inheriting the placeholder's position and size.

**Numbering.** Placeholders are numbered from 1 in order of their PDF resource name, with embedded
numbers compared as numbers — `/Im2` comes before `/Im10`, not after. A resource dictionary has no
order of its own, so the rule has to be stated and never vary: a different order would silently
swap one placeholder's content with another's, and the document would still render.

Do not guess the numbering. `Inspect` reports it:

```csharp
foreach (var image in MonitovoPdf.Inspect(templateBytes).Pages[0].Images)
    Console.WriteLine($"{image.Index}: {image.ResourceName} {image.PixelWidth}x{image.PixelHeight}");
```

### Reading a template

`Inspect` reports a template's pages and fields without filling anything — the page size, what
each field is called, where it sits, and how it asks to be drawn:

```csharp
var info = MonitovoPdf.Inspect(templateBytes);

foreach (var field in info.Fields)
    Console.WriteLine($"{field.Name} {field.Kind} {field.FontFamily} {field.FontSizePoints}pt");

var page = info.Pages[0];
if (Math.Abs(page.WidthMillimetres - 100) > 0.5)
    throw new InvalidOperationException("This template is not the size we print.");
```

That is the answer to "why did nothing appear?" — usually a field named something other than what
was assumed — and it is how to reject a template that is the wrong size up front rather than
stretching it to fit.

### Text across several lines

A value containing line breaks is drawn as several lines, and a field the template flags as
multiline wraps on word boundaries. `TextOptions.Multiline` decides explicitly when neither
applies. Wrapped text shrinks to fit the field's height as well as its width, and stops at the
bottom edge rather than drawing outside the field.

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

Text is drawn in **the font, size and alignment the field's default-appearance string asks for**. A
form laid out for Helvetica and drawn in something wider would wrap or shrink where the designer
expected it to fit, so the template's intent wins over `DefaultFontFamily`. That default applies
only when the template names no font, or names one the host does not have — a missing font
substitutes rather than failing the render.

When a value's appearance genuinely belongs to the caller rather than the document, `TextOptions`
overrides any of it for one field:

```csharp
fill.SetText("part_number", "WIDGET-4471",
    new TextOptions { FontSizePoints = 18, Alignment = TextAlignment.Centre });
```

Prefer changing the template where you can. Appearance living in the template is what lets whoever
designs it control how the document looks without a code change.

The base-14 PDF fonts map to what a host is likely to actually have: Helvetica to Arial, Times to
Times New Roman, Courier to Courier New. Those are defined to be substituted rather than embedded,
and most templates do **not** embed a font for an empty field — they name one and expect the
renderer to supply it, which is why configuring fonts matters.

Text shrinks to fit rather than clipping, down to the floor. Images scale to fit their field and
centre, keeping aspect ratio, using pixel dimensions so a DPI value embedded in the image cannot
change how large it lands.

### Fonts

**On Windows the host's installed fonts are used automatically**, so nothing is needed to get
started. On Linux, PDFsharp's cross-platform build loads no fonts at all and a slim container
ships none, so text will fail to draw until you configure some. Two ways:

```csharp
// Your own fonts. Preferred: the template's layout was designed around particular metrics.
MonitovoPdf.UseFontDirectory("/usr/share/fonts/truetype/dejavu", fallbackFamily: "DejaVuSans");

// Or the font embedded in this package, for a host that has none at all.
MonitovoPdf.UseBundledFonts();
```

`UseBundledFonts` draws everything in DejaVu Sans, whatever the template asked for. It is a
working last resort rather than a substitute for real font configuration: DejaVu's metrics are
not Arial's, so text occupies a different width than the designer saw and shrink-to-fit may
engage where it did not before.

**Configure fonts once, at start-up.** PDFsharp fixes its font resolver the first time one is
used and will not accept another afterwards. Calling either method after a render throws.

If neither is configured and the host has no usable fonts, the first render throws a
`TemplateRenderException` naming both methods, rather than failing somewhere inside the PDF
engine.

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

**Symbology check characters are calculated; data check digits are not.** Code 128, Code 93 and
the like carry an internal check character that is part of the encoding, and it is generated for
you — nothing to do. What is *not* calculated is a check digit belonging to the **data**: the last
digit of an EAN, UPC or GS1 number, or of an ITF-14. Supply those yourself, or the result is a
barcode that scans cleanly and carries the wrong number.

### Printing the value under the bars

A barcode's value printed as readable text below it is what somebody falls back to when a scanner
is not to hand or the symbol has been scuffed: they read the number off the label and key it in.
A label carrying its number only as bars has no fallback at all.

```csharp
fill.SetBarcode("barcode", BarcodeType.Code128, "47028538", new BarcodeOptions { ShowValue = true });
fill.SetBarcodeAt(1, 2, BarcodeType.Code128, "47028538", new BarcodeOptions { ShowValue = true });
```

It is **off by default**, because it changes the geometry rather than adding to it. The text is
drawn inside the space the barcode was already given, so the bars give up the height it takes — a
fifth of it by default, adjustable per barcode with `CaptionHeightFraction` or across a render with
`RenderingOptions.BarcodeCaptionHeightFraction`. Shorter bars are marginally harder to scan at an
angle, which makes this a deliberate trade rather than a free improvement.

The text inherits the barcode's own position and rotation, so a barcode a template stood on its end
gets its value turned to match, running alongside the bars. It does **not** inherit the stretch: a
placeholder five times wider than it is tall would otherwise widen every glyph by that same five
times, so each of the placeholder's axes is measured separately and the text drawn at a true point
size. Sizing follows the space reserved unless `CaptionFontSizePoints` says otherwise, and a value
too wide for its bars is shrunk until it fits.

**The text is the value you supplied, not the value as encoded.** Where the symbology added a check
character during encoding, that character is not shown — the number a person reads off the label is
the number they were given to look up, and printing a longer one underneath would send them looking
for something that does not exist. For EAN and UPC, where the specification prescribes both a
grouped layout and the check digit, supply the complete number and expect a single plain line.

### Sizing a barcode field

Quiet zones are included in the symbol rather than assumed around it, so a barcode never loses its
margin to the edge of its field. The consequence is that **a field too small for its content
produces narrow modules rather than a clipped symbol** — it will look fine on screen and fail to
scan.

The narrow module is what decides. A printer cannot render a module thinner than one dot, and a
module under roughly two dots scans unreliably:

| Printer resolution | One dot | Practical minimum module |
|---|---|---|
| 203 dpi | 0.125 mm | ~0.25 mm |
| 300 dpi | 0.085 mm | ~0.17 mm |
| 600 dpi | 0.042 mm | ~0.08 mm |

Two things follow. **Shorter content needs less width** — a Code 128 of eight characters needs
noticeably less room than one of twenty, so sizing a field for the longest value you will ever put
in it is the safe approach. And **a barcode is the one element worth widening the field for**:
text shrinks to fit and stays readable, a barcode shrinks to fit and stops scanning.

The test suite renders every symbology and decodes it back at 300 dpi and at 203 dpi, the latter
being what many thermal label printers rasterise at. That establishes the symbols themselves are
sound at low resolution. It does not establish that *your* field is wide enough for *your* content
on *your* printer — for that, print one and scan it.

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
git tag v1.0.0
git push origin v1.0.0
```

The release workflow takes the version from the tag, builds, tests, packs, pushes to nuget.org and
opens a GitHub release.

It authenticates with [NuGet Trusted
Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing): GitHub issues a
short-lived OIDC token, nuget.org validates it against a policy and returns a key valid for one
hour. No publishing key is stored in this repository, so there is nothing here to leak or rotate.
The only stored value is `NUGET_USER`, the nuget.org profile name, which is an identifier rather
than a credential.

From 1.0, a breaking change to the public surface requires a major bump. The approval test's
baseline is the definition of that surface, so a break shows up as a diff in review rather than
being discovered by a consumer.

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
