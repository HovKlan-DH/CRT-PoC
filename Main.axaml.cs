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
using System.Threading.Tasks;

namespace VRT
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

        public Main()
        {
            InitializeComponent();
            this.SubscribePanelSizeChanges();
            this.HardwareComboBox.SelectionChanged += this.OnHardwareSelectionChanged;
            this.BoardComboBox.SelectionChanged += this.OnBoardSelectionChanged;
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
        // from the "Board schematics" sheet. Disposes previous bitmap instances before loading new ones.
        // ###########################################################################################
        private async void OnBoardSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            foreach (var thumb in this._currentThumbnails)
                thumb.ImageSource?.Dispose();
            this._currentThumbnails = [];
            this.SchematicsThumbnailList.ItemsSource = null;

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

            var thumbnails = await Task.Run(() =>
            {
                var result = new List<SchematicThumbnail>();

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

                    result.Add(new SchematicThumbnail
                    {
                        Name = schematic.SchematicName,
                        ImageSource = bitmap
                    });
                }

                return result;
            });

            this._currentThumbnails = thumbnails;
            this.SchematicsThumbnailList.ItemsSource = thumbnails;
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
        // ###########################################################################################
        private void OnSchematicsZoom(object? sender, PointerWheelEventArgs e)
        {
            var pos = e.GetPosition(this.SchematicsImage);
            double delta = e.Delta.Y > 0 ? SchematicsZoomFactor : 1.0 / SchematicsZoomFactor;

            double newScale = this._schematicsMatrix.M11 * delta;
            if (newScale < SchematicsMinZoom || newScale > SchematicsMaxZoom)
                return;

            // Build a zoom matrix centered at the cursor position in image-local space
            var zoomMatrix = Matrix.CreateTranslation(-pos.X, -pos.Y)
                           * Matrix.CreateScale(delta, delta)
                           * Matrix.CreateTranslation(pos.X, pos.Y);

            this._schematicsMatrix = zoomMatrix * this._schematicsMatrix;
            ((MatrixTransform)this.SchematicsImage.RenderTransform!).Matrix = this._schematicsMatrix;

            e.Handled = true;
        }
    }
}