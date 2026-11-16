using System.Windows;

namespace LiangWenWidget;

public partial class SettingsDialog : Window
{
    public string ApiKeyResult { get; private set; } = "";
    public string BaseUrlResult { get; private set; } = "https://api.deepseek.com";

    public SettingsDialog(AppConfig cfg)
    {
        InitializeComponent();
        KeyBox.Text = cfg.ApiKey;
        UrlBox.Text = cfg.BaseUrl;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(KeyBox.Text))
        {
            MessageBox.Show(this, "API Key 不能为空。", "提示");
            return;
        }
        ApiKeyResult = KeyBox.Text.Trim();
        BaseUrlResult = string.IsNullOrWhiteSpace(UrlBox.Text) ? "https://api.deepseek.com" : UrlBox.Text.Trim();
        DialogResult = true;
    }
}
