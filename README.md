# PDFCompressor CLI

A cross-platform (Windows, macOS & Linux) command-line PDF compression tool built with .NET 10, PDFsharp, and SixLabors.ImageSharp. It drastically reduces PDF file sizes by compressing internal streams (Flate/Deflate), re-encoding and optimizing embedded images (JPEG & raw bitmap streams), downsampling high-resolution pictures, and stripping unused metadata.

## Features

- **Cross-Platform**: Runs seamlessly on macOS, Linux, and Windows.
- **Deep PDF Optimization**: Optimizes PDF object streams with Flate best compression and cleans unused object references.
- **Embedded Image Re-compression**: Detects embedded images (JPEG, raw RGB, and Grayscale) and recompresses them using customizable quality levels.
- **Smart Downsampling**: Preserves aspect ratio while resizing oversized images via `--max-width` and `--max-height`.
- **Metadata Stripping**: Optionally removes title, author, creator, and producer metadata (`-s, --strip-metadata`) for privacy and extra size reduction.
- **Batch Processing**: Supports compressing individual PDF files as well as entire directories.
- **Global Tool Ready**: Can be installed and invoked anywhere in your terminal as `pdf-compressor`.

---

## Installation

### Option 1: Install as a Global .NET Tool

Build and install it locally:

```bash
cd PdfCompressor
dotnet pack -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg PdfCompressor
```

> **macOS Note**: If `pdf-compressor` is not recognized immediately, ensure your `PATH` includes the .NET tools folder in `~/.zshrc`:
> ```bash
> export PATH="$PATH:$HOME/.dotnet/tools"
> ```

### Option 2: Run Directly via `dotnet run`

```bash
dotnet run --project ./PdfCompressor.csproj -- "path/to/document.pdf"
```

---

## Usage

```bash
pdf-compressor <pdf-path-or-folder> [options]
```

### Options

| Option | Shorthand | Description | Default |
|---|---|---|---|
| `--output <path>` | `-o` | Target file path or directory for compressed PDF(s) | `<filename>_compressed.pdf` |
| `--quality <1-100>` | `-q` | JPEG compression quality for embedded images | `75` |
| `--max-width <pixels>` | `-w` | Maximum image width (maintains aspect ratio) | Original |
| `--max-height <pixels>` | - | Maximum image height (maintains aspect ratio) | Original |
| `--strip-metadata` | `-s` | Strip author, creator, and document metadata | `false` |
| `--help` | `-h` | Display help information | - |

---

## Examples

### 1. Default compression
```bash
pdf-compressor "document.pdf"
# Output: document_compressed.pdf
```

### 2. High-compression mode with custom output path
```bash
pdf-compressor "scan.pdf" -q 60 -o "scan_compressed.pdf"
```

### 3. Downscale oversized images and strip metadata
```bash
pdf-compressor "presentation.pdf" -w 1600 -q 70 --strip-metadata
```

### 4. Batch compress an entire directory of PDFs
```bash
pdf-compressor ./scanned_invoices/ -o ./compressed_invoices/ -q 65
```

### Benchmark Example

```
Processing: sample_document.pdf (2.09 MB)
Pages: 3 | Analyzing PDF objects...
  [img #1] 1200x900 -> 600x450 (684.53 KB -> 28.12 KB, -95.9%)
  [img #2] 1200x900 -> 600x450 (691.75 KB -> 31.73 KB, -95.4%)
  [img #3] 1200x900 -> 600x450 (755.26 KB -> 49.90 KB, -93.4%)
Successfully saved: sample_document_compressed.pdf
Size: 2.09 MB -> 115.05 KB (94.6% reduction)
Images processed: 3 found, 3 compressed
```

---

## License

MIT
