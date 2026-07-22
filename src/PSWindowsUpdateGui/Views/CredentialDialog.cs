using System.Management.Automation;
using System.Windows;
using System.Windows.Controls;

namespace PSWindowsUpdateGui.Views;

internal sealed class CredentialDialog : Window
{
    private readonly TextBox _userName = new TextBox();
    private readonly PasswordBox _password = new PasswordBox { Margin = new Thickness(3), Padding = new Thickness(5) };

    public CredentialDialog(string parameterName)
    {
        Title = $"Credential for -{parameterName}";
        Width = 430;
        Height = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "The password is kept only in memory and is never written to logs or settings.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });
        panel.Children.Add(new TextBlock { Text = "User name" });
        panel.Children.Add(_userName);
        panel.Children.Add(new TextBlock { Text = "Password", Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(_password);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var ok = new Button { Content = "Use credential", IsDefault = true };
        ok.Click += (_, __) =>
        {
            if (string.IsNullOrWhiteSpace(_userName.Text) || _password.SecurePassword.Length == 0)
            {
                MessageBox.Show(this, "Enter both a user name and password.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Credential = new PSCredential(_userName.Text.Trim(), _password.SecurePassword.Copy());
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        Content = panel;
    }

    public PSCredential? Credential { get; private set; }
}
