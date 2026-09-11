using System.Windows;

namespace Maush.App;

public sealed class MessageBoxConfirmationService : IConfirmationService
{
    public bool Confirm(string message, string title) =>
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
