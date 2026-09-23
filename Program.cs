using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PdfCompressor;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || (args.Length == 1 && (args[0] == "-h" || args[0] == "/?")))
        {
            ShowHelp();
            return 0;
        }

        var inputPath = args[0];
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            PrintError($"Input path not found: {inputPath}");
            return 1;
        }

        var quality = ParseIntOption(args, "--quality", "-q", 75);
        var maxWidth = ParseNullableIntOption(args, "--max-width", "-w");
        var maxHeight = ParseNullableIntOption(args, "--max-height", null);
        var outputPath = ParseStringOption(args, "--output", "-o");
        var stripMetadata = args.Contains("--strip-metadata") || args.Contains("-s");

        try
        {
            if (Directory.Exists(inputPath))
            {
                return await ProcessDirectoryAsync(inputPath, outputPath, quality, maxWidth, maxHeight, stripMetadata);
            }

            return await ProcessSinglePdfAsync(inputPath, outputPath, quality, maxWidth, maxHeight, stripMetadata);
        }
        catch (Exception ex)
        {
            PrintError($"Fatal error processing PDF: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ProcessSinglePdfAsync(
        string inputPath,
        string? outputPath,
        int quality,
        int? maxWidth,
        int? maxHeight,
        bool stripMetadata)
    {
        outputPath ??= GenerateDefaultOutputPath(inputPath);

        var originalFileInfo = new FileInfo(inputPath);
        var originalSizeInBytes = originalFileInfo.Length;

        Console.WriteLine($"Processing: {Path.GetFileName(inputPath)} ({FormatBytes(originalSizeInBytes)})");

        var result = await CompressPdfAsync(inputPath, outputPath, quality, maxWidth, maxHeight, stripMetadata);

        if (!File.Exists(outputPath))
        {
            PrintError("Failed to produce output PDF file.");
            return 1;
        }

        var compressedFileInfo = new FileInfo(outputPath);
        var compressedSizeInBytes = compressedFileInfo.Length;
        var reductionPercent = (1.0 - ((double)compressedSizeInBytes / originalSizeInBytes)) * 100.0;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Successfully saved: {outputPath}");
        if (reductionPercent > 0)
        {
            Console.WriteLine($"Size: {FormatBytes(originalSizeInBytes)} -> {FormatBytes(compressedSizeInBytes)} ({reductionPercent:F1}% reduction)");
        }
        else
        {
            Console.WriteLine($"Size: {FormatBytes(originalSizeInBytes)} -> {FormatBytes(compressedSizeInBytes)} (Already optimized)");
        }
        Console.WriteLine($"Images processed: {result.TotalImages} found, {result.CompressedImages} compressed");
        Console.ResetColor();

        return 0;
    }

    private static async Task<int> ProcessDirectoryAsync(
        string inputDir,
        string? outputDir,
        int quality,
        int? maxWidth,
        int? maxHeight,
        bool stripMetadata)
    {
        var pdfFiles = Directory.GetFiles(inputDir, "*.pdf", SearchOption.TopDirectoryOnly);
        if (pdfFiles.Length == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"No PDF files found in directory: {inputDir}");
            Console.ResetColor();
            return 0;
        }

        outputDir ??= Path.Combine(inputDir, "compressed");
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        Console.WriteLine($"Found {pdfFiles.Length} PDF file(s) in {inputDir}");
        Console.WriteLine($"Saving compressed PDFs to: {outputDir}\n");

        long totalOriginalBytes = 0;
        long totalCompressedBytes = 0;
        int successfulFiles = 0;

        foreach (var file in pdfFiles)
        {
            var fileName = Path.GetFileName(file);
            var targetPath = Path.Combine(outputDir, fileName);

            var origSize = new FileInfo(file).Length;
            totalOriginalBytes += origSize;

            Console.WriteLine($"----------------------------------------");
            Console.WriteLine($"File {successfulFiles + 1}/{pdfFiles.Length}: {fileName} ({FormatBytes(origSize)})");

            try
            {
                var result = await CompressPdfAsync(file, targetPath, quality, maxWidth, maxHeight, stripMetadata);
                var compSize = new FileInfo(targetPath).Length;
                totalCompressedBytes += compSize;
                successfulFiles++;

                var redPercent = (1.0 - ((double)compSize / origSize)) * 100.0;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Saved: {targetPath} ({FormatBytes(compSize)}, {redPercent:F1}% reduction)");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                PrintError($"Failed to compress {fileName}: {ex.Message}");
            }
        }

        Console.WriteLine("========================================");
        Console.ForegroundColor = ConsoleColor.Green;
        var totalReduction = (1.0 - ((double)totalCompressedBytes / Math.Max(1, totalOriginalBytes))) * 100.0;
        Console.WriteLine($"Batch completed: {successfulFiles}/{pdfFiles.Length} files processed successfully.");
        Console.WriteLine($"Total size: {FormatBytes(totalOriginalBytes)} -> {FormatBytes(totalCompressedBytes)} ({totalReduction:F1}% overall reduction)");
        Console.ResetColor();

        return 0;
    }

    private class CompressionResult
    {
        public int TotalImages { get; set; }
        public int CompressedImages { get; set; }
    }

    private static async Task<CompressionResult> CompressPdfAsync(
        string inputPath,
        string outputPath,
        int quality,
        int? maxWidth,
        int? maxHeight,
        bool stripMetadata)
    {
        var result = new CompressionResult();

        // Ensure output directory exists
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        // Load document
        using var inputStream = File.OpenRead(inputPath);
        using var document = PdfReader.Open(inputStream, PdfDocumentOpenMode.Modify);

        Console.WriteLine($"Pages: {document.PageCount} | Analyzing PDF objects...");

        var allObjects = document.Internals.GetAllObjects();
        var processedImageDicts = new HashSet<PdfDictionary>();

        foreach (var obj in allObjects)
        {
            if (obj is PdfDictionary dict && dict.Stream != null)
            {
                var subtype = dict.Elements.GetName("/Subtype");
                if (subtype == "/Image")
                {
                    if (!processedImageDicts.Add(dict))
                    {
                        continue;
                    }

                    result.TotalImages++;
                    var compressed = await TryCompressImageAsync(dict, quality, maxWidth, maxHeight, result.TotalImages);
                    if (compressed)
                    {
                        result.CompressedImages++;
                    }
                }
            }
        }

        // Optimize document compression options
        document.Options.CompressContentStreams = true;
        document.Options.NoCompression = false;
        document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
        document.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Never;

        // Optionally strip metadata
        if (stripMetadata)
        {
            document.Info.Title = string.Empty;
            document.Info.Author = string.Empty;
            document.Info.Subject = string.Empty;
            document.Info.Keywords = string.Empty;
            document.Info.Creator = string.Empty;
            document.Info.Elements.Remove("/Producer");
        }

        // Save compressed PDF
        using var outputStream = new MemoryStream();
        document.Save(outputStream);
        await File.WriteAllBytesAsync(outputPath, outputStream.ToArray());

        return result;
    }

    private static async Task<bool> TryCompressImageAsync(
        PdfDictionary dict,
        int quality,
        int? maxWidth,
        int? maxHeight,
        int imageIndex)
    {
        try
        {
            var filter = GetFilterName(dict);
            var originalLength = dict.Stream?.Length ?? 0;
            if (originalLength == 0) return false;

            byte[]? rawImageBytes = null;
            bool isJpeg = filter == "/DCTDecode";

            if (isJpeg)
            {
                rawImageBytes = dict.Stream!.Value;
            }
            else
            {
                // Attempt to read unfiltered stream if it's a known raw or flate stream
                var unfiltered = dict.Stream!.UnfilteredValue;
                if (unfiltered != null && unfiltered.Length > 0)
                {
                    // Check magic bytes for PNG or JPEG inside stream
                    if (unfiltered.Length > 3 && unfiltered[0] == 0xFF && unfiltered[1] == 0xD8 && unfiltered[2] == 0xFF)
                    {
                        rawImageBytes = unfiltered;
                        isJpeg = true;
                    }
                    else
                    {
                        var width = dict.Elements.GetInteger("/Width");
                        var height = dict.Elements.GetInteger("/Height");
                        var colorSpace = dict.Elements.GetName("/ColorSpace");
                        var bitsPerComponent = dict.Elements.GetInteger("/BitsPerComponent");

                        if (bitsPerComponent == 8 && width > 0 && height > 0)
                        {
                            if (colorSpace == "/DeviceRGB" && unfiltered.Length >= width * height * 3)
                            {
                                return TryCompressRawRgb(dict, unfiltered, width, height, quality, maxWidth, maxHeight, originalLength, imageIndex);
                            }
                            if (colorSpace == "/DeviceGray" && unfiltered.Length >= width * height)
                            {
                                return TryCompressRawGray(dict, unfiltered, width, height, quality, maxWidth, maxHeight, originalLength, imageIndex);
                            }
                        }
                    }
                }
            }

            if (rawImageBytes == null)
            {
                return false;
            }

            using var image = Image.Load(rawImageBytes);
            var origW = image.Width;
            var origH = image.Height;

            bool resized = false;
            if (maxWidth.HasValue || maxHeight.HasValue)
            {
                var targetWidth = maxWidth ?? 0;
                var targetHeight = maxHeight ?? 0;

                if ((targetWidth > 0 && origW > targetWidth) || (targetHeight > 0 && origH > targetHeight))
                {
                    image.Mutate(ctx => ctx.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(targetWidth, targetHeight)
                    }));
                    resized = true;
                }
            }

            using var compressedStream = new MemoryStream();
            await image.SaveAsJpegAsync(compressedStream, new JpegEncoder { Quality = quality });
            var newBytes = compressedStream.ToArray();

            if (newBytes.Length < originalLength)
            {
                dict.Stream!.Value = newBytes;
                dict.Elements.SetInteger("/Length", newBytes.Length);
                dict.Elements.SetName("/Filter", "/DCTDecode");
                dict.Elements.SetName("/ColorSpace", "/DeviceRGB");
                dict.Elements.SetInteger("/BitsPerComponent", 8);
                if (resized)
                {
                    dict.Elements.SetInteger("/Width", image.Width);
                    dict.Elements.SetInteger("/Height", image.Height);
                }

                var saving = (1.0 - ((double)newBytes.Length / originalLength)) * 100.0;
                Console.WriteLine($"  [img #{imageIndex}] {origW}x{origH} -> {(resized ? $"{image.Width}x{image.Height}" : "same")} ({FormatBytes(originalLength)} -> {FormatBytes(newBytes.Length)}, -{saving:F1}%)");
                return true;
            }
        }
        catch
        {
            // If any image fails decoding/re-encoding, preserve original image intact.
        }

        return false;
    }

    private static bool TryCompressRawRgb(
        PdfDictionary dict,
        byte[] unfiltered,
        int width,
        int height,
        int quality,
        int? maxWidth,
        int? maxHeight,
        int originalLength,
        int imageIndex)
    {
        try
        {
            using var image = Image.LoadPixelData<Rgb24>(unfiltered, width, height);
            var origW = image.Width;
            var origH = image.Height;

            bool resized = false;
            if (maxWidth.HasValue || maxHeight.HasValue)
            {
                var targetWidth = maxWidth ?? 0;
                var targetHeight = maxHeight ?? 0;

                if ((targetWidth > 0 && origW > targetWidth) || (targetHeight > 0 && origH > targetHeight))
                {
                    image.Mutate(ctx => ctx.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(targetWidth, targetHeight)
                    }));
                    resized = true;
                }
            }

            using var compressedStream = new MemoryStream();
            image.SaveAsJpeg(compressedStream, new JpegEncoder { Quality = quality });
            var newBytes = compressedStream.ToArray();

            if (newBytes.Length < originalLength)
            {
                dict.Stream!.Value = newBytes;
                dict.Elements.SetInteger("/Length", newBytes.Length);
                dict.Elements.SetName("/Filter", "/DCTDecode");
                dict.Elements.SetName("/ColorSpace", "/DeviceRGB");
                dict.Elements.SetInteger("/BitsPerComponent", 8);
                dict.Elements.Remove("/DecodeParms");
                if (resized)
                {
                    dict.Elements.SetInteger("/Width", image.Width);
                    dict.Elements.SetInteger("/Height", image.Height);
                }

                var saving = (1.0 - ((double)newBytes.Length / originalLength)) * 100.0;
                Console.WriteLine($"  [raw-rgb img #{imageIndex}] {origW}x{origH} -> {(resized ? $"{image.Width}x{image.Height}" : "same")} ({FormatBytes(originalLength)} -> {FormatBytes(newBytes.Length)}, -{saving:F1}%)");
                return true;
            }
        }
        catch
        {
            // Keep original
        }
        return false;
    }

    private static bool TryCompressRawGray(
        PdfDictionary dict,
        byte[] unfiltered,
        int width,
        int height,
        int quality,
        int? maxWidth,
        int? maxHeight,
        int originalLength,
        int imageIndex)
    {
        try
        {
            using var image = Image.LoadPixelData<L8>(unfiltered, width, height);
            var origW = image.Width;
            var origH = image.Height;

            bool resized = false;
            if (maxWidth.HasValue || maxHeight.HasValue)
            {
                var targetWidth = maxWidth ?? 0;
                var targetHeight = maxHeight ?? 0;

                if ((targetWidth > 0 && origW > targetWidth) || (targetHeight > 0 && origH > targetHeight))
                {
                    image.Mutate(ctx => ctx.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(targetWidth, targetHeight)
                    }));
                    resized = true;
                }
            }

            using var compressedStream = new MemoryStream();
            image.SaveAsJpeg(compressedStream, new JpegEncoder { Quality = quality });
            var newBytes = compressedStream.ToArray();

            if (newBytes.Length < originalLength)
            {
                dict.Stream!.Value = newBytes;
                dict.Elements.SetInteger("/Length", newBytes.Length);
                dict.Elements.SetName("/Filter", "/DCTDecode");
                dict.Elements.SetName("/ColorSpace", "/DeviceGray");
                dict.Elements.SetInteger("/BitsPerComponent", 8);
                dict.Elements.Remove("/DecodeParms");
                if (resized)
                {
                    dict.Elements.SetInteger("/Width", image.Width);
                    dict.Elements.SetInteger("/Height", image.Height);
                }

                var saving = (1.0 - ((double)newBytes.Length / originalLength)) * 100.0;
                Console.WriteLine($"  [raw-gray img #{imageIndex}] {origW}x{origH} -> {(resized ? $"{image.Width}x{image.Height}" : "same")} ({FormatBytes(originalLength)} -> {FormatBytes(newBytes.Length)}, -{saving:F1}%)");
                return true;
            }
        }
        catch
        {
            // Keep original
        }
        return false;
    }

    private static string GetFilterName(PdfDictionary dict)
    {
        var item = dict.Elements["/Filter"];
        if (item is PdfName name)
        {
            return name.Value;
        }
        if (item is PdfArray array && array.Elements.Count > 0)
        {
            var first = array.Elements[0];
            if (first is PdfName arrayName)
            {
                return arrayName.Value;
            }
        }
        return string.Empty;
    }

    private static string GenerateDefaultOutputPath(string inputPath)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? string.Empty;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
        var ext = Path.GetExtension(inputPath);
        return Path.Combine(dir, $"{fileNameWithoutExt}_compressed{ext}");
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F2} MB",
            >= 1024 => $"{bytes / 1024.0:F2} KB",
            _ => $"{bytes} B"
        };
    }

    private static int ParseIntOption(string[] args, string longName, string? shortName, int defaultValue)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if ((args[i].Equals(longName, StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrEmpty(shortName) && args[i].Equals(shortName, StringComparison.OrdinalIgnoreCase))) &&
                int.TryParse(args[i + 1], out var val))
            {
                return Math.Clamp(val, 1, 100);
            }
        }
        return defaultValue;
    }

    private static int? ParseNullableIntOption(string[] args, string longName, string? shortName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if ((args[i].Equals(longName, StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrEmpty(shortName) && args[i].Equals(shortName, StringComparison.OrdinalIgnoreCase))) &&
                int.TryParse(args[i + 1], out var val) && val > 0)
            {
                return val;
            }
        }
        return null;
    }

    private static string? ParseStringOption(string[] args, string longName, string? shortName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(longName, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(shortName) && args[i].Equals(shortName, StringComparison.OrdinalIgnoreCase)))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {message}");
        Console.ResetColor();
    }

    private static void ShowHelp()
    {
        Console.WriteLine("PDF Compressor CLI (.NET 10)");
        Console.WriteLine("Usage:");
        Console.WriteLine("  pdf-compressor <pdf-path-or-folder> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <path>       Output file path or target directory");
        Console.WriteLine("  -q, --quality <1-100>     Image compression quality (default: 75)");
        Console.WriteLine("  -w, --max-width <pixels>  Maximum width for embedded images");
        Console.WriteLine("      --max-height <pixels> Maximum height for embedded images");
        Console.WriteLine("  -s, --strip-metadata      Remove author, title, and metadata to reduce size");
        Console.WriteLine("  -h, --help                Show help information");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  pdf-compressor document.pdf");
        Console.WriteLine("  pdf-compressor document.pdf -q 60 -o compressed.pdf");
        Console.WriteLine("  pdf-compressor document.pdf -q 50 -w 1600 --strip-metadata");
        Console.WriteLine("  pdf-compressor ./scanned_invoices/ -o ./compressed_invoices/ -q 70");
    }
}
