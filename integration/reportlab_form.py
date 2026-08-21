"""Builds a form with ReportLab, a PDF writer that shares no code with LibreOffice.

Two authoring tools disagreeing is the point. Whether a tick box carries one piece of
artwork per state or a single one, whether a set of radio buttons names its states after
its values or after their positions, how a dropdown encodes its options — these are
choices each writer makes for itself, and a library that only ever sees one writer's
choices has tested its own assumptions rather than the format.
"""

from reportlab.lib.colors import black, white
from reportlab.pdfgen import canvas


def build(path, width=400, height=300):
    """Writes a form carrying a tick box, radio buttons, a dropdown and a list box."""
    sheet = canvas.Canvas(path, pagesize=(width, height))
    sheet.setFont("Helvetica", 10)

    sheet.drawString(20, height - 40, "Agree:")
    sheet.acroForm.checkbox(
        name="agree", x=80, y=height - 44, size=14,
        buttonStyle="check", borderColor=black, fillColor=white, textColor=black)

    sheet.drawString(20, height - 90, "Size:")
    for index, value in enumerate(("Small", "Large")):
        sheet.acroForm.radio(
            name="size", value=value, selected=False,
            x=80 + (index * 80), y=height - 94, size=14,
            buttonStyle="circle", borderColor=black, fillColor=white, textColor=black)

    sheet.drawString(20, height - 140, "Country:")
    sheet.acroForm.choice(
        name="country", value="Ireland", options=["Ireland", "Portugal", "Japan"],
        x=80, y=height - 148, width=140, height=22,
        borderColor=black, fillColor=white, textColor=black, forceBorder=True)

    sheet.drawString(20, height - 210, "Sizes:")
    sheet.acroForm.listbox(
        name="sizes", value="S", options=["S", "M", "L"],
        x=80, y=height - 250, width=140, height=60,
        borderColor=black, fillColor=white, textColor=black, forceBorder=True)

    sheet.save()
    return path
