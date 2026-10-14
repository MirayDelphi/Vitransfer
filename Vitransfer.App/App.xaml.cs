using System.Windows;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vitransfer.Core;
using System.Windows.Controls;
namespace Vitransfer.App;
public partial class App : Application {
    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        bool deletePreview = e.Args.Length == 2 && e.Args[0] == "--preview-delete";
        bool itemsPreview = e.Args.Length == 2 && e.Args[0] == "--preview-items";
        bool dlcPreview = e.Args.Length == 2 && e.Args[0] == "--preview-dlc";
        Window window = dlcPreview ? new DlcDialog(new LocalConnection(Environment.CurrentDirectory), new Game("PCSE00001", "Example game", null, true)) : deletePreview ? new DeleteGameDialog(new Game("PCSE00001", "Example game", null, true)) : new MainWindow();
        if (dlcPreview && window is DlcDialog sampleDlc) {
            sampleDlc.Dlcs.ItemsSource = new[] { new DlcEntry("EXTRA001", "Additional costume pack", "addcont/PCSE00001/EXTRA001", "UP0001-PCSE00001_00-EXTRA00100000000", true), new DlcEntry("EXTRA002", "EXTRA002", "reAddcont/PCSE00001/EXTRA002", null, false) };
            sampleDlc.Status.Text = "2 DLC folders found.";
        }
        if (itemsPreview && window is MainWindow sampleWindow) {
            sampleWindow.LeftGames.ItemsSource = new[] { new Game("PCSE00001", "Example game", null, true, 2147483648, true, true, true) };
            sampleWindow.RightGames.ItemsSource = new[] { new Game("PCSG00001", "Another game", null, false, 1048576, HasPatch: true) };
        }
        if (e.Args.Length == 2 && (e.Args[0] == "--preview" || deletePreview || itemsPreview || dlcPreview)) {
            var content = (FrameworkElement)window.Content;
            content.Measure(deletePreview ? new Size(528, double.PositiveInfinity) : new Size(992, 648));
            content.Arrange(new Rect(0, 0, deletePreview ? 528 : 992, deletePreview ? content.DesiredSize.Height : 648));
            content.UpdateLayout();
            if (itemsPreview && window is MainWindow menuWindow) {
                ContextMenu? previous = null;
                foreach (var list in new[] { menuWindow.LeftGames, menuWindow.RightGames }) {
                    var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    item.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    if (item.ContextMenu?.Items.Count != 3 || ReferenceEquals(previous, item.ContextMenu))
                        throw new InvalidOperationException("Game context menu did not initialize independently for each row.");
                    previous = item.ContextMenu;
                }
            }
            double width = deletePreview ? content.ActualWidth : 992, height = deletePreview ? content.ActualHeight : 648;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width + 48), (int)Math.Ceiling(height + 48), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) {
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 18, 32)), null, new Rect(0, 0, width + 48, height + 48));
                drawing.DrawRectangle(new VisualBrush(content), null, new Rect(24, 24, width, height));
            }
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(e.Args[1])) encoder.Save(output);
            Shutdown();
            return;
        }
        window.Show();
    }
}
