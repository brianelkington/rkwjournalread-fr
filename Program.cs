using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using System.Text;
// using System.Text.Json;
// using Microsoft.Rest;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training;
// using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training.Models;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction.Models;
using SkiaSharp;

namespace read_journal_customvision
{
    public class ImageEntry
    {
        public string Path { get; set; } = "";
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
            // Flags
            bool saveImages = args.Any(a => a.Equals("--save-images", StringComparison.OrdinalIgnoreCase));
            bool verbose = args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase));

            // Input: folder or JSON
            string inputArg = args.FirstOrDefault(a => !a.StartsWith("--")) ?? DefaultFolder;
            bool isJson = inputArg.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                              && File.Exists(inputArg);

            List<ImageEntry> entries;
            string baseInputFolder;

            if (isJson)
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                string json = File.ReadAllText(inputArg);
                entries = System.Text.Json.JsonSerializer
                    .Deserialize<List<ImageEntry>>(json, options)
                    ?? new List<ImageEntry>();

                // normalize paths
                string folder = Path.GetDirectoryName(Path.GetFullPath(inputArg)) ?? Directory.GetCurrentDirectory();
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
                    .EnumerateFiles(inputArg, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => new[] { ".jpg", ".jpeg" }
                        .Contains(Path.GetExtension(f).ToLowerInvariant()))
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

            // Load settings
            var cfg = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            // Custom Vision endpoints & keys
            string trainEndpoint = cfg["CustomVisionTrainingEndpoint"];
            string trainKey = cfg["CustomVisionTrainingKey"];
            string predEndpoint = cfg["CustomVisionPredictionEndpoint"];
            string predKey = cfg["CustomVisionPredictionKey"];
            Guid projectId = Guid.Parse(cfg["CustomVisionProjectId"]);
            string modelName = cfg["CustomVisionPublishedName"];

            if (string.IsNullOrWhiteSpace(trainEndpoint) || string.IsNullOrWhiteSpace(trainKey) ||
                string.IsNullOrWhiteSpace(predEndpoint) || string.IsNullOrWhiteSpace(predKey) ||
                projectId == Guid.Empty || string.IsNullOrWhiteSpace(modelName))
            {
                Console.Error.WriteLine("Missing one of the Custom Vision settings in appsettings.json");
                return;
            }

            // Create clients
            var trainingClient = new CustomVisionTrainingClient(
                new Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training.ApiKeyServiceClientCredentials(trainKey))
            { Endpoint = trainEndpoint };

            var predictionClient = new CustomVisionPredictionClient(
                new Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction.ApiKeyServiceClientCredentials(predKey))
            { Endpoint = predEndpoint };

            // Prepare output folder & aggregator
            string outputBase = Path.Combine(baseInputFolder, OutputFolder);
            Directory.CreateDirectory(outputBase);
            string aggPath = Path.Combine(outputBase, "aggregator.txt");
            using var aggWriter = new StreamWriter(aggPath, append: false) { AutoFlush = true };

            int pageCount = 0;
            var overallSw = Stopwatch.StartNew();

            // Process each entry
            foreach (var entry in entries)
            {
                ProcessImageFile(
                    entry.Path,
                    entry.Split,
                    trainingClient,
                    predictionClient,
                    projectId,
                    modelName,
                    saveImages,
                    verbose,
                    outputBase,
                    ref pageCount,
                    aggWriter);
            }

            overallSw.Stop();
            Console.WriteLine($"\nProcessed {pageCount} page(s) in {overallSw.Elapsed:c}.");
        }

        static void ProcessImageFile(
            string imagePath,
            bool split,
            CustomVisionTrainingClient trainingClient,
            CustomVisionPredictionClient predictionClient,
            Guid projectId,
            string publishedModel,
            bool saveImages,
            bool verbose,
            string outputBase,
            ref int pageCount,
            StreamWriter aggregatorWriter)
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
                pageCount++;
                string pageName = baseName + suffix;
                ProcessPage(
                    bmp,
                    predictionClient,
                    projectId,
                    publishedModel,
                    saveImages,
                    verbose,
                    outputBase,
                    pageName,
                    ref aggregatorWriter);
            }
        }

        static void ProcessPage(
    SKBitmap bmp,
    CustomVisionPredictionClient predictionClient,
    Guid projectId,
    string publishedModel,
    bool saveImages,
    bool verbose,
    string outputBase,
    string pageName,
    ref StreamWriter aggWriter)
        {
            // Setup logging: console → per-page + aggregator
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
                Console.WriteLine($"[INFO] Encoding page to JPEG for prediction...");
                using var imgData = SKImage.FromBitmap(bmp)
                                          .Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
                using var ms = new MemoryStream(imgData.ToArray());
                Console.WriteLine($"[INFO] JPEG encoding complete. Size: {ms.Length} bytes.");

                Console.WriteLine($"[INFO] Sending image to Custom Vision Prediction API...");
                Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction.Models.ImagePrediction result = predictionClient
                    .ClassifyImageAsync(projectId, publishedModel, ms)
                    .GetAwaiter().GetResult();
                Console.WriteLine($"[INFO] Prediction API call complete. {result.Predictions.Count} tags returned.");

                Console.WriteLine("Predicted tags:");
                foreach (var tag in result.Predictions.OrderByDescending(t => t.Probability))
                    Console.WriteLine($"  {tag.TagName}: {tag.Probability:P2}");

                // Optionally save annotated image
                if (saveImages && verbose)
                {
                    string outJpg = Path.Combine(outputBase, pageName + "_tags.jpg");
                    Console.WriteLine($"[INFO] Annotating and saving image to: {outJpg}");
                    AnnotateTags(bmp, result.Predictions, outJpg);
                    Console.WriteLine($"[INFO] Annotated image saved.");
                }
                else if (saveImages)
                {
                    Console.WriteLine("[INFO] Skipping annotation (requires --verbose).");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Exception in {pageName}: {ex}");
                if (ex.InnerException != null)
                    Console.Error.WriteLine($"[ERROR] Inner exception: {ex.InnerException}");
            }
            finally
            {
                sw.Stop();
                Console.WriteLine($"[INFO] Done in {sw.Elapsed:c}\n");
                Console.SetOut(origOut);
                Console.SetError(origErr);
            }
        }

        static SKBitmap Subset(SKBitmap src, int x1, int y1, int x2, int y2)
        {
            var dst = new SKBitmap(x2 - x1, y2 - y1);
            src.ExtractSubset(dst, new SKRectI(x1, y1, x2, y2));
            return dst;
        }

        static void AnnotateTags(SKBitmap src, IList<PredictionModel> tags, string outputPath)
        {
            using var bmp = new SKBitmap(src.Info);
            using var canvas = new SKCanvas(bmp);
            canvas.DrawBitmap(src, 0, 0);

            using var paint = new SKPaint
            {
                Color = SKColors.Yellow,
                IsAntialias = true
            };

            using var font = new SKFont
            {
                Size = 24
            };

            int y = 30;
            foreach (var tag in tags)
            {
                canvas.DrawText($"{tag.TagName}: {tag.Probability:P2}", 10, y, font, paint);
                y += 30;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var outStream = new SKFileWStream(outputPath);
            bmp.Encode(outStream, SKEncodedImageFormat.Jpeg, JpegQuality);
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
