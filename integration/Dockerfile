# Builds an AcroForm PDF template with LibreOffice and exercises the running service
# against it. This is the end-to-end check that a template authored by a real tool,
# rather than one synthesised in a unit test, can actually be filled.
FROM debian:bookworm-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libreoffice-writer \
        python3 \
        python3-uno \
        poppler-utils \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# LibreOffice needs a writable home for its user profile.
ENV HOME=/tmp

WORKDIR /integration
COPY make_template.py run_tests.py ./

ENTRYPOINT ["python3", "run_tests.py"]
