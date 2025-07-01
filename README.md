# Program.cs — Journal Computer Vision Processor

This C# console application processes scanned journal images using Azure AI Vision and SkiaSharp. It supports batch OCR, captioning, and confidence aggregation, with flexible input and output options.

## Features

- **Input**: Accepts a folder of JPEG images or a JSON file listing images and split flags.
- **Image Splitting**: Optionally splits double-page scans into left/right pages.
- **Azure AI Vision**: Uses Azure's `ImageAnalysisClient` for OCR, captioning, and dense captions.
- **Confidence Aggregation**: Computes and reports average word confidence across all processed images.
- **Output**:
  - Per-page logs and an aggregator summary.
  - Optionally saves annotated images with OCR results.
- **Configurable**: Reads Azure endpoint/key from `appsettings.json`.
- **Verbose Mode**: Extra details and word-level confidences with `--verbose`.

## Usage

```sh
dotnet run -- [input-folder|input.json] [--save-images] [--verbose]
```

- `input-folder`: Directory containing `.jpg`/`.jpeg` images (default: `images`)
- `input.json`: JSON file with an array of `{ "path": "...", "split": true/false }`
- `--save-images`: Save annotated JPEGs with OCR overlays.
- `--verbose`: Print detailed output, including confidences.

## Key Classes & Methods

- **ImageEntry**: Represents an image and whether to split it.
- **Main**: Parses arguments, loads config, builds image list, and orchestrates processing.
- **ProcessImageFile**: Handles splitting and per-region processing.
- **ProcessRegion**: Runs Azure Vision analysis, logs results, and aggregates confidences.
- **LoadAndOrientImage**: Loads and orients images using EXIF data.
- **AnnotateAndSave**: Draws OCR results on images and saves them.
- **TeeTextWriter**: Writes output to multiple streams (console, file, aggregator).

## Dependencies

- [Azure.AI.Vision.ImageAnalysis](https://learn.microsoft.com/azure/ai-services/computer-vision/)
- [SkiaSharp](https://github.com/mono/SkiaSharp)
- [Microsoft.Extensions.Configuration](https://learn.microsoft.com/dotnet/api/microsoft.extensions.configuration)

## Configuration

Create an `appsettings.json` file with your Azure Vision endpoint and key:

```json
{
  "AIServicesEndpoint": "https://<your-resource>.cognitiveservices.azure.com/",
  "AIServicesKey": "<your-key>"
}
```

## Output

- Annotated images and logs are saved in an `image_out` subfolder.
- Aggregated word confidence is reported at the end.

---
**Note:** Requires Azure AI Vision resource and valid credentials.