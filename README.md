# RKW Journal Reader - Document Analysis

This .NET console application processes scanned journal images using Azure AI Document Intelligence (Form Recognizer). It extracts text and word confidences, optionally annotates images, and aggregates results. German sentences are automatically detected and translated to English.

## Features

- Processes images from a folder or a JSON list.
- Uses Azure Form Recognizer (Document Intelligence) for OCR.
- Splits double-page scans if needed.
- Outputs recognized text, word confidences, and per-page logs.
- Optionally saves annotated images with word bounding boxes.
- Aggregates results and statistics.
- **German sentences are detected and translated to English.**  
  Translations are saved per page and also written to `aggregator.txt` with a `***** ` prefix for easy identification.

## Usage

1. **Configure Azure Credentials**

   Edit `appsettings.json` with your Azure Form Recognizer and Translator endpoint and keys:
   ```json
   {
     "DocumentAnalysisEndpoint": "<your-form-recognizer-endpoint>",
     "DocumentAnalysisKey": "<your-form-recognizer-key>",
     "DocumentAnalysisModelId": "prebuilt-read",
     "TranslatorEndpoint": "<your-translator-endpoint>",
     "TranslatorKey": "<your-translator-key>",
     "TranslatorRegion": "<your-translator-region>"
   }
   ```

2. **Run the Application**

   ```sh
   dotnet run -- [input-folder-or-json] [--save-images] [--verbose]
   ```

   - `input-folder-or-json`: Folder with `.jpg`/`.jpeg` images or a JSON file listing images.
   - `--save-images`: Save annotated images with word bounding boxes (requires `--verbose`).
   - `--verbose`: Print detailed output and confidences.

3. **Output**

   - Results are saved in an `image_out` folder inside your input directory.
   - Per-page logs: `<image>_L.out`, `<image>_R.out`, etc.
   - Aggregated text: `aggregator.txt`
   - Annotated images: `<image>_words.jpg` (if enabled)
   - Per-page text: `<image>.txt` with English sentences and German sentences alongside their English translations

## Requirements

- .NET 6 or later
- Azure Form Recognizer resource and API key
- Azure Translator resource and API key
- SkiaSharp NuGet package

## Notes

- Double-page scans are split automatically if `Split` is true in the JSON or by default for folder input.
- Word confidences and bounding boxes are available in verbose mode.
- German sentence detection and translation is performed sentence-by-sentence for improved accuracy.
- See `Program.cs` for implementation details.

## License

This project is licensed under the MIT License. See