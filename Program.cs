using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Core;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

namespace read_journal_documentanalysis
{
    public class ImageEntry
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("split")]
        public bool Split { get; set; }
    }

    class Program
    {
        // Configurable constants
        private const int BinderWidth = 0;
        private const int JpegQuality = 50;
        private const string DefaultFolder = "images";
        private const string OutputFolder = "image_out";

        static void Main(string[] args)
        {
            bool saveImages = args.Any(a => a.Equals("--save-images", StringComparison.OrdinalIgnoreCase));
            bool verbose = args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase));

            // Determine input: folder or JSON
            string inputArg = args.FirstOrDefault(a => !a.StartsWith("--")) ?? DefaultFolder;
            bool isJson = inputArg.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                              && File.Exists(inputArg);

            var entries = new List<ImageEntry>();
            string baseInputFolder;

            if (isJson)
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var json = File.ReadAllText(inputArg);
                entries = JsonSerializer.Deserialize<List<ImageEntry>>(json, opts) ?? new List<ImageEntry>();

                // Normalize relative paths
                string folder = Path.GetDirectoryName(Path.GetFullPath(inputArg))
                                ?? Directory.GetCurrentDirectory();
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (!Path.IsPathRooted(e.Path))
                        e.Path = Path.Combine(folder, e.Path);
                    entries[i] = e;
                }
                baseInputFolder = folder;
            }
            else if (Directory.Exists(inputArg))
            {
                entries = Directory
                    .EnumerateFiles(inputArg, "*.jpg", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.EnumerateFiles(inputArg, "*.jpeg", SearchOption.TopDirectoryOnly))
                    .Select(f => new ImageEntry { Path = f, Split = true })
                    .ToList();
                baseInputFolder = inputArg;
            }
            else
            {
                Console.Error.WriteLine($"Input not found: {inputArg}");
                return;
            }

            if (!entries.Any())
            {
                Console.WriteLine("No images to process; exiting.");
                return;
            }

            // Load configuration
            var cfg = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            string endpoint = cfg["DocumentAnalysisEndpoint"];
            string apiKey = cfg["DocumentAnalysisKey"];
            string modelId = cfg["DocumentAnalysisModelId"] ?? "prebuilt-read";

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine("Missing DocumentAnalysisEndpoint or DocumentAnalysisKey in appsettings.json");
                return;
            }

            // Create client
            var client = new DocumentAnalysisClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

            // Prepare output
            string outputBase = Path.Combine(baseInputFolder, OutputFolder);
            Directory.CreateDirectory(outputBase);
            string aggPath = Path.Combine(outputBase, "aggregator.txt");
            using var aggWriter = new StreamWriter(aggPath, append: false) { AutoFlush = true };

            double totalWordConf = 0;
            int totalWordCount = 0;
            var overallSw = Stopwatch.StartNew();

            // Process each image/entry
            foreach (var entry in entries)
            {
                ProcessImageFile(
                    entry.Path,
                    entry.Split,
                    client,
                    modelId,
                    saveImages,
                    verbose,
                    outputBase,
                    ref totalWordConf,
                    ref totalWordCount,
                    aggWriter);
            }

            overallSw.Stop();
            Console.WriteLine($"\nProcessed {totalWordCount} words total.");
            Console.WriteLine($"Overall average word confidence: {(totalWordCount > 0
                ? totalWordConf / totalWordCount
                : 0):P2}");
            Console.WriteLine($"Total time: {overallSw.Elapsed:c}");
        }

        static void ProcessImageFile(
            string imagePath,
            bool split,
            DocumentAnalysisClient client,
            string modelId,
            bool saveImages,
            bool verbose,
            string outputBase,
            ref double totalWordConf,
            ref int totalWordCount,
            StreamWriter aggWriter)
        {
            using var full = LoadAndOrientImage(imagePath);
            string baseName = Path.GetFileNameWithoutExtension(imagePath);

            var pages = new List<(SKBitmap bmp, string suffix)>();
            if (split)
            {
                int W = full.Width, H = full.Height;
                int half = (W - BinderWidth) / 2;
                pages.Add((Subset(full, 0, 0, half, H), "_L"));
                pages.Add((Subset(full, half + BinderWidth, 0, W, H), "_R"));
            }
            else
            {
                pages.Add((full, ""));
            }

            foreach (var (bmp, suffix) in pages)
            {
                ProcessPage(
                    bmp,
                    client,
                    modelId,
                    saveImages,
                    verbose,
                    outputBase,
                    baseName + suffix,
                    ref totalWordConf,
                    ref totalWordCount,
                    aggWriter);

                // Dispose temporary bitmaps created for split pages to
                // release native memory. The original full bitmap is
                // disposed by the surrounding using statement.
                if (!ReferenceEquals(bmp, full))
                    bmp.Dispose();
            }
        }

        static void ProcessPage(
            SKBitmap bmp,
            DocumentAnalysisClient client,
            string modelId,
            bool saveImages,
            bool verbose,
            string outputBase,
            string pageName,
            ref double totalWordConf,
            ref int totalWordCount,
            StreamWriter aggWriter)
        {
            // Set up logging → console + per-page + aggregator
            var origOut = Console.Out;
            var origErr = Console.Error;
            string logPath = Path.Combine(outputBase, pageName + ".out");
            using var logWriter = new StreamWriter(logPath, append: false) { AutoFlush = true };
            var consolePlusAgg = new TeeTextWriter(origOut, aggWriter);
            var allOut = new TeeTextWriter(consolePlusAgg, logWriter);
            Console.SetOut(allOut);
            var errorPlusAgg = new TeeTextWriter(origErr, aggWriter);
            var allErr = new TeeTextWriter(errorPlusAgg, logWriter);
            Console.SetError(allErr);

            var sw = Stopwatch.StartNew();
            Console.WriteLine($"---------- {pageName} ----------");

            try
            {
                // Encode page to JPEG in memory
                using var imgData = SKImage.FromBitmap(bmp)
                                          .Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
                using var ms = new MemoryStream(imgData.ToArray());

                if (verbose)
                    Console.WriteLine("[INFO] Calling Document Intelligence Analyze...");
                var operation = client.AnalyzeDocument(WaitUntil.Completed, modelId, ms);
                var result = operation.Value;
                if (verbose)
                    Console.WriteLine("[INFO] Analysis complete.");


                // Words
                var words = result.Pages.SelectMany(p => p.Words).ToList();
                if (words.Any())
                {
                    // Lines
                    Console.WriteLine("Recognized Text:");
                    foreach (var page in result.Pages)
                        foreach (var line in page.Lines)
                            Console.WriteLine($"  {line.Content}");
                            
                    if (verbose)
                        Console.WriteLine("\nWord confidences:");
                    double pageSum = 0;
                    foreach (var w in words)
                    {
                        if (verbose)
                            Console.WriteLine($"  {w.Content} (Conf:{w.Confidence:P2})");
                        pageSum += w.Confidence;
                    }
                    double pageAvg = pageSum / words.Count;
                    Console.WriteLine($"\nAverage word confidence for {pageName}: {pageAvg:P2}");

                    totalWordConf += pageSum;
                    totalWordCount += words.Count;
                }
                else
                {
                    Console.WriteLine("No words detected.");
                }

                // Annotate bounding boxes if requested
                if (saveImages && verbose)
                {
                    string outJpg = Path.Combine(outputBase, pageName + "_words.jpg");
                    AnnotateAndSaveWords(bmp, words, outJpg, verbose);
                }
                // else if (saveImages)
                // {
                //     Console.WriteLine("Skipping image annotation (requires --verbose).");
                // }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {pageName}: {ex.Message}");
            }
            finally
            {
                sw.Stop();
                Console.WriteLine($"Done in {sw.Elapsed:c}\n");
                Console.SetOut(origOut);
                Console.SetError(origErr);
            }
        }

        static SKBitmap LoadAndOrientImage(string path)
        {
            using var codec = SKCodec.Create(path)
                ?? throw new InvalidOperationException($"Cannot open {path}");
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height);
            var raw = new SKBitmap(info);
            codec.GetPixels(info, raw.GetPixels());
            var oriented = raw.ApplyExifOrientation(codec.EncodedOrigin);
            if (!ReferenceEquals(oriented, raw))
                raw.Dispose();
            return oriented;
        }

        static SKBitmap Subset(SKBitmap src, int x1, int y1, int x2, int y2)
        {
            var dst = new SKBitmap(x2 - x1, y2 - y1);
            src.ExtractSubset(dst, new SKRectI(x1, y1, x2, y2));
            return dst;
        }

        static void AnnotateAndSaveWords(SKBitmap src, IReadOnlyList<DocumentWord> words, string outputPath, bool verbose)
        {
            using var bmp = new SKBitmap(src.Info);
            using var canvas = new SKCanvas(bmp);
            canvas.DrawBitmap(src, 0, 0);
            using var paint = new SKPaint
            {
                Color = SKColors.Cyan,
                StrokeWidth = 2,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };

            if (verbose)
            {
                foreach (var w in words)
                {
                    var poly = w.BoundingPolygon
                                .Select(p => new SKPoint(p.X, p.Y))
                                .ToArray();
                    DrawPolygon(canvas, poly, paint);
                }

            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var outStream = new SKFileWStream(outputPath);
            bmp.Encode(outStream, SKEncodedImageFormat.Jpeg, JpegQuality);
            if (verbose)
                Console.WriteLine($"Saved {Path.GetFileName(outputPath)}");
        }

        static void DrawPolygon(SKCanvas canvas, SKPoint[] pts, SKPaint paint)
        {
            using var path = new SKPath();
            path.MoveTo(pts[0]);
            foreach (var pt in pts.Skip(1))
                path.LineTo(pt);
            path.Close();
            canvas.DrawPath(path, paint);
        }
    }

    public static class SKBitmapExtensions
    {
        public static SKBitmap ApplyExifOrientation(this SKBitmap src, SKEncodedOrigin origin)
        {
            if (origin == SKEncodedOrigin.TopLeft) return src;
            int w = src.Width, h = src.Height;
            bool swap = origin == SKEncodedOrigin.RightTop
                     || origin == SKEncodedOrigin.LeftBottom
                     || origin == SKEncodedOrigin.RightBottom
                     || origin == SKEncodedOrigin.LeftTop;
            var dst = new SKBitmap(swap ? h : w, swap ? w : h);
            using var canvas = new SKCanvas(dst);
            switch (origin)
            {
                case SKEncodedOrigin.BottomRight:
                    canvas.Translate(w, h); canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.RightTop:
                    canvas.Translate(h, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.LeftBottom:
                    canvas.Translate(0, w); canvas.RotateDegrees(270); break;
                case SKEncodedOrigin.TopRight:
                    canvas.Scale(-1, 1); canvas.Translate(-w, 0); break;
                case SKEncodedOrigin.BottomLeft:
                    canvas.Scale(1, -1); canvas.Translate(0, -h); break;
                case SKEncodedOrigin.LeftTop:
                    canvas.Scale(-1, 1); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom:
                    canvas.Scale(1, -1); canvas.RotateDegrees(90); break;
            }
            canvas.DrawBitmap(src, 0, 0);
            return dst;
        }
    }

    public class TeeTextWriter : TextWriter
    {
        private readonly TextWriter _a, _b;
        public TeeTextWriter(TextWriter a, TextWriter b) { _a = a; _b = b; }
        public override Encoding Encoding => _a.Encoding;
        public override void Write(char c) { _a.Write(c); _b.Write(c); }
        public override void Write(string s) { _a.Write(s); _b.Write(s); }
        public override void WriteLine(string s) { _a.WriteLine(s); _b.WriteLine(s); }
        public override void Flush() { _a.Flush(); _b.Flush(); }
    }
}
