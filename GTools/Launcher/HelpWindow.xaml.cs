using System.Windows;

namespace OperationBlacktide.Launcher;

public partial class HelpWindow : Window
{
    public HelpWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        Resources.MergedDictionaries.Add(owner.Resources);
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width); MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width - 24));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height - 24));
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } };
    }

    private void CloseHelp(object sender, RoutedEventArgs e) => Close();
}
