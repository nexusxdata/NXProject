using System.Windows;

namespace NXProject.Views
{
    public partial class PasswordDialog : Window
    {
        public string Password { get; private set; } = "";

        /// <param name="confirmMode">true = pede confirmação (ao salvar); false = só uma vez (ao abrir)</param>
        public PasswordDialog(string message, bool confirmMode = false)
        {
            InitializeComponent();
            MessageText.Text = message;
            if (confirmMode)
                ConfirmPanel.Visibility = Visibility.Visible;
            PasswordBox1.Focus();
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            bool hasPass = PasswordBox1.Password.Length >= 6;
            bool match   = ConfirmPanel.Visibility != Visibility.Visible
                           || PasswordBox1.Password == PasswordBox2.Password;

            MismatchText.Visibility = (ConfirmPanel.Visibility == Visibility.Visible &&
                                       PasswordBox2.Password.Length > 0 && !match)
                ? Visibility.Visible : Visibility.Collapsed;

            OkButton.IsEnabled = hasPass && match;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Password     = PasswordBox1.Password;
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
