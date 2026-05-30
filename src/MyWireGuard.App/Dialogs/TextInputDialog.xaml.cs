using System.Windows;
using System.Windows.Controls;

namespace MyWireGuard.App.Dialogs;

public partial class TextInputDialog : Window
{
    public TextInputDialog(string title, string prompt, string initialValue, bool isMultiline)
    {
        InitializeComponent();
        Title = title;
        HeaderTextBlock.Text = title;
        PromptTextBlock.Text = prompt;
        InputTextBox.Text = initialValue;
        ResultText = initialValue;
        if (isMultiline)
        {
            Height = 420;
            MinHeight = 420;
            SizeToContent = SizeToContent.Manual;
            InputBorder.ClearValue(HeightProperty);
            InputBorder.MinHeight = 220;
            InputTextBox.AcceptsReturn = true;
            InputTextBox.TextWrapping = TextWrapping.Wrap;
            InputTextBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            InputTextBox.VerticalContentAlignment = VerticalAlignment.Top;
            InputTextBox.Padding = new Thickness(10, 8, 10, 8);
        }

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
