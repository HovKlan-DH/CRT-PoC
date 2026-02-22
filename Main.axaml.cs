using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VRT
{
    public partial class Main : Window
    {
        public Main()
        {
            InitializeComponent();
            SubscribePanelSizeChanges();
        }

        // ###########################################################################################
        // Subscribes to Bounds property changes on each panel to update the size labels in real-time.
        // ###########################################################################################
        private void SubscribePanelSizeChanges()
        {
            LeftPanel.PropertyChanged += (s, e) =>
            {
                if (e.Property == Visual.BoundsProperty)
                    LeftSizeLabel.Text = $"{this.LeftPanel.Bounds.Width:F0} × {this.LeftPanel.Bounds.Height:F0}";
            };

            RightPanel.PropertyChanged += (s, e) =>
            {
                if (e.Property == Visual.BoundsProperty)
                    RightSizeLabel.Text = $"{this.RightPanel.Bounds.Width:F0} × {this.RightPanel.Bounds.Height:F0}";
            };
        }

        // ###########################################################################################
        // Handles the button click event and updates the status text.
        // ###########################################################################################
        private void OnMyButtonClick(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Button was clicked!";
        }
    }
}