# Runtime image for the HTTP host. The library itself ships as a NuGet package and needs none
# of this; the host exists for callers that want the capability over HTTP rather than in process.
#
# Fonts matter here: PDFsharp's cross-platform build loads none on its own and a slim container
# ships none, so text would silently fail to draw without them.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Restore against the project files alone, so the layer caches until dependencies change.
COPY MonitovoPDF/MonitovoPDF.csproj MonitovoPDF/
COPY MonitovoPDF.Server/MonitovoPDF.Server.csproj MonitovoPDF.Server/
RUN dotnet restore MonitovoPDF.Server/MonitovoPDF.Server.csproj

COPY MonitovoPDF/ MonitovoPDF/
COPY MonitovoPDF.Server/ MonitovoPDF.Server/
RUN dotnet publish MonitovoPDF.Server/MonitovoPDF.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0

# DejaVu is under the Bitstream Vera licence, which permits redistribution provided the
# notice travels with the copies. The package's copyright file under /usr/share/doc
# satisfies that, so do not strip /usr/share/doc from this image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

# Ship the licence notices with the binaries. PDFsharp's MIT terms, ZXing.Net's Apache-2.0
# terms and the fonts' Bitstream Vera terms all require their notice to accompany the copies.
COPY LICENSE THIRD-PARTY-NOTICES.md ./
COPY licenses/ ./licenses/

# DejaVu ships DejaVuSans.ttf and DejaVuSans-Bold.ttf, which match the face-name
# convention the font resolver expects.
ENV ASPNETCORE_URLS=http://+:8080 \
    Rendering__FontDirectory=/usr/share/fonts/truetype/dejavu \
    Rendering__DefaultFontFamily=DejaVuSans

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "MonitovoPDF.Server.dll"]
