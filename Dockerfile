# ─── Build Stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Copy property files for Central Package Management (CPM)
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]

# Copy csproj files for all projects to cache restore layer
COPY ["src/Web.Api/Web.Api.csproj", "src/Web.Api/"]
COPY ["src/Application/Application.csproj", "src/Application/"]
COPY ["src/Domain/Domain.csproj", "src/Domain/"]
COPY ["src/Infrastructure/Infrastructure.csproj", "src/Infrastructure/"]
COPY ["src/SharedKernel/SharedKernel.csproj", "src/SharedKernel/"]

# Restore dependencies
RUN dotnet restore "src/Web.Api/Web.Api.csproj" -r linux-x64

# Copy the rest of the source code
COPY . .

# Build and publish for Linux x64 so OpenCvSharp native runtimes are included
RUN dotnet publish "src/Web.Api/Web.Api.csproj" \
  -c Release \
  -o /app/publish \
  -r linux-x64 \
  --no-restore

# ─── Runtime Stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app

# Install dependencies for Npgsql (Kerberos), Globalization, and AI Runtimes (ONNX/OpenCV)
# We use Debian (glibc) instead of Alpine (musl) for ONNX/OpenCV native compatibility
RUN apt-get update && apt-get install -y --no-install-recommends \
  libkrb5-3 \
  libicu74 \
  curl \
  libgdiplus \
  libgomp1 \
  libfontconfig1 \
  libfreetype6 \
  libjpeg-turbo8 \
  libpng16-16t64 \
  libtiff6 \
  libwebp7 \
  libx11-6 \
  libgl1 \
  libopenexr-3-1-30 \
  && rm -rf /var/lib/apt/lists/*


RUN ln -s /usr/lib/x86_64-linux-gnu/libtiff.so.6 \
  /usr/lib/x86_64-linux-gnu/libtiff.so.5

# Disable invariant globalization to use icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV LD_LIBRARY_PATH=/app/runtimes/linux-x64/native

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# .NET 8+ images use port 8080 by default
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Copy the published output from the build stage
COPY --from=build /app/publish .

# OpenCvSharp P/Invoke probes several library names
RUN ln -sf libOpenCvSharpExtern.so /app/runtimes/linux-x64/native/OpenCvSharpExtern.so

ENTRYPOINT ["dotnet", "Web.Api.dll"]
