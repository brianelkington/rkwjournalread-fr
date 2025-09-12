# RKW Journal Reader - Document Analysis

This .NET console application processes scanned journal images using Azure AI Document Intelligence (Form Recognizer) and optionally Azure OpenAI for OCR correction. It extracts text, word confidences, optionally annotates images, and aggregates results. German sentences are automatically detected and translated to English, with confidence scores included.

## Features

- Processes images from a folder or a JSON list.
- Uses Azure Form Recognizer (Document Intelligence) for OCR.
- Optionally uses Azure OpenAI to correct OCR errors (triggered by `--correct-translations`).
- Splits double-page scans if needed.
- Outputs recognized text, word confidences, and per-page logs.
- Optionally saves annotated images with word bounding boxes.
- Aggregates results and statistics.
- **German sentences are detected and translated to English.**  
  Translations are saved per page and also written to an aggregator file with a `*****` prefix for easy identification.
- Confidence scores are included for all recognized, corrected, and translated text.

## Usage

1. **Configure Azure Credentials**

   Edit `appsettings.json` with your Azure Form Recognizer, Translator, and OpenAI endpoints and keys:
   ```json
   {
     "DocumentAnalysisEndpoint": "<your-form-recognizer-endpoint>",
     "DocumentAnalysisKey": "<your-form-recognizer-key>",
     "DocumentAnalysisModelId": "prebuilt-read",
     "TranslatorEndpoint": "<your-translator-endpoint>",
     "TranslatorKey": "<your-translator-key>",
     "TranslatorRegion": "<your-translator-region>",
     "OpenAIEndpoint": "<your-azure-openai-endpoint>",
     "OpenAIKey": "<your-azure-openai-key>",
     "OpenAIDeployment": "<your-openai-deployment-name>"
   }
   ```

2. **Run the Application**

   ```sh
   dotnet run -- [input-folder-or-json] [--save-images] [--verbose] [--correct-translations]
   ```

   - `input-folder-or-json`: Folder with `.jpg`/`.jpeg` images or a JSON file listing images.
   - `--save-images`: Save annotated images with word bounding boxes (requires `--verbose`).
   - `--verbose`: Print detailed output and confidences.
   - `--correct-translations`: Use Azure OpenAI to correct OCR errors before translation.

3. **Output**

   - Results are saved in an `image_out` folder inside your input directory.
   - Per-page logs: `<image>_L.out`, `<image>_R.out`, etc.
   - Aggregated text: `aggregator_<timestamp>.txt`
   - Annotated images: `<image>_words.jpg` (if enabled)
   - Translations: `<image>_L_en.txt`, `<image>_R_en.txt` (includes OCR German, corrected text, and English translation with confidence scores)

## Requirements

- .NET 6 or later
- Azure Form Recognizer resource and API key
- Azure Translator resource and API key
- Azure OpenAI resource and API key (if using correction)
- SkiaSharp NuGet package

## Notes

- Double-page scans are split automatically if `Split` is true in the JSON or by default for folder input.
- Word confidences and bounding boxes are available in verbose mode.
- German sentence detection and translation is performed sentence-by-sentence for improved accuracy.
- If `--correct-translations` is used, translation is performed on corrected text; otherwise, on the original OCR output.
- See `Program.cs` for implementation details.

## License

This project is licensed under the MIT License. See [LICENSE.md](LICENSE.md) for details.