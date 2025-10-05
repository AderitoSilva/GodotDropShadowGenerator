# Godot Drop Shadow Generator
This is a simple editor tool that generates drop shadow images for a set of textures, and stores them at a given output directory. It can be useful for scenarios where you need to use baked drop shadow sprites in your 2D objects, without you having to create the drop shadow images manually.

## How to use
1. Copy the `DropShadowGenerator.cs` file to somewhere in your Godot C# project.
2. Build your project, to make the new script available in the editor.
3. Somewhere in your project, create a new resource file of type `DropShadowGenerator`. For example, in the **File System** dock panel, right-click in a folder, and select **Create New** -> **Resource...**, and then search for "DropShadowGenerator" and select it.
4. Now, you can generate drop shadows:
    - Set the `OutputDirectory` property with the directory where you want to save the generated drop shadow images.
    - Add key-value pairs to the `OutputFileNames` dictionary, where the keys are the textures/images you want to generate drop shadows for and the values are the file names of the generated drop shadow files. The file names don't need to have an extension.
    - Set the `BlurRadius` and `ShadowColor` however you like.
    - Click the **Generate** button in the panel to generate the drop shadow images. The output panel displays information about the generation process.
    - Your drop shadow image files should now be available at the directory you specified (i.e. `OutputDirectory`).

## Additional Details
- The genarated drop shadow images are saved in PNG format.
- The resolution of the generated images is padded as necessary to make room for the blur radius.
- The tool processes `Texture2D` instances, instead of `Image` files. This is to make it more convenient to use the default import behavior of Godot, where image files are imported as `Texture2D` by default. This also makes it possible for you to provide textures from other sources (for example, `ViewportTexture`), instead of just images.
- Although this is a simple tool, it is relatively optimized for performance. It uses buffer pooling, paralell processing and SIMD vector math for faster image processing.

## Compatibility
This tool was created for Godot 4.5, but it might work well in older versions.

## Footnotes
I'm providing this tool here just as a drop-in script, since it is just a single C# file that I made for my own usage. If you would like me to publish a NuGet package that you can more convenientely reference in your projects, please let me know.
