using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace VRT
{
    public partial class Main : Window
    {
        private Matrix _schematicsMatrix = Matrix.Identity;
        private const double SchematicsZoomFactor = 1.1;
        private const double SchematicsMinZoom = 0.1;
        private const double SchematicsMaxZoom = 20.0;

        public Main()
        {
            InitializeComponent();
            this.SubscribePanelSizeChanges();
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
            var pos = e.GetPosition(this.SchematicsContainer);
            double delta = e.Delta.Y > 0 ? SchematicsZoomFactor : 1.0 / SchematicsZoomFactor;

            double newScale = this._schematicsMatrix.M11 * delta;
            if (newScale < SchematicsMinZoom || newScale > SchematicsMaxZoom)
                return;

            // Build a zoom matrix centered at the cursor position in container space
            var zoomMatrix = Matrix.CreateTranslation(-pos.X, -pos.Y)
                           * Matrix.CreateScale(delta, delta)
                           * Matrix.CreateTranslation(pos.X, pos.Y);

            this._schematicsMatrix = this._schematicsMatrix * zoomMatrix;
            ((MatrixTransform)this.SchematicsImage.RenderTransform!).Matrix = this._schematicsMatrix;

            e.Handled = true;
        }
    }
}