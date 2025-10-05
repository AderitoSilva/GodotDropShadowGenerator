using Godot;
using System.Buffers;
using NVector4 = System.Numerics.Vector4;  // This is to avoid conflicts with `Godot.Vector4`.


#nullable enable  // You can remove this if you have nullable enabled in your project.


namespace Godot.EditorTools;

/// <summary>
/// Tool that can be configured and stored as a resource, and can be used to generate
/// drop shadow images for the textures selected in its configuration.
/// </summary>
/// <remarks>
/// <para>
/// Use the "Generate" button in the tool's dock panel to generate drop shadows for
/// the textures in <see cref="OutputFileNames"/>.
/// </para>
/// </remarks>
[Tool]
[GlobalClass]
public partial class DropShadowGenerator : Resource
{


    private float[]? _cachedGaussianKernel;  // Caches the Gaussian weights for a specific blur radius.
    private int _cachedGaussianKernelRadii;  // The blur radius for which the cached Gaussian weights are stored.


    [ExportToolButton("Generate")]
    private Callable GenerateButton => Callable.From(Generate);


    /// <summary>
    /// The directory where the generated drop shadow images will be save to.
    /// </summary>
    [ExportGroup("Files")]
    [Export(PropertyHint.Dir)]
    public string? OutputDirectory { get; set; }


    /// <summary>
    /// A dictionary that maps the source textures to the file names for their generated
    /// drop shadow images.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file names don't need to include a file name extension.
    /// </para>
    /// </remarks>
    [Export]
    public global::Godot.Collections.Dictionary<Texture2D, string?>? OutputFileNames { get; set; }


    [ExportGroup("Shadow")]
    [Export(PropertyHint.Range, "0,250,1,or_greater")]
    public int BlurRadius { get; set; } = 10;


    [Export]
    public Color ShadowColor { get; set; } = new(0f, 0f, 0f, 1f);


    /// <summary>
    /// Generate the drop shadow images.
    /// </summary>
    public virtual void Generate()
    {
        // Validate state.
        if (OutputFileNames is null || OutputFileNames.Count == 0)
        {
            GD.PushWarning("No drop shadow image is configured for generation.");
            return;
        }

        string? outputDirectory = OutputDirectory?.AsSpan().Trim().TrimEnd('/').ToString();
        if (string.IsNullOrEmpty(outputDirectory))
        {
            GD.PushError($"'{PropertyName.OutputDirectory}' property is not set.");
            return;
        }

        // Create output directory, if it doesn't exist.
        if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(outputDirectory)))
        {
            Error result = DirAccess.MakeDirAbsolute(ProjectSettings.GlobalizePath(outputDirectory));
            if (result != Error.Ok)
            {
                GD.PushError($"Failed to create output directory '{outputDirectory}'.");
                return;
            }
        }

        // Generate drop shadow images.
        GD.Print($"Generating drop shadow images. {OutputFileNames.Count} images will be processed.");
        long startTimestamp = Stopwatch.GetTimestamp();  // Snapshot the current timestamp, for benchmark printing.
        int blurRadius = Math.Clamp(BlurRadius, 0, 512);
        Color shadowColor = ShadowColor;
        int generatedCount = 0;  // Count the generated images, for printing.
        foreach (var (texture, fileName) in OutputFileNames)
        {
            // Validate configured texture->file-name mapping.
            if (string.IsNullOrWhiteSpace(fileName))
            {
                GD.PushWarning("A texture has no configured drop shadow output file name.");
                continue;
            }
            if (texture is null)
            {
                GD.PushWarning($"No texture specified for output file '{fileName}'.");
                continue;
            }

            // Get the image of the texture.
            using Image? image = texture.GetImage();
            if (image is null)
            {
                GD.PushWarning($"Could not access the image data of the texture for output file '{fileName}'.");
                continue;
            }

            // Generate a drop shadow for the image.
            using Image dropShadowImage = GenerateDropShadow(image, shadowColor, blurRadius);

            // Save the generated drop shadow image.
            string outputPath = outputDirectory + "/" + fileName.AsSpan().Trim().TrimStart('/').ToString();
            if (!outputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                outputPath += ".png";
            }
            if (dropShadowImage.SavePng(outputPath) == Error.Ok)
            {
                GD.Print($"Generated drop shadow image at '{outputPath}'.");
                generatedCount++;
            }
            else
            {
                GD.PushError($"Failed to save drop shadow image at '{outputPath}'.");
            }
        }
        TimeSpan processingDuration = Stopwatch.GetElapsedTime(startTimestamp);
        GD.Print($"Drop shadow generation complete. {generatedCount} images generated " +
            $"in {processingDuration.TotalSeconds:0.000} seconds.");
    }


    protected Image GenerateDropShadow(Image image, Color color, int blurRadius)
    {
        int sourceWidth = image.GetWidth();
        int sourceHeight = image.GetHeight();
        int destinationWidth = sourceWidth + blurRadius * 2;
        int destinationHeight = sourceHeight + blurRadius * 2;

        // Rent working buffers.
        var pool = ArrayPool<NVector4>.Shared;
        NVector4[] buffer = pool.Rent(destinationWidth * destinationHeight);
        byte[] destinationData = ArrayPool<byte>.Shared.Rent(destinationWidth * destinationHeight * 4);

        try
        {
            // Ensure input is RGBA8.
            if (image.GetFormat() != Image.Format.Rgba8)
            {
                image.Convert(Image.Format.Rgba8);
            }

            byte[] sourceData = image.GetData();

            // Initialize padding pixels to transparent. This is needed because we are
            // using a rented buffer from the pool, that might not have been cleared.
            {
                // Initialize padding rows (top and bottom) to transparent.
                for (int y = 0; y < blurRadius; y++)
                {
                    int rowStart = y * destinationWidth;
                    int rowEnd = (destinationHeight - 1 - y) * destinationWidth;
                    for (int x = 0; x < destinationWidth; x++)
                    {
                        buffer[rowStart + x] = NVector4.Zero;
                        buffer[rowEnd + x] = NVector4.Zero;
                    }
                }

                // Initialize padding columns (left and right) to transparent.
                for (int y = blurRadius; y < destinationHeight - blurRadius; y++)
                {
                    int leftIdx = y * destinationWidth;
                    int rightIdx = leftIdx + destinationWidth - 1;
                    for (int x = 0; x < blurRadius; x++)
                    {
                        buffer[leftIdx + x] = NVector4.Zero;
                        buffer[rightIdx - x] = NVector4.Zero;
                    }
                }
            }

            // Copy + apply shadow color into padded buffer.
            for (int y = 0; y < sourceHeight; y++)
            {
                for (int x = 0; x < sourceWidth; x++)
                {
                    int sourceIdx = (y * sourceWidth + x) * 4;
                    float alpha = sourceData[sourceIdx + 3] / 255f;
                    if (alpha <= 0f)
                    {
                        // Fill transparent for this pixel.
                        int dstIdx = (y + blurRadius) * destinationWidth + (x + blurRadius);
                        buffer[dstIdx] = NVector4.Zero;
                        continue;
                    }

                    int destinationIdx = (y + blurRadius) * destinationWidth + (x + blurRadius);
                    buffer[destinationIdx] = new NVector4(color.R, color.G, color.B, color.A * alpha);
                }
            }

            // Apply blur in place.
            ApplyGaussianBlur(buffer, destinationWidth, destinationHeight, blurRadius);

            // Convert back to byte[].
            Parallel.For(0, destinationWidth * destinationHeight, i =>
            {
                NVector4 v = buffer[i];
                int idx = i * 4;
                destinationData[idx] = (byte)(Math.Clamp(v.X, 0f, 1f) * 255);
                destinationData[idx + 1] = (byte)(Math.Clamp(v.Y, 0f, 1f) * 255);
                destinationData[idx + 2] = (byte)(Math.Clamp(v.Z, 0f, 1f) * 255);
                destinationData[idx + 3] = (byte)(Math.Clamp(v.W, 0f, 1f) * 255);
            });

            // Create final image.
            Image shadowImage = Image.CreateFromData(
                destinationWidth, destinationHeight,
                false, Image.Format.Rgba8,
                destinationData.AsSpan(0, destinationWidth * destinationHeight * 4));

            return shadowImage;
        }
        finally
        {
            pool.Return(buffer, clearArray: false);
            ArrayPool<byte>.Shared.Return(destinationData, clearArray: false);
        }
    }


    private void ApplyGaussianBlur(NVector4[] buffer, int width, int height, int radius)
    {
        if (radius <= 0 || width <= 0 || height <= 0)
            return;

        // Compute Gaussian weights (cached, based on radius).
        float[] kernel = ComputeGaussianKernel(radius);

        // Rent a temporary buffer.
        var pool = ArrayPool<NVector4>.Shared;
        NVector4[] tmp = pool.Rent(width * height);

        try
        {
            // Horizontal pass → tmp (parallel over rows).
            Parallel.For(0, height, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    NVector4 sum = NVector4.Zero;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int nx = Math.Clamp(x + k, 0, width - 1);
                        sum += buffer[row + nx] * kernel[k + radius];
                    }
                    tmp[row + x] = sum;
                }
            });

            // Vertical pass → buffer (parallel over rows).
            Parallel.For(0, height, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    NVector4 sum = NVector4.Zero;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int ny = Math.Clamp(y + k, 0, height - 1);
                        sum += tmp[ny * width + x] * kernel[k + radius];
                    }
                    buffer[row + x] = sum;
                }
            });
        }
        finally
        {
            pool.Return(tmp, clearArray: false);
        }
    }


    private float[] ComputeGaussianKernel(int radius)
    {
        // If there are already cached weights for the provided radius, return those.
        if (_cachedGaussianKernel is not null && _cachedGaussianKernel.Length > 0
            && _cachedGaussianKernelRadii == radius)
        {
            return _cachedGaussianKernel;
        }

        // Compute the Gaussian weights.
        float sigma = radius / 2f;
        float twoSigmaSq = 2 * sigma * sigma;
        float[] kernel = new float[radius * 2 + 1];
        float sum = 0;
        for (int i = -radius; i <= radius; i++)
        {
            float v = Mathf.Exp(-(i * i) / twoSigmaSq);
            kernel[i + radius] = v;
            sum += v;
        }
        for (int i = 0; i < kernel.Length; i++)
            kernel[i] /= sum;

        // Cache the computed Gaussian weights for the provided radius.
        _cachedGaussianKernel = kernel;
        _cachedGaussianKernelRadii = radius;

        // Return the computed weights.
        return kernel;
    }


}
