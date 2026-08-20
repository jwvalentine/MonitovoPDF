"""End-to-end check against a template LibreOffice actually produced.

The .NET test suite renders templates it synthesises itself, which proves the renderer
but not that a template from a real authoring tool can be filled. This closes that gap:
LibreOffice builds a PDF form, the running service fills it, and the result is inspected.

Exits non-zero if any check fails, so a container runner reports the failure.
"""

import base64
import json
import os
import struct
import subprocess
import sys
import time
import urllib.error
import urllib.request
import zlib

import barcodes
import make_template

BASE_URL = os.environ.get("MONITOVO_URL", "http://app:8080")
OUTPUT_DIR = os.environ.get("OUTPUT_DIR", "/out")

PART_NUMBER = "WIDGET-4471"
DESCRIPTION = "Stainless bracket, 40mm, pack of 10"

failures = []


def check(description, condition, detail=""):
    """Records a check, printing its result. Keeps going so one run reports everything."""
    if condition:
        print(f"  PASS  {description}")
    else:
        print(f"  FAIL  {description}{' — ' + detail if detail else ''}")
        failures.append(description)


def post(path, payload):
    """POSTs JSON and returns (status, body bytes, content type)."""
    request = urllib.request.Request(
        f"{BASE_URL}{path}",
        data=json.dumps(payload).encode(),
        headers={"Content-Type": "application/json"},
        method="POST")

    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return response.status, response.read(), response.headers.get("Content-Type", "")
    except urllib.error.HTTPError as error:
        return error.code, error.read(), error.headers.get("Content-Type", "")


def wait_for_service(timeout_seconds=120):
    """Blocks until the service answers its health endpoint."""
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        try:
            with urllib.request.urlopen(f"{BASE_URL}/health", timeout=5) as response:
                if response.status == 200:
                    return True
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            pass
        time.sleep(1)

    return False


def striped_png(width=240, height=100):
    """Builds a barcode-like PNG without needing an imaging library.

    A recognisable image makes the rendered label worth looking at, and proves the image
    path handles something with real dimensions rather than a single pixel.
    """
    bars = [3, 1, 2, 1, 1, 3, 1, 2, 2, 1, 3, 1, 1, 2, 1, 3, 2, 1, 1, 2]

    pattern = bytearray()
    ink = 0
    for run in bars:
        pattern.extend([0 if ink else 255] * (run * 3))
        ink ^= 1
    pattern.extend([255] * max(0, width - len(pattern)))
    row = bytes(pattern[:width])

    raw = b"".join(b"\x00" + row for _ in range(height))

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    header = struct.pack(">IIBBBBB", width, height, 8, 0, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(raw))
            + chunk(b"IEND", b""))


def extract_text(path):
    """Extracts the visible text of a PDF using poppler.

    Using an independent extractor rather than grepping the content stream checks the thing
    that actually matters: that a different PDF consumer can read what was drawn. The raw
    bytes are not searchable because the content stream is compressed — the text itself is
    written literally, so decompressing would also work, but a real extractor is the better
    check.
    """
    result = subprocess.run(
        ["pdftotext", "-layout", path, "-"],
        capture_output=True, text=True, timeout=60)

    if result.returncode != 0:
        return ""

    return result.stdout


def run_barcode_checks():
    """Renders every supported symbology onto one sheet and reads them back."""
    print("\nBuilding an all-symbologies template with LibreOffice")
    sheet_path = os.path.join(OUTPUT_DIR, "barcode-template.pdf")
    make_template.barcode_sheet(sheet_path, barcodes.NAMES)

    sheet_bytes = open(sheet_path, "rb").read()
    missing = [n for n in barcodes.NAMES if f"/T({n})".encode() not in sheet_bytes]
    check("the sheet template defines a field per symbology", not missing, f"missing: {missing}")

    print(f"Rendering all {len(barcodes.NAMES)} symbologies in one request")
    status, body, content_type = post("/v1/labels", {
        "template": base64.b64encode(sheet_bytes).decode(),
        "barcodes": {name: {"type": name, "value": sent} for name, sent, *_ in barcodes.SYMBOLOGIES},
    })

    check("the barcode sheet renders", status == 200, f"status {status}: {body[:300]!r}")
    check("the sheet response is a PDF", content_type.startswith("application/pdf"), content_type)

    if status != 200:
        return

    sheet_out = os.path.join(OUTPUT_DIR, "barcodes.pdf")
    with open(sheet_out, "wb") as handle:
        handle.write(body)
    print(f"        wrote {sheet_out} ({len(body)} bytes)")

    check("the barcode sheet is flat", b"/Widget" not in body)
    check("barcodes are vectors, not images", b"/XObject" not in body,
          "an XObject means something was rasterised")

    # Every pass must stand on its own. Two renderers, because a symbol that decodes under one
    # and not the other is a real defect a single renderer hides. Two resolutions, because 203 dpi
    # is what a great many thermal label printers rasterise at, and narrow modules fail there long
    # before they fail at 300 — a barcode that only passes at high resolution is not one to trust.
    expected_to_decode = [name for name in barcodes.NAMES if name not in barcodes.NO_DECODER]
    read_by = {name: [] for name, *_ in barcodes.SYMBOLOGIES}

    for label, rasterise, dpi in (("poppler", barcodes.rasterise, 300),
                                  ("pdfium", barcodes.rasterise_with_pdfium, 300),
                                  ("pdfium", barcodes.rasterise_with_pdfium, 203)):
        pass_name = f"{label} at {dpi} dpi"
        prefix = os.path.join(OUTPUT_DIR, f"sheet-{label}-{dpi}")

        print(f"\nDecoding the rendered sheet, rasterised by {pass_name}")
        images = rasterise(sheet_out, prefix, dpi)
        decoded, tally = barcodes.decode_all(images)
        print(f"        {len(images)} page(s); decoders read "
              + ", ".join(f"{k}={v}" for k, v in tally.items()))

        results, unread = barcodes.verify(decoded)
        for name, _, hit in results:
            if hit:
                read_by[name].append(pass_name)

        failed = [name for name in expected_to_decode if name in unread]
        check(f"every decodable symbology scans when rasterised by {pass_name}",
              not failed, f"did not decode: {failed}")

    print("\nWhich passes read each symbology")
    for name, sent, *_ in barcodes.SYMBOLOGIES:
        where = read_by[name]
        if name in barcodes.NO_DECODER:
            print(f"  ----  {name} rendered; no decoder here can read it")
        elif where:
            print(f"  PASS  {name} decoded back to \"{sent}\"  [{', '.join(where)}]")
        else:
            print(f"  FAIL  {name} was not read by any pass")

    # Guard the other direction: if a decoder for these ever appears, tighten the exclusion
    # rather than leaving them silently untested.
    surprises = [name for name in barcodes.NO_DECODER if read_by[name]]
    check("the undecodable list is still accurate", not surprises,
          f"now decodable, remove from NO_DECODER: {surprises}")


def run_image_slot_checks():
    """Fills a template whose placeholder is an image rather than a form field.

    The barcode replaces the placeholder as vector graphics and inherits its geometry, so the
    thing worth proving is that it still scans after a real renderer has drawn it — the same
    bar for a barcode addressed by position as for one addressed by field name.
    """
    print("\nReplacing an image placeholder addressed by position")

    template = barcodes.slot_template()
    value = "SLOT-4471"

    status, body, content_type = post("/v1/labels", {
        "template": base64.b64encode(template).decode(),
        "barcodesAt": [{"page": 1, "index": 1, "type": "code128", "value": value}],
    })

    check("a placeholder template renders", status == 200, f"status {status}: {body[:300]!r}")
    check("the response is a PDF", content_type.startswith("application/pdf"), content_type)

    if status != 200:
        return

    out = os.path.join(OUTPUT_DIR, "slots.pdf")
    with open(out, "wb") as handle:
        handle.write(body)
    print(f"        wrote {out} ({len(body)} bytes)")

    check("the barcode replaced the placeholder as vectors, not a picture",
          b"/Subtype /Form" in body or b"/Subtype/Form" in body,
          "no form XObject in the output")

    for label, rasterise, dpi in (("poppler", barcodes.rasterise, 300),
                                  ("pdfium", barcodes.rasterise_with_pdfium, 300),
                                  ("pdfium", barcodes.rasterise_with_pdfium, 203)):
        images = rasterise(out, os.path.join(OUTPUT_DIR, f"slot-{label}-{dpi}"), dpi)
        decoded, _ = barcodes.decode_all(images)

        found = any(fmt == "code128" and text == value for fmt, text in decoded)
        check(f"the replaced placeholder scans when rasterised by {label} at {dpi} dpi",
              found, f"decoders read: {sorted(text for _, text in decoded)}")


def decodes_everywhere(pdf_path, prefix, symbology, value):
    """Checks a barcode reads back under both renderers, at both resolutions.

    A barcode carrying its value as readable text has shorter bars than one that does not,
    because the text is drawn inside the space the barcode was given. Shorter bars are what
    a scanner has less of to work with, so a caption is only worth having if the symbol
    still scans with one — which is the thing to measure rather than assume.
    """
    for label, rasterise, dpi in (("poppler", barcodes.rasterise, 300),
                                  ("pdfium", barcodes.rasterise_with_pdfium, 300),
                                  ("pdfium", barcodes.rasterise_with_pdfium, 203)):
        images = rasterise(pdf_path, os.path.join(OUTPUT_DIR, f"{prefix}-{label}-{dpi}"), dpi)
        decoded, _ = barcodes.decode_all(images)

        found = any(fmt == symbology and text == value for fmt, text in decoded)
        check(f"{prefix} still scans when rasterised by {label} at {dpi} dpi",
              found, f"decoders read: {sorted(text for _, text in decoded)}")


def run_barcode_caption_checks(template_b64):
    """Prints a barcode's value as readable text beneath it, and reads both back.

    This is the fallback somebody uses when a scanner is not to hand or the symbol has been
    scuffed: they read the number off the label and key it in. So there are two things to
    prove, and they pull against each other — the number has to be readable as text, and the
    bars have to still scan having given up the height the text took.
    """
    value = "47028538"

    print("\nPrinting a barcode's value as readable text, in a form field")
    status, body, _ = post("/v1/labels", {
        "template": template_b64,
        "barcodes": {"barcode": {"type": "code128", "value": value, "showValue": True}},
    })

    check("a captioned barcode renders into a field", status == 200, f"status {status}: {body[:300]!r}")

    if status == 200:
        path = os.path.join(OUTPUT_DIR, "caption-field.pdf")
        with open(path, "wb") as handle:
            handle.write(body)
        print(f"        wrote {path} ({len(body)} bytes)")

        # An independent extractor reading it is what proves the value is real text in an
        # embedded font, rather than something only this library knows how to interpret.
        check("the value is readable as text beneath the bars", value in extract_text(path),
              f"pdftotext saw: {extract_text(path).strip()[:200]!r}")
        decodes_everywhere(path, "caption-field", "code128", value)

    print("\nThe same barcode without a caption, as a control")
    status, body, _ = post("/v1/labels", {
        "template": template_b64,
        "barcodes": {"barcode": {"type": "code128", "value": value}},
    })

    if status == 200:
        path = os.path.join(OUTPUT_DIR, "caption-none.pdf")
        with open(path, "wb") as handle:
            handle.write(body)

        # Without this the readable-text check above would pass on a label that merely happened
        # to carry the number somewhere else.
        check("an uncaptioned barcode carries no such text", value not in extract_text(path),
              f"pdftotext saw: {extract_text(path).strip()[:200]!r}")

    # A placeholder addressed by position, and then the same one stood on its end. Rotation is
    # where a caption is easiest to get wrong: the bars inherit the placeholder's transform
    # whatever it is, so text that does not inherit the same one ends up lying on its side.
    for name, transform in (("upright", "170 0 0 60 15 90"), ("turned", "0 170 -60 0 180 15")):
        print(f"\nPrinting a barcode's value on the {name} image placeholder")

        status, body, _ = post("/v1/labels", {
            "template": base64.b64encode(barcodes.slot_template(transform=transform)).decode(),
            "barcodesAt": [{"page": 1, "index": 1, "type": "code128",
                            "value": value, "showValue": True}],
        })

        check(f"the {name} captioned placeholder renders", status == 200,
              f"status {status}: {body[:300]!r}")

        if status != 200:
            continue

        path = os.path.join(OUTPUT_DIR, f"caption-{name}.pdf")
        with open(path, "wb") as handle:
            handle.write(body)
        print(f"        wrote {path} ({len(body)} bytes)")

        check(f"the value on the {name} placeholder is readable as text",
              value in extract_text(path),
              f"pdftotext saw: {extract_text(path).strip()[:200]!r}")

        decodes_everywhere(path, f"caption-{name}", "code128", value)


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    template_path = os.path.join(OUTPUT_DIR, "template.pdf")

    print("Building a template with LibreOffice")
    make_template.main_with_output(template_path)

    template_bytes = open(template_path, "rb").read()
    check("LibreOffice produced an AcroForm",
          b"AcroForm" in template_bytes,
          "no /AcroForm in the exported template")
    for name, *_ in make_template.FIELDS:
        check(f"template defines the field '{name}'",
              f"/T({name})".encode() in template_bytes)

    print(f"\nWaiting for the service at {BASE_URL}")
    if not wait_for_service():
        print("  FAIL  the service never became healthy")
        return 1
    print("  PASS  the service is healthy")

    template_b64 = base64.b64encode(template_bytes).decode()
    barcode_b64 = base64.b64encode(striped_png()).decode()

    print("\nRendering a label from the LibreOffice template")
    status, body, content_type = post("/v1/labels", {
        "template": template_b64,
        "fields": {"part_number": PART_NUMBER, "description": DESCRIPTION},
        "images": {"barcode": barcode_b64},
    })

    check("the render succeeds", status == 200, f"status {status}: {body[:300]!r}")
    check("the response is a PDF", content_type.startswith("application/pdf"), content_type)

    if status == 200:
        label_path = os.path.join(OUTPUT_DIR, "label.pdf")
        with open(label_path, "wb") as handle:
            handle.write(body)
        print(f"        wrote {label_path} ({len(body)} bytes)")

        check("the output is a PDF document", body.startswith(b"%PDF"))
        check("the output carries no interactive widgets",
              b"/Widget" not in body,
              "form fields survived into the output, which would risk a blank print")
        check("the template did carry widgets to begin with", b"/Widget" in template_bytes)
        check("the output embeds a font", b"FontFile" in body,
              "no embedded font, so text was probably not drawn")
        check("the output embeds an image", b"/XObject" in body)
        check("the output is larger than the template", len(body) > len(template_bytes))

        label_text = extract_text(label_path)
        check("the part number is readable in the finished label",
              PART_NUMBER in label_text,
              f"pdftotext saw: {label_text.strip()[:200]!r}")
        check("the description is readable in the finished label",
              DESCRIPTION in label_text,
              f"pdftotext saw: {label_text.strip()[:200]!r}")
        check("the blank template carried no such text",
              PART_NUMBER not in extract_text(template_path))

    print("\nChecking that bad input is refused")
    status, body, _ = post("/v1/labels", {
        "template": template_b64,
        "fields": {"no_such_field": "value"},
    })
    check("an unknown field name is rejected", status == 400, f"status {status}")
    check("the error names the offending field", b"no_such_field" in body, body[:200].decode(errors="replace"))

    status, body, _ = post("/v1/labels", {
        "template": base64.b64encode(b"this is not a PDF").decode(),
        "fields": {"part_number": PART_NUMBER},
    })
    check("a template that is not a PDF is rejected", status == 400, f"status {status}")

    status, body, _ = post("/v1/labels", {
        "template": template_b64,
        "fields": {"part_number": PART_NUMBER},
        "images": {"part_number": barcode_b64},
    })
    check("a field given both text and an image is rejected", status == 400, f"status {status}")

    run_barcode_checks()
    run_image_slot_checks()
    run_barcode_caption_checks(template_b64)

    print()
    if failures:
        print(f"{len(failures)} check(s) failed:")
        for failure in failures:
            print(f"  - {failure}")
        return 1

    print("All checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
