using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace VRT
{
    public partial class Main : Window
    {
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
    }
}