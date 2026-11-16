using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LaunchDock.App.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string title, string message, string confirmText) : this()
    {
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        Opened += (_, _) => ConfirmButton.Focus();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
