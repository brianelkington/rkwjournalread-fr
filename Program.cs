using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using OpenAI.Chat;
using System.ClientModel;
using Microsoft.Extensions.Configuration;
using SkiaSharp;
using System.Net.Http;

#nullable enable

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
        private static string TranslatorEndpoint = "";
        private static string TranslatorKey = "";
        private static string TranslatorRegion = "";
        private static string OpenAIEndpoint = "";
        private static string OpenAIKey = "";
        private static string OpenAIDeployment = "";

        static void Main(string[] args)
        {
            bool saveImages = args.Any(a => a.Equals("--save-images", StringComparison.OrdinalIgnoreCase));
            bool verbose = args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase));
            bool correctTranslations = args.Any(a => a.Equals("--correct-translations", StringComparison.OrdinalIgnoreCase));

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

            TranslatorEndpoint = cfg["TranslatorEndpoint"] ?? "";
            TranslatorKey = cfg["TranslatorKey"] ?? "";
            TranslatorRegion = cfg["TranslatorRegion"] ?? "";
            OpenAIEndpoint = cfg["OpenAIEndpoint"] ?? "";
            OpenAIKey = cfg["OpenAIKey"] ?? "";
            OpenAIDeployment = cfg["OpenAIDeployment"] ?? "";

            if (string.IsNullOrWhiteSpace(TranslatorEndpoint) ||
                string.IsNullOrWhiteSpace(TranslatorKey) ||
                string.IsNullOrWhiteSpace(TranslatorRegion))
            {
                Console.Error.WriteLine("Missing Translator configuration in appsettings.json; German translation will be skipped.");
            }

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine("Missing DocumentAnalysisEndpoint or DocumentAnalysisKey in appsettings.json");
                return;
            }

            if (string.IsNullOrWhiteSpace(OpenAIEndpoint) ||
                string.IsNullOrWhiteSpace(OpenAIKey) ||
                string.IsNullOrWhiteSpace(OpenAIDeployment))
            {
                Console.Error.WriteLine("Missing OpenAI configuration in appsettings.json; OCR correction will be skipped.");
            }

            // Create client
            var client = new DocumentAnalysisClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

            // Create OpenAI client if configured
            AzureOpenAIClient? openAiClient = null;
            if (!string.IsNullOrWhiteSpace(OpenAIEndpoint) && !string.IsNullOrWhiteSpace(OpenAIKey))
            {
                openAiClient = new AzureOpenAIClient(new Uri(OpenAIEndpoint), new AzureKeyCredential(OpenAIKey));
            }

            // Prepare output
            string outputBase = Path.Combine(baseInputFolder, OutputFolder);
            Directory.CreateDirectory(outputBase);
            string aggregatorFileName = $"aggregator_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string aggregatorPath = Path.Combine(outputBase, aggregatorFileName);
            using var aggWriter = new StreamWriter(aggregatorPath, append: false) { AutoFlush = true };

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
                    aggWriter,
                    openAiClient,
                    correctTranslations // pass flag
                );
            }

            overallSw.Stop();
            Console.WriteLine($"\nProcessed {totalWordCount} words total.");
            Console.WriteLine($"Overall average word confidence: {(totalWordCount > 0
                ? totalWordConf / totalWordCount
                : 0):P2}");
            Console.WriteLine($"Total time: {overallSw.Elapsed:c}");
        }

        // Update method signatures to accept the flag
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
            StreamWriter aggWriter,
            AzureOpenAIClient? openAiClient,
            bool correctTranslations // add parameter
        )
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
                    aggWriter,
                    openAiClient,
                    correctTranslations // pass flag
                );

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
            StreamWriter aggWriter,
            AzureOpenAIClient? openAiClient,
            bool correctTranslations // add parameter
        )
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
                    var sbText = new StringBuilder();
                    var sentences = new List<string>();
                    var sentenceConfidences = new List<double>();

                    foreach (var page in result.Pages)
                    {
                        foreach (var line in page.Lines)
                        {
                            Console.WriteLine($"  {line.Content}");
                            sbText.AppendLine(line.Content);

                            // Split line into sentences using basic punctuation
                            var lineSentences = line.Content
                                .Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .ToList();

                            sentences.AddRange(lineSentences);

                            // Estimate confidence for each sentence as average of word confidences in the line
                            if (line.Spans != null && line.Spans.Count > 0)
                            {
                                // Find words that are within the spans of this line
                                var pageWords = page.Words;
                                var spanStart = line.Spans[0].Index;
                                var spanEnd = line.Spans[line.Spans.Count - 1].Index + line.Spans[line.Spans.Count - 1].Length;
                                var wordsInLine = pageWords
                                    .Where(w => w.Span.Index >= spanStart && (w.Span.Index + w.Span.Length) <= spanEnd)
                                    .ToList();
                                double avgConf = wordsInLine.Any() ? wordsInLine.Average(w => w.Confidence) : 0.0;
                                for (int i = 0; i < lineSentences.Count; i++)
                                    sentenceConfidences.Add(avgConf);
                            }
                            else
                            {
                                for (int i = 0; i < lineSentences.Count; i++)
                                    sentenceConfidences.Add(0.0);
                            }
                        }
                    }

                    // --- Language detection, correction, and translation ---
                    var englishLines = new List<string>();
                    var germanLines = new List<string>();

                    ChatClient? chatClient = null;
                    if (openAiClient != null)
                        chatClient = openAiClient.GetChatClient(OpenAIDeployment);

                    for (int i = 0; i < sentences.Count; i++)
                    {
                        var sentence = sentences[i];
                        var confidence = sentenceConfidences[i];
                        string? lang = DetectLanguage(sentence);

                        if (lang != null && lang.StartsWith("de", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"********** German: {sentence} (Confidence: {confidence:P2})");
                            germanLines.Add($"German: {sentence} (Confidence: {confidence:P2})");

                            string textToTranslate = sentence;
                            if (correctTranslations && chatClient != null)
                            {
                                var prompt = $"Correct any OCR errors in this sentence, especially those caused by cursive handwriting: \"{sentence}\"";
                                ChatMessage[] messages =
                                {
                                    new SystemChatMessage("You are an expert at correcting OCR errors in handwritten text."),
                                    new UserChatMessage(prompt)
                                };

                                var options = new ChatCompletionOptions { Temperature = 0.2f };
                                ChatCompletion response = chatClient.CompleteChat(messages, options);
                                textToTranslate = response.Content[0].Text.Trim();
                            }

                            string? translated = TranslateGermanToEnglish(textToTranslate);
                            if (!string.IsNullOrWhiteSpace(translated))
                            {
                                Console.WriteLine($"********** English Translation: {translated} (Confidence: {confidence:P2})");
                                englishLines.Add($"English: {translated} (Confidence: {confidence:P2})");
                                aggWriter.WriteLine("***** " + translated);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"********** English: {sentence} (Confidence: {confidence:P2})");
                            englishLines.Add($"English: {sentence} (Confidence: {confidence:P2})");
                            aggWriter.WriteLine("***** " + sentence);
                        }
                    }

                    if (englishLines.Any())
                    {
                        string enPath = Path.Combine(outputBase, pageName + "_en.txt");
                        File.WriteAllLines(enPath, englishLines);
                        if (verbose)
                            Console.WriteLine($"\nEnglish text saved to {Path.GetFileName(enPath)}");
                    }

                    if (germanLines.Any())
                    {
                        string dePath = Path.Combine(outputBase, pageName + "_de.txt");
                        File.WriteAllLines(dePath, germanLines);
                        if (verbose)
                            Console.WriteLine($"\nGerman text saved to {Path.GetFileName(dePath)}");
                    }
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

        static string? DetectLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (string.IsNullOrWhiteSpace(TranslatorEndpoint) ||
                string.IsNullOrWhiteSpace(TranslatorKey) ||
                string.IsNullOrWhiteSpace(TranslatorRegion))
                return null;
            try
            {
                using var http = new HttpClient();
                string url = TranslatorEndpoint.TrimEnd('/') + "/translator/text/v3.0/detect?api-version=3.0";
                var body = JsonSerializer.Serialize(new[] { new { Text = text } });
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                req.Headers.Add("Ocp-Apim-Subscription-Key", TranslatorKey);
                req.Headers.Add("Ocp-Apim-Subscription-Region", TranslatorRegion);

                var resp = http.SendAsync(req).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement[0].GetProperty("language").GetString();
            }
            catch
            {
                return null;
            }
        }

        static string? TranslateGermanToEnglish(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (string.IsNullOrWhiteSpace(TranslatorEndpoint) ||
                string.IsNullOrWhiteSpace(TranslatorKey) ||
                string.IsNullOrWhiteSpace(TranslatorRegion))
                return null;
            try
            {
                using var http = new HttpClient();
                string url = TranslatorEndpoint.TrimEnd('/') + "/translator/text/v3.0/translate?api-version=3.0&to=en";
                var body = JsonSerializer.Serialize(new[] { new { Text = text } });
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                req.Headers.Add("Ocp-Apim-Subscription-Key", TranslatorKey);
                req.Headers.Add("Ocp-Apim-Subscription-Region", TranslatorRegion);

                var resp = http.SendAsync(req).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                var first = doc.RootElement[0];
                return first.GetProperty("translations")[0].GetProperty("text").GetString();
            }
            catch
            {
                return null;
            }
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
        public override void Write(string? s) { _a.Write(s); _b.Write(s); }
        public override void WriteLine(string? s) { _a.WriteLine(s); _b.WriteLine(s); }
        public override void Flush() { _a.Flush(); _b.Flush(); }
    }
}
