# Builds AcroForm PDF templates with LibreOffice and exercises the running service against
# them. This is the end-to-end check that a template authored by a real tool, rather than one
# synthesised in a unit test, can be filled — and that every barcode the service draws can be
# read back by decoders that are not the encoder.
FROM debian:bookworm-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libreoffice-writer \
        python3 \
        python3-uno \
        python3-pip \
        poppler-utils \
        zbar-tools \
        dmtx-utils \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# zxing-cpp reads the 2D symbologies zbar cannot. pypdfium2 is PDFium, a different renderer from
# poppler: a document that rasterises correctly under one and not the other is a real defect, and
# PDFium is what many print paths use.
RUN pip3 install --quiet --break-system-packages --no-cache-dir zxing-cpp pillow pypdfium2 reportlab

# LibreOffice needs a writable home for its user profile.
ENV HOME=/tmp

WORKDIR /integration
COPY make_template.py barcodes.py reportlab_form.py run_tests.py ./

ENTRYPOINT ["python3", "run_tests.py"]
