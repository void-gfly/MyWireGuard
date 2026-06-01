using System.Windows;

namespace MyWireGuard.App.Dialogs;

public partial class UnhandledExceptionDialog : Window
{
    public static readonly DependencyProperty ExceptionTextProperty = DependencyProperty.Register(
        nameof(ExceptionText),
        typeof(string),
        typeof(UnhandledExceptionDialog),
        new PropertyMetadata(string.Empty));

    public UnhandledExceptionDialog(string exceptionText)
    {
        InitializeComponent();
        ExceptionText = exceptionText;
        Loaded += OnLoaded;
    }

    public string ExceptionText
    {
        get => (string)GetValue(ExceptionTextProperty);
        set => SetValue(ExceptionTextProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ExceptionTextBox.Focus();
        ExceptionTextBox.SelectAll();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ExceptionText);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
