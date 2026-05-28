using System.Windows;

namespace MyWireGuard.App.Dialogs;

public partial class TextInputDialog : Window
{
    public TextInputDialog(string title, string prompt, string initialValue)
    {
        InitializeComponent();
        Title = title;
        PromptTextBlock.Text = prompt;
        InputTextBox.Text = initialValue;
        ResultText = initialValue;
        Loaded += OnLoaded;
    }

    public string ResultText { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InputTextBox.Focus();
        InputTextBox.SelectAll();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        ResultText = InputTextBox.Text.Trim();
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}