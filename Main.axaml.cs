using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CRT
{
    public partial class Main : Window
    {
        // Zoom
        private Matrix _schematicsMatrix = Matrix.Identity;
        private const double SchematicsZoomFactor = 1.2;
        private const double SchematicsMinZoom = 0.9;
        private const double SchematicsMaxZoom = 20.0;

        // Thumbnails
        private List<SchematicThumbnail> _currentThumbnails = [];
        private const int ThumbnailMaxWidth = 800;

        // Full-res viewer
        private Bitmap? _currentFullResBitmap;
        private CancellationTokenSource? _fullResLoadCts;

        // Panning
        private bool _isPanning;
        private Point _panStartPoint;
        private Matrix _panStartMatrix;

        public Main()
        {
            InitializeComponent();
            this.SubscribePanelSizeChanges();
            this.HardwareComboBox.SelectionChanged += this.OnHardwareSelectionChanged;
            this.BoardComboBox.SelectionChanged += this.OnBoardSelectionChanged;
            this.SchematicsThumbnailList.SelectionChanged += this.OnSchematicsThumbnailSelectionChanged;
            this.PopulateHardwareDropDown();
        }

        // ###########################################################################################
        // Subscribes to Bounds property changes on each panel to update the size labels in real-time.
        // ###########################################################################################
        private void SubscribePanelSizeChanges()
        {
            this.LeftPanel.PropertyChanged += (s, e) =>
            {
                if (e.Property == Visual.BoundsProperty)
                    this.LeftSizeLabel.Text = $"{this.LeftPanel.Bounds.Width:F0} × {this.LeftPanel.Bounds.Height:F0}";
            };

            this.RightPanel.PropertyChanged += (s, e) =>
            {
                if (e.Property == Visual.BoundsProperty)
                    this.RightSizeLabel.Text = $"{this.RightPanel.Bounds.Width:F0} × {this.RightPanel.Bounds.Height:F0}";
            };
        }

        // ###########################################################################################
        // Populates the hardware drop-down with distinct, sorted hardware names from loaded data.
        // Automatically selects the first entry and triggers the board drop-down to populate.
        // ###########################################################################################
        private void PopulateHardwareDropDown()
        {
            var hardwareNames = DataManager.HardwareBoards
                .Select(e => e.HardwareName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            this.HardwareComboBox.ItemsSource = hardwareNames;

            if (hardwareNames.Count > 0)
                this.HardwareComboBox.SelectedIndex = 0;
        }

        // ###########################################################################################
        // Filters the board drop-down to only show boards belonging to the selected hardware.
        // ###########################################################################################
        private void OnHardwareSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var selectedHardware = this.HardwareComboBox.SelectedItem as string;

            var boards = DataManager.HardwareBoards
                .Where(entry => string.Equals(entry.HardwareName, selectedHardware, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.BoardName)
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .ToList();

            this.BoardComboBox.ItemsSource = boards;
            this.BoardComboBox.SelectedIndex = boards.Count > 0 ? 0 : -1;
        }

        // ###########################################################################################
        // Handles board selection changes - lazily loads board data and builds the thumbnail gallery
        // from the "Board schematics" sheet. Full-resolution bitmaps are loaded on a background thread,
        // then pre-scaled to thumbnail size on the UI thread before the originals are disposed.
        // ###########################################################################################
        private async void OnBoardSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            foreach (var thumb in this._currentThumbnails)
                (thumb.ImageSource as IDisposable)?.Dispose();
            this._currentThumbnails = [];
            this.SchematicsThumbnailList.ItemsSource = null;
            this.ResetSchematicsViewer();

            var selectedHardware = this.HardwareComboBox.SelectedItem as string;
            var selectedBoard = this.BoardComboBox.SelectedItem as string;

            if (string.IsNullOrEmpty(selectedHardware) || string.IsNullOrEmpty(selectedBoard))
                return;

            var entry = DataManager.HardwareBoards.FirstOrDefault(e =>
                string.Equals(e.HardwareName, selectedHardware, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.BoardName, selectedBoard, StringComparison.OrdinalIgnoreCase));

            if (entry == null || string.IsNullOrWhiteSpace(entry.ExcelDataFile))
                return;

            var boardData = await DataManager.LoadBoardDataAsync(entry);
            if (boardData == null)
                return;

            // Load full-resolution bitmaps on a background thread
            var loaded = await Task.Run(() =>
            {
                var result = new List<(string Name, string FullPath, Bitmap? FullBitmap)>();

                foreach (var schematic in boardData.Schematics)
                {
                    if (string.IsNullOrWhiteSpace(schematic.SchematicImageFile))
                        continue;

                    var fullPath = Path.Combine(DataManager.DataRoot,
                        schematic.SchematicImageFile.Replace('/', Path.DirectorySeparatorChar));

                    Bitmap? bitmap = null;
                    if (File.Exists(fullPath))
                    {
                        try { bitmap = new Bitmap(fullPath); }
                        catch (Exception ex) { Logger.Warning($"Could not load schematic image [{fullPath}] - [{ex.Message}]"); }
                    }

                    result.Add((schematic.SchematicName, fullPath, bitmap));
                }

                return result;
            });

            // Pre-scale to thumbnail size on UI thread, then release full-resolution originals
            var thumbnails = new List<SchematicThumbnail>();

            foreach (var (name, fullPath, fullBitmap) in loaded)
            {
                IImage? thumbnailImage = null;

                if (fullBitmap != null)
                {
                    thumbnailImage = CreateScaledThumbnail(fullBitmap, ThumbnailMaxWidth);
                    fullBitmap.Dispose();
                }

                thumbnails.Add(new SchematicThumbnail
                {
                    Name = name,
                    ImageFilePath = fullPath,
                    ImageSource = thumbnailImage
                });
            }

            this._currentThumbnails = thumbnails;
            this.SchematicsThumbnailList.ItemsSource = thumbnails;

            if (thumbnails.Count > 0)
                this.SchematicsThumbnailList.SelectedIndex = 0;
        }

        // ###########################################################################################
        // Loads the full-resolution image for the selected thumbnail on a background thread and
        // displays it in the main viewer. Cancels any in-flight load from a previous selection,
        // ensuring rapid switching never shows a stale image or leaks a bitmap.
        // ###########################################################################################
        private async void OnSchematicsThumbnailSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            this._fullResLoadCts?.Cancel();
            this._fullResLoadCts = new CancellationTokenSource();
            var cts = this._fullResLoadCts;

            var selected = this.SchematicsThumbnailList.SelectedItem as SchematicThumbnail;

            this.SchematicsImage.Source = null;
            this._schematicsMatrix = Matrix.Identity;
            ((MatrixTransform)this.SchematicsImage.RenderTransform!).Matrix = this._schematicsMatrix;

            if (selected == null || string.IsNullOrEmpty(selected.ImageFilePath))
                return;

            var bitmap = await Task.Run(() =>
            {
                if (cts.Token.IsCancellationRequested)
                    return null;

                try { return new Bitmap(selected.ImageFilePath); }
                catch (Exception ex)
                {
                    Logger.Warning($"Could not load full-res schematic [{selected.ImageFilePath}] - [{ex.Message}]");
                    return null;
                }
            }, cts.Token);

            if (cts.Token.IsCancellationRequested)
            {
                bitmap?.Dispose();
                return;
            }

            this._currentFullResBitmap?.Dispose();
            this._currentFullResBitmap = bitmap;
            this.SchematicsImage.Source = bitmap;
        }

        // ###########################################################################################
        // Clears the main schematics image, cancels any pending full-res load, disposes the current
        // full-res bitmap, and resets the zoom transform to the identity state.
        // ###########################################################################################
        private void ResetSchematicsViewer()
        {
            this._fullResLoadCts?.Cancel();
            this._fullResLoadCts = null;
            this._currentFullResBitmap?.Dispose();
            this._currentFullResBitmap = null;
            this.SchematicsImage.Source = null;
            this._schematicsMatrix = Matrix.Identity;
            ((MatrixTransform)this.SchematicsImage.RenderTransform!).Matrix = this._schematicsMatrix;
            this._isPanning = false;
            this.SchematicsContainer.Cursor = Cursor.Default;
        }

        // ###########################################################################################
        // Returns the rectangle (in the image control's local coordinate space) that the actual
        // bitmap content occupies, accounting for Stretch="Uniform" letterboxing on either axis.
        // ###########################################################################################
        private Rect GetImageContentRect()
        {
            var containerSize = this.SchematicsContainer.Bounds.Size;
            var bitmap = this._currentFullResBitmap;

            if (bitmap == null || containerSize.Width <= 0 || containerSize.Height <= 0)
                return new Rect(containerSize);

            double containerAspect = containerSize.Width / containerSize.Height;
            double bitmapAspect = (double)bitmap.PixelSize.Width / bitmap.PixelSize.Height;

            double contentX, contentY, contentWidth, contentHeight;

            if (bitmapAspect > containerAspect)
            {
                // Letterbox top and bottom
                contentWidth = containerSize.Width;
                contentHeight = containerSize.Width / bitmapAspect;
                contentX = 0;
                contentY = (containerSize.Height - contentHeight) / 2.0;
            }
            else
            {
                // Letterbox left and right
                contentHeight = containerSize.Height;
                contentWidth = containerSize.Height * bitmapAspect;
                contentX = (containerSize.Width - contentWidth) / 2.0;
                contentY = 0;
            }

            return new Rect(contentX, contentY, contentWidth, contentHeight);
        }

        // ###########################################################################################
        // Clamps the current schematics matrix so no empty space is visible inside the container.
        // If the scaled content is smaller than the container on an axis it is centered on that axis.
        // Always writes the corrected matrix back to the RenderTransform.
        // ###########################################################################################
        private void ClampSchematicsMatrix()
        {
            var containerSize = this.SchematicsContainer.Bounds.Size;
            if (containerSize.Width <= 0 || containerSize.Height <= 0)
                return;

            var contentRect = this.GetImageContentRect();
            double scale = this._schematicsMatrix.M11;
            double tx = this._schematicsMatrix.M31;
            double ty = this._schematicsMatrix.M32;

            double scaledWidth = scale * contentRect.Width;
            double scaledHeight = scale * contentRect.Height;
            double scaledLeft = scale * contentRect.Left + tx;
            double scaledTop = scale * contentRect.Top + ty;
            double scaledRight = scaledLeft + scaledWidth;
            double scaledBottom = scaledTop + scaledHeight;

            // Horizontal - prevent empty space; center if content is narrower than container
            if (scaledWidth >= containerSize.Width)
            {
                if (scaledLeft > 0) tx -= scaledLeft;
                else if (scaledRight < containerSize.Width) tx += containerSize.Width - scaledRight;
            }
            else
            {
                tx = (containerSize.Width - scaledWidth) / 2.0 - scale * contentRect.Left;
            }

            // Vertical - prevent empty space; center if content is shorter than container
            if (scaledHeight >= containerSize.Height)
            {
                if (scaledTop > 0) ty -= scaledTop;
                else if (scaledBottom < containerSize.Height) ty += containerSize.Height - scaledBottom;
            }
            else
            {
                ty = (containerSize.Height - scaledHeight) / 2.0 - scale * contentRect.Top;
            }

            this._schematicsMatrix = new Matrix(scale, 0, 0, scale, tx, ty);
            ((MatrixTransform)this.SchematicsImage.RenderTransform!).Matrix = this._schematicsMatrix;
        }

        // ###########################################################################################
        // Creates a pre-scaled bitmap from a full-resolution source image. Uses a temporary Image
        // control rendered to a RenderTargetBitmap. This trades a one-time scale cost at load time
        // for smooth, zero-cost rendering during layout changes (e.g. splitter drags).
        // ###########################################################################################
        private static RenderTargetBitmap CreateScaledThumbnail(Bitmap source, int maxWidth)
        {
            double scale = Math.Min(1.0, (double)maxWidth / source.PixelSize.Width);
            int tw = Math.Max(1, (int)(source.PixelSize.Width * scale));
            int th = Math.Max(1, (int)(source.PixelSize.Height * scale));

            var imageControl = new Image
            {
                Source = source,
                Stretch = Stretch.Uniform
            };
            imageControl.Measure(new Size(tw, th));
            imageControl.Arrange(new Rect(0, 0, tw, th));

            var rtb = new RenderTargetBitmap(new PixelSize(tw, th), new Vector(96, 96));
            rtb.Render(imageControl);
            return rtb;
        }

        // ###########################################################################################
        // Handles the button click event and updates the status text.
        // ###########################################################################################
        private void OnMyButtonClick(object sender, RoutedEventArgs e)
        {
            this.StatusText.Text = "Button was clicked!";
        }

        // ###########################################################################################
        // Handles mouse wheel zoom on the Schematics image, centered on the cursor position.
        // Builds a zoom matrix by translating the cursor to the origin, scaling, then translating back,
        // keeping the pixel under the cursor stationary throughout the operation.
        // When zooming out past the minimum scale, snaps back to the identity matrix to always
        // restore the full image view regardless of accumulated translation offsets.
        // ###########################################################################################
        private void OnSchematicsZoom(object? sender, PointerWheelEventArgs e)
        {
            var pos = e.GetPosition(this.SchematicsImage);
            double delta = e.Delta.Y > 0 ? SchematicsZoomFactor : 1.0 / SchematicsZoomFactor;

            double newScale = this._schematicsMatrix.M11 * delta;

            if (newScale > SchematicsMaxZoom)
                return;

            if (newScale < SchematicsMinZoom)
            {
                this._schematicsMatrix = Matrix.Identity;
                ((MatrixTransform)this.SchematicsImage.RenderTransform!).Matrix = this._schematicsMatrix;
                e.Handled = true;
                return;
            }

            // Build a zoom matrix centered at the cursor position in image-local space
            var zoomMatrix = Matrix.CreateTranslation(-pos.X, -pos.Y)
                           * Matrix.CreateScale(delta, delta)
                           * Matrix.CreateTranslation(pos.X, pos.Y);

            this._schematicsMatrix = zoomMatrix * this._schematicsMatrix;
            this.ClampSchematicsMatrix();

            e.Handled = true;
        }

        // ###########################################################################################
        // Enters pan mode when the right mouse button is pressed - captures the pointer so movement
        // is tracked even outside the container, and switches the cursor to a hand.
        // ###########################################################################################
        private void OnSchematicsPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this.SchematicsContainer).Properties.IsRightButtonPressed)
                return;

            this._isPanning = true;
            this._panStartPoint = e.GetPosition(this.SchematicsContainer);
            this._panStartMatrix = this._schematicsMatrix;
            this.SchematicsContainer.Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Pointer.Capture(this.SchematicsContainer);
            e.Handled = true;
        }

        // ###########################################################################################
        // Translates the schematics image while the right mouse button is held down.
        // The delta is computed from the capture start point so the image follows the cursor exactly.
        // ###########################################################################################
        private void OnSchematicsPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!this._isPanning)
                return;

            var delta = e.GetPosition(this.SchematicsContainer) - this._panStartPoint;
            this._schematicsMatrix = this._panStartMatrix * Matrix.CreateTranslation(delta.X, delta.Y);
            this.ClampSchematicsMatrix();
            e.Handled = true;
        }

        // ###########################################################################################
        // Exits pan mode when the right mouse button is released - releases pointer capture
        // and restores the default cursor.
        // ###########################################################################################
        private void OnSchematicsPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!this._isPanning)
                return;

            this._isPanning = false;
            this.SchematicsContainer.Cursor = Cursor.Default;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
}