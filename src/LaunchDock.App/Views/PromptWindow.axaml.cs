using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LaunchDock.App.Views;

public partial class PromptWindow : Window
{
    public PromptWindow()
    {
        InitializeComponent();
    }

    public PromptWindow(string title, string message, string initialValue) : this()
    {
        Title = title;
        MessageText.Text = message;
        InputBox.Text = initialValue;
        Opened += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public PromptWindow(string title, string message, string initialValue, bool isPassword) : this(title, message, initialValue)
    {
        if (isPassword)
        {
            InputBox.PasswordChar = '●';
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(InputBox.Text);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Close(InputBox.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }
}
