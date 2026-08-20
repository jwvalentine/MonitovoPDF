"""End-to-end check of every barcode symbology the service can draw.

The service generates these itself, so the only honest test is to render a sheet of them,
rasterise the PDF and read the symbols back with decoders that are not the encoder. Three
are used: zbar and libdmtx are independent implementations, and zxing-cpp is a separate C++
implementation of the same algorithms as the C# encoder.

Matching is on symbology *and* value. Matching on value alone gives false positives, because
these test values share digit runs — "1234567" sent as MSI also appears inside the UPC-A
value "012345678905", so a text-only match would report MSI as decoded when nothing read it.
"""

import os
import re
import subprocess

# name, value sent, expected decoded text, decoder format tokens that count as this symbology.
SYMBOLOGIES = [
    ("code128", "CODE128-4471", "CODE128-4471", {"code128"}),
    ("code39", "CODE39-4471", "CODE39-4471", {"code39"}),
    ("code93", "CODE93-4471", "CODE93-4471", {"code93"}),
    ("codabar", "A12345B", "12345", {"codabar"}),
    ("itf", "12345670", "12345670", {"i25", "itf", "interleaved2of5"}),
    ("ean13", "5901234123457", "5901234123457", {"ean13"}),
    ("ean8", "96385074", "96385074", {"ean8"}),
    ("upca", "012345678905", "012345678905", {"upca", "upa"}),
    ("upce", "01234565", "01234565", {"upce"}),
    ("msi", "1234567", "1234567", {"msi"}),
    ("plessey", "12345678", "12345678", {"plessey"}),
    ("qr", "https://example.invalid/qr-4471", "https://example.invalid/qr-4471", {"qrcode", "qr"}),
    ("datamatrix", "DATAMATRIX-4471", "DATAMATRIX-4471", {"datamatrix"}),
    ("aztec", "AZTEC-4471", "AZTEC-4471", {"aztec"}),
    ("pdf417", "PDF417-4471", "PDF417-4471", {"pdf417"}),
]

NAMES = [name for name, *_ in SYMBOLOGIES]

# Symbologies none of the available decoders can read. Rendered and inspected, never scanned.
NO_DECODER = {"msi", "plessey"}


def normalise(token):
    """Reduces a decoder's format name to a comparable token: "CODE-128" and "Code128" agree."""
    return re.sub(r"[^a-z0-9]", "", token.lower())


def rasterise(pdf_path, out_prefix, dpi=300):
    """Renders the PDF to PNG pages and returns their paths."""
    directory = os.path.dirname(pdf_path)
    prefix = os.path.basename(out_prefix)
    for stale in os.listdir(directory):
        if stale.startswith(prefix) and stale.endswith(".png"):
            os.remove(os.path.join(directory, stale))

    subprocess.run(["pdftoppm", "-r", str(dpi), "-png", pdf_path, out_prefix],
                   check=True, timeout=180)

    return sorted(
        os.path.join(directory, f)
        for f in os.listdir(directory)
        if f.startswith(prefix) and f.endswith(".png"))


def decode_zbar(image):
    """Reads every symbology zbar supports, reporting the format it recognised."""
    enable = [f"-S{s}.enable=1" for s in
              ("code128", "code39", "code93", "codabar", "i25", "ean13", "ean8", "upca", "upce", "qrcode")]

    result = subprocess.run(["zbarimg", "-q", *enable, image],
                            capture_output=True, text=True, timeout=180)

    found = []
    for line in result.stdout.splitlines():
        # Lines read "CODE-128:the value"; the value itself may contain colons.
        if ":" in line:
            symbology, _, text = line.partition(":")
            found.append((normalise(symbology), text))

    return found


def decode_dmtx(image):
    """Reads Data Matrix with libdmtx, an implementation unrelated to the encoder."""
    result = subprocess.run(["dmtxread", "-N4", "-m2000", image],
                            capture_output=True, text=True, timeout=240)

    # dmtxread reads nothing but Data Matrix, so anything it returns is one.
    return [("datamatrix", line) for line in result.stdout.splitlines() if line.strip()]


def decode_zxingcpp(image):
    """Reads the 2D symbologies zbar cannot, using the C++ implementation."""
    try:
        import zxingcpp
        from PIL import Image
    except ImportError:
        return []

    found = []
    for result in zxingcpp.read_barcodes(Image.open(image)):
        if result.text:
            found.append((normalise(str(result.format).split(".")[-1]), result.text))

    return found


def decode_all(images):
    """Returns every (format, text) pair any decoder read, plus a per-decoder tally."""
    pairs, tally = set(), {}

    for decoder, reader in (("zbar", decode_zbar),
                            ("libdmtx", decode_dmtx),
                            ("zxing-cpp", decode_zxingcpp)):
        found = []
        for image in images:
            try:
                found.extend(reader(image))
            except (subprocess.TimeoutExpired, subprocess.CalledProcessError, FileNotFoundError):
                pass

        tally[decoder] = len(found)
        pairs.update(found)

    return pairs, tally


def verify(decoded_pairs):
    """Matches each symbology on format and value. Returns (results, unread)."""
    results, unread = [], []

    for name, sent, expected, formats in SYMBOLOGIES:
        hit = any(
            fmt in formats and (expected == text or normalise(expected) in normalise(text))
            for fmt, text in decoded_pairs)

        results.append((name, sent, hit))
        if not hit:
            unread.append(name)

    return results, unread


def rasterise_with_pdfium(pdf_path, out_prefix, dpi=300):
    """Renders the PDF to PNG pages with PDFium rather than poppler.

    Two renderers rather than one because they are genuinely different implementations, and a
    document that comes out right under poppler but wrong under PDFium is a real defect that a
    single renderer would hide. PDFium is also what a great many viewers and print paths use.
    """
    import pypdfium2

    directory = os.path.dirname(pdf_path)
    prefix = os.path.basename(out_prefix)
    for stale in os.listdir(directory):
        if stale.startswith(prefix) and stale.endswith(".png"):
            os.remove(os.path.join(directory, stale))

    written = []
    document = pypdfium2.PdfDocument(pdf_path)
    try:
        for index, page in enumerate(document):
            image = page.render(scale=dpi / 72).to_pil()
            path = os.path.join(directory, f"{prefix}-{index + 1}.png")
            image.save(path)
            written.append(path)
    finally:
        document.close()

    return sorted(written)
