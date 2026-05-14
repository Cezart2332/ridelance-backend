using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using Application.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public sealed class LicensePlateDetectionService : ILicensePlateDetectionService, IDisposable
{
    private readonly InferenceSession _session;
    private readonly ILogger<LicensePlateDetectionService> _logger;
    private const int TargetSize = 640;
    
    // Prioritize Recall: Lower thresholds to catch more plates from various angles and distances
    private const float ConfidenceThreshold = 0.18f; 
    private const float NmsThreshold = 0.75f;

    public LicensePlateDetectionService(ILogger<LicensePlateDetectionService> logger)
    {
        _logger = logger;
        
        // Search for the model in common locations
        string[] searchPaths = 
        [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "best.onnx"),
            Path.Combine(Directory.GetCurrentDirectory(), "models", "best.onnx"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "models", "best.onnx"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "models", "best.onnx"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "models", "best.onnx")
        ];

        string? modelPath = searchPaths.FirstOrDefault(File.Exists);

        if (modelPath is null)
        {
            _logger.LogError("YOLO model not found in any of the expected locations: {Paths}", string.Join(", ", searchPaths));
            throw new FileNotFoundException("YOLOv8 ONNX model not found. Checked: " + string.Join("; ", searchPaths));
        }

        _logger.LogInformation("Initializing LicensePlateDetectionService with model: {ModelPath}", modelPath);
        
        try
        {
            using var options = new SessionOptions();
            options.AppendExecutionProvider_CPU();
            
            _session = new InferenceSession(modelPath, options);
            _logger.LogInformation("ONNX InferenceSession initialized successfully.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "CRITICAL: Failed to initialize ONNX InferenceSession. This usually indicates missing native dependencies (glibc/libgomp) on Linux/Alpine.", 
                ex);
        }
    }

    public async Task<byte[]> ProcessImageAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        try
        {
            using var memoryStream = new MemoryStream();
            await imageStream.CopyToAsync(memoryStream, cancellationToken);
            byte[] imageBytes = memoryStream.ToArray();

            using Mat src = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            if (src.Empty())
            {
                _logger.LogWarning("Failed to decode image.");
                return imageBytes;
            }

            // 1. Preprocess: Contrast Enhancement (CLAHE)
            using var lab = new Mat();
            Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);
            Mat[] channels = Cv2.Split(lab);
            using CLAHE clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new Size(8, 8));
            clahe.Apply(channels[0], channels[0]);
            Cv2.Merge(channels, lab);
            using var enhanced = new Mat();
            Cv2.CvtColor(lab, enhanced, ColorConversionCodes.Lab2BGR);
            foreach (Mat c in channels)
            {
                c.Dispose();
            }

            // 2. Letterbox Preprocessing
            float scale = Math.Min((float)TargetSize / src.Width, (float)TargetSize / src.Height);
            int newW = (int)(src.Width * scale);
            int newH = (int)(src.Height * scale);
            int padX = (TargetSize - newW) / 2;
            int padY = (TargetSize - newH) / 2;

            using Mat resized = enhanced.Resize(new Size(newW, newH));
            using var canvas = new Mat(new Size(TargetSize, TargetSize), MatType.CV_8UC3, new Scalar(114, 114, 114));
            using var targetRoi = new Mat(canvas, new Rect(padX, padY, newW, newH));
            resized.CopyTo(targetRoi);

            using Mat rgb = canvas.CvtColor(ColorConversionCodes.BGR2RGB);
            DenseTensor<float> inputTensor = Preprocess(rgb);

            // 3. Inference
            string inputName = _session.InputMetadata.Keys.First();
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);
            string outputName = _session.OutputMetadata.Keys.First();
            Tensor<float> output = results.First(r => r.Name == outputName).AsTensor<float>();

            // 4. Post-process
            List<Detection> allDetections = ParseDetections(output, scale, padX, padY);
            bool debugMode = Environment.GetEnvironmentVariable("PLATE_BLUR_DEBUG") == "true";

            _logger.LogInformation("Detection Summary: {Count} candidates above threshold {T}.", allDetections.Count, ConfidenceThreshold);

            var finalDetections = new List<Detection>();
            foreach (Detection d in allDetections)
            {
                float ratio = (float)d.Box.Width / d.Box.Height;
                
                // Shape Validation: Plates are usually wide.
                bool isCorrectShape = ratio >= 0.9f && ratio <= 8.5f;
                if (!isCorrectShape)
                {
                    _logger.LogWarning("Rejecting candidate: Ratio {Ratio:F2} is non-plate like.", ratio);
                    continue;
                }

                // Size Safeguard
                bool isTooLarge = d.Box.Width > src.Width * 0.40 || d.Box.Height > src.Height * 0.25;
                if (isTooLarge)
                {
                    _logger.LogWarning("Rejecting candidate: Too large for a plate.");
                    continue;
                }

                finalDetections.Add(d);
            }

            if (finalDetections.Count == 0)
            {
                return imageBytes;
            }

            foreach (Detection detection in finalDetections)
            {
                _logger.LogInformation("Blurring: Box({X},{Y},{W},{H}) @ Conf:{C:P0}", detection.Box.X, detection.Box.Y, detection.Box.Width, detection.Box.Height, detection.Confidence);

                if (debugMode)
                {
                    src.Rectangle(detection.Box, Scalar.Red, 4);
                    var textPoint = new Point(detection.Box.X, detection.Box.Y - 12);
                    src.PutText($"{detection.Confidence:P0} R:{(float)detection.Box.Width / detection.Box.Height:F1}", textPoint, HersheyFonts.HersheySimplex, 1.0, Scalar.Red, 2);
                }
                else
                {
                    ApplyBlur(src, detection.Box);
                }
            }

            if (debugMode)
            {
                string debugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_detections.jpg");
                src.SaveImage(debugPath);
            }

            return src.ToBytes(".jpg");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "License plate processing failed.");
            throw new InvalidOperationException("License plate processing failed.", ex);
        }
    }

    private DenseTensor<float> Preprocess(Mat rgbImage)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, TargetSize, TargetSize });
        Span<float> buffer = tensor.Buffer.Span;
        int stride = TargetSize * TargetSize;

        for (int y = 0; y < TargetSize; y++)
        {
            for (int x = 0; x < TargetSize; x++)
            {
                Vec3b color = rgbImage.At<Vec3b>(y, x);
                int offset = y * TargetSize + x;
                buffer[offset] = (float)color.Item0 / 255.0f;
                buffer[stride + offset] = (float)color.Item1 / 255.0f;
                buffer[2 * stride + offset] = (float)color.Item2 / 255.0f;
            }
        }
        return tensor;
    }

    private List<Detection> ParseDetections(Tensor<float> output, float scale, int padX, int padY)
    {
        int classesCount = output.Dimensions[1] - 4;
        int candidatesCount = output.Dimensions[2];
        var detections = new List<Detection>();
        float maxConf = 0;

        for (int i = 0; i < candidatesCount; i++)
        {
            float maxScore = 0;
            for (int c = 0; c < classesCount; c++)
            {
                float score = output[0, 4 + c, i];
                if (score > maxScore)
                {
                    maxScore = score;
                }
            }

            if (maxScore > maxConf)
            {
                maxConf = maxScore;
            }

            if (maxScore < ConfidenceThreshold)
            {
                continue;
            }

            float cx = output[0, 0, i];
            float cy = output[0, 1, i];
            float w = output[0, 2, i];
            float h = output[0, 3, i];

            float x = (cx - w / 2f - padX) / scale;
            float y = (cy - h / 2f - padY) / scale;
            float width = w / scale;
            float height = h / scale;

            detections.Add(new Detection
            {
                Box = new Rect((int)x, (int)y, (int)width, (int)height),
                Confidence = maxScore
            });
        }

        _logger.LogInformation("Raw Pass Complete. Max Confidence: {M:F3}. Threshold: {T}.", maxConf, ConfidenceThreshold);
        return PerformNms(detections);
    }

    private List<Detection> PerformNms(List<Detection> detections)
    {
        var result = new List<Detection>();
        var sorted = detections.OrderByDescending(d => d.Confidence).ToList();

        while (sorted.Count > 0)
        {
            Detection current = sorted[0];
            result.Add(current);
            sorted.RemoveAt(0);

            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                if (CalculateIou(current.Box, sorted[i].Box) > NmsThreshold)
                {
                    sorted.RemoveAt(i);
                }
            }
        }
        return result;
    }

    private float CalculateIou(Rect box1, Rect box2)
    {
        int x1 = Math.Max(box1.X, box2.X);
        int y1 = Math.Max(box1.Y, box2.Y);
        int x2 = Math.Min(box1.X + box1.Width, box2.X + box2.Width);
        int y2 = Math.Min(box1.Y + box1.Height, box2.Y + box2.Height);

        int intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        int union = box1.Width * box1.Height + box2.Width * box2.Height - intersection;

        return union <= 0 ? 0 : (float)intersection / union;
    }

    private void ApplyBlur(Mat image, Rect box)
    {
        // Expand the ROI slightly (5%) to ensure no plate edges "leak" out
        int padW = (int)(box.Width * 0.05);
        int padH = (int)(box.Height * 0.05);
        
        int x = Math.Max(0, box.X - padW);
        int y = Math.Max(0, box.Y - padH);
        int w = Math.Min(image.Width - x, box.Width + 2 * padW);
        int h = Math.Min(image.Height - y, box.Height + 2 * padH);

        if (w <= 2 || h <= 2)
        {
            return;
        }

        using var roi = new Mat(image, new Rect(x, y, w, h));
        
        int kSize = (Math.Max(w, h) / 3) | 1; 
        kSize = Math.Clamp(kSize, 31, 151);

        Cv2.GaussianBlur(roi, roi, new Size(kSize, kSize), 0);
        Cv2.MedianBlur(roi, roi, (kSize / 2) | 1);
        Cv2.GaussianBlur(roi, roi, new Size(kSize, kSize), 0);
    }

    public void Dispose() => _session?.Dispose();

    private sealed class Detection
    {
        public Rect Box { get; set; }
        public float Confidence { get; set; }
    }
}
