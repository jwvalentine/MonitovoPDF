# Runtime image for the service. Fonts matter here: PDFsharp's cross-platform build
# loads none on its own and a slim container ships none, so text would silently fail
# to draw without them.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY MonitovoPDF.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish MonitovoPDF.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0

# DejaVu is under the Bitstream Vera licence, which permits redistribution provided the
# notice travels with the copies. The package's copyright file under /usr/share/doc
# satisfies that, so do not strip /usr/share/doc from this image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

# Ship the licence notices with the binaries. PDFsharp's MIT terms and the fonts'
# Bitstream Vera terms both require their notice to accompany the copies.
COPY LICENSE THIRD-PARTY-NOTICES.md ./

# DejaVu ships DejaVuSans.ttf and DejaVuSans-Bold.ttf, which match the face-name
# convention the font resolver expects.
ENV ASPNETCORE_URLS=http://+:8080 \
    Rendering__FontDirectory=/usr/share/fonts/truetype/dejavu \
    Rendering__DefaultFontFamily=DejaVuSans

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "MonitovoPDF.dll"]
