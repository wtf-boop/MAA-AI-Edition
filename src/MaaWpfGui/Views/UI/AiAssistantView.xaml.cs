// Part of MaaAssistantArknights, licensed under AGPL-3.0.
using System.Windows;
using System.Windows.Controls;
using MaaWpfGui.ViewModels.UI;

namespace MaaWpfGui.Views.UI;

public partial class AiAssistantView : System.Windows.Controls.UserControl
{
    public AiAssistantView()
    {
        InitializeComponent();
        Loaded += (_, _) => {
            if (DataContext is AiAssistantViewModel vm && ApiKeyBox.Password != vm.ApiKey)
            {
                ApiKeyBox.Password = vm.ApiKey;
            }
        };
    }

    private void ApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AiAssistantViewModel vm && sender is PasswordBox box)
        {
            vm.ApiKey = box.Password;
        }
    }
}
