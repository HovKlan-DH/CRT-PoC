using Avalonia.Media.Imaging;

namespace VRT
{
    // ###########################################################################################
    // Represents a single schematic thumbnail item for display in the Schematics tab gallery.
    // ###########################################################################################
    public class SchematicThumbnail
    {
        public string Name { get; init; } = string.Empty;
        public Bitmap? ImageSource { get; init; }
    }
}