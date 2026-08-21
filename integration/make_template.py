"""Builds a label template PDF carrying named AcroForm fields, using LibreOffice.

The point of doing this through LibreOffice rather than writing the PDF directly is that
it produces a template the way a real template author would: form controls placed on a
page, exported with "create PDF form" turned on. The field names given here are the keys
the service expects in a render request.
"""

import os
import subprocess
import sys
import time

import uno
from com.sun.star.awt import Point, Size
from com.sun.star.beans import PropertyValue
from com.sun.star.connection import NoConnectException
from com.sun.star.text.HoriOrientation import NONE as HORI_NONE
from com.sun.star.text.RelOrientation import PAGE_FRAME
from com.sun.star.text.TextContentAnchorType import AT_PAGE
from com.sun.star.text.VertOrientation import NONE as VERT_NONE

# LibreOffice works in hundredths of a millimetre.
MM = 100

PAGE_WIDTH_MM = 100
PAGE_HEIGHT_MM = 60

# name, x, y, width, height — all in millimetres.
FIELDS = [
    ("part_number", 5, 5, 90, 10),
    ("description", 5, 18, 90, 8),
    ("barcode", 5, 30, 50, 24),
]

SOCKET = "socket,host=127.0.0.1,port=2002;urp;StarOffice.ComponentContext"


def start_libreoffice():
    """Starts a headless LibreOffice listening on a UNO socket and returns the process."""
    return subprocess.Popen([
        "soffice",
        "--headless",
        "--norestore",
        "--nologo",
        "--nodefault",
        f"--accept={SOCKET.rsplit(';', 1)[0]};",
    ])


def connect(timeout_seconds=90):
    """Waits for the UNO socket to accept a connection and returns the remote context."""
    local_context = uno.getComponentContext()
    resolver = local_context.ServiceManager.createInstanceWithContext(
        "com.sun.star.bridge.UnoUrlResolver", local_context)

    deadline = time.monotonic() + timeout_seconds
    while True:
        try:
            return resolver.resolve(f"uno:{SOCKET}")
        except NoConnectException:
            if time.monotonic() > deadline:
                raise
            time.sleep(0.5)


def build_document(desktop):
    """Creates a Writer document sized like a label, with one form control per field."""
    document = desktop.loadComponentFromURL(
        "private:factory/swriter", "_blank", 0, ())

    page_style = document.StyleFamilies.getByName("PageStyles").getByName("Standard")
    page_style.Width = PAGE_WIDTH_MM * MM
    page_style.Height = PAGE_HEIGHT_MM * MM
    page_style.TopMargin = 0
    page_style.BottomMargin = 0
    page_style.LeftMargin = 0
    page_style.RightMargin = 0

    draw_page = document.DrawPage

    for name, x, y, width, height in FIELDS:
        control = document.createInstance("com.sun.star.form.component.TextField")
        control.Name = name

        shape = document.createInstance("com.sun.star.drawing.ControlShape")
        shape.setSize(Size(width * MM, height * MM))
        shape.setControl(control)

        draw_page.add(shape)

        # Anchoring alone is not enough. Writer keeps its own orientation for a shape and
        # will override an absolute position unless the orientation is explicitly detached
        # from the text flow and made relative to the page.
        shape.AnchorType = AT_PAGE
        shape.HoriOrient = HORI_NONE
        shape.VertOrient = VERT_NONE
        shape.HoriOrientRelation = PAGE_FRAME
        shape.VertOrientRelation = PAGE_FRAME
        shape.setPosition(Point(x * MM, y * MM))

        if shape.AnchorType != AT_PAGE:
            raise RuntimeError(f"Control '{name}' would not anchor to the page.")

    return document


def build_form_controls(desktop):
    """Creates a document carrying a tick box, a dropdown and a pair of radio buttons.

    These are what a business form is mostly made of, as opposed to the text fields a label
    uses, and a real authoring tool is the only way to find out what it actually emits for
    them — in particular whether it writes appearance streams for each state, which is what
    the library paints when it fills one.
    """
    document = desktop.loadComponentFromURL(
        "private:factory/swriter", "_blank", 0, ())

    draw_page = document.DrawPage

    def place(control, name, x, y, width, height):
        control.Name = name

        shape = document.createInstance("com.sun.star.drawing.ControlShape")
        shape.setSize(Size(width * MM, height * MM))
        shape.setControl(control)
        draw_page.add(shape)

        shape.AnchorType = AT_PAGE
        shape.HoriOrient = HORI_NONE
        shape.VertOrient = VERT_NONE
        shape.HoriOrientRelation = PAGE_FRAME
        shape.VertOrientRelation = PAGE_FRAME
        shape.setPosition(Point(x * MM, y * MM))

        if shape.AnchorType != AT_PAGE:
            raise RuntimeError(f"Control '{name}' would not anchor to the page.")

    box = document.createInstance("com.sun.star.form.component.CheckBox")
    place(box, "agree", 20, 20, 6, 6)

    combo = document.createInstance("com.sun.star.form.component.ListBox")
    combo.StringItemList = ("Ireland", "Portugal", "Japan")
    combo.Dropdown = True
    place(combo, "country", 20, 40, 60, 8)

    return document


def form_controls(output_path):
    """Builds the form-controls template and writes it to disk."""
    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    process = start_libreoffice()
    try:
        context = connect()
        desktop = context.ServiceManager.createInstanceWithContext(
            "com.sun.star.frame.Desktop", context)

        document = build_form_controls(desktop)
        try:
            export_pdf(document, output_path)
        finally:
            document.close(False)
    finally:
        process.terminate()
        process.wait(timeout=30)

    return output_path


def export_pdf(document, path):
    """Exports to PDF with form export enabled, so the controls become AcroForm fields."""
    filter_data = (
        PropertyValue("ExportFormFields", 0, True, 0),
        PropertyValue("FormsType", 0, 1, 0),
    )

    arguments = (
        PropertyValue("FilterName", 0, "writer_pdf_Export", 0),
        PropertyValue(
            "FilterData", 0,
            uno.Any("[]com.sun.star.beans.PropertyValue", filter_data), 0),
    )

    document.storeToURL(uno.systemPathToFileUrl(path), arguments)


def build_barcode_sheet(desktop, names):
    """Creates an A4 template with one generously sized form field per symbology name."""
    document = desktop.loadComponentFromURL("private:factory/swriter", "_blank", 0, ())

    page_style = document.StyleFamilies.getByName("PageStyles").getByName("Standard")
    page_style.Width = 210 * MM
    page_style.Height = 297 * MM
    for margin in ("TopMargin", "BottomMargin", "LeftMargin", "RightMargin"):
        setattr(page_style, margin, 0)

    draw_page = document.DrawPage

    columns = 3
    cell_width, cell_height = 66, 56
    field_width, field_height = 58, 26

    for index, name in enumerate(names):
        column, row = index % columns, index // columns
        x = 6 + (column * cell_width)
        y = 10 + (row * cell_height)

        control = document.createInstance("com.sun.star.form.component.TextField")
        control.Name = name

        shape = document.createInstance("com.sun.star.drawing.ControlShape")
        shape.setSize(Size(field_width * MM, field_height * MM))
        shape.setControl(control)
        draw_page.add(shape)

        shape.AnchorType = AT_PAGE
        shape.HoriOrient = HORI_NONE
        shape.VertOrient = VERT_NONE
        shape.HoriOrientRelation = PAGE_FRAME
        shape.VertOrientRelation = PAGE_FRAME
        shape.setPosition(Point(x * MM, y * MM))

        if shape.AnchorType != AT_PAGE:
            raise RuntimeError(f"Control '{name}' would not anchor to the page.")

    return document


def barcode_sheet(output_path, names):
    """Builds the all-symbologies template at the given path."""
    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    office = start_libreoffice()
    try:
        context = connect()
        desktop = context.ServiceManager.createInstanceWithContext(
            "com.sun.star.frame.Desktop", context)

        document = build_barcode_sheet(desktop, names)
        try:
            export_pdf(document, output_path)
        finally:
            document.close(False)

        print(f"Wrote {output_path} ({os.path.getsize(output_path)} bytes) "
              f"with {len(names)} barcode field(s)")
    finally:
        office.terminate()
        office.wait(timeout=30)


def main_with_output(output_path):
    """Builds the template at the given path, starting and stopping LibreOffice around it."""
    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    office = start_libreoffice()
    try:
        context = connect()
        desktop = context.ServiceManager.createInstanceWithContext(
            "com.sun.star.frame.Desktop", context)

        document = build_document(desktop)
        try:
            export_pdf(document, output_path)
        finally:
            document.close(False)

        print(f"Wrote {output_path} ({os.path.getsize(output_path)} bytes) "
              f"with fields: {', '.join(name for name, *_ in FIELDS)}")
    finally:
        office.terminate()
        office.wait(timeout=30)


if __name__ == "__main__":
    main_with_output(sys.argv[1] if len(sys.argv) > 1 else "/out/template.pdf")
