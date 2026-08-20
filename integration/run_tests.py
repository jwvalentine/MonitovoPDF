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

    Using an independent extractor rather than grepping the content stream checks the
    thing that actually matters: that a different PDF consumer can read what was drawn.
    The bytes are not searchable directly, because an embedded font subset stores text as
    glyph indices rather than as characters.
    """
    result = subprocess.run(
        ["pdftotext", "-layout", path, "-"],
        capture_output=True, text=True, timeout=60)

    if result.returncode != 0:
        return ""

    return result.stdout


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
