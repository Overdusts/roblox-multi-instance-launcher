using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace RobloxLauncher.UI;

public class LoginDialog : Form
{
    private WebView2 _webView = null!;
    private Label _statusLabel = null!;
    private ModernButton _btnCancel = null!;
    private TitleBar _titleBar = null!;
    private bool _cookieCaptured;

    public string? CapturedCookie { get; private set; }

    public LoginDialog()
    {
        SetupUI();
        _ = InitWebView();
    }

    private void SetupUI()
    {
        FormBorderStyle = FormBorderStyle.None;
        Size = new Size(520, 700);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.BgMain;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        _titleBar = new TitleBar(this) { Title = "Login to Roblox" };
        Controls.Add(_titleBar);

        var mainPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 12),
            BackColor = Theme.BgMain,
        };

        // Status bar at bottom
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0),
        };

        _statusLabel = new Label
        {
            Text = "Loading Roblox login page...",
            Font = Theme.FontSmall,
            ForeColor = Theme.TextMuted,
            Dock = DockStyle.Left,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 0),
        };

        _btnCancel = new ModernButton
        {
            Text = "Cancel",
            Width = 90,
            Height = 36,
            Dock = DockStyle.Right,
            ButtonColor = Theme.Danger,
            HoverColor = Color.FromArgb(255, 85, 88),
            PressColor = Color.FromArgb(180, 50, 52),
        };
        _btnCancel.Click += (s, e) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        bottomPanel.Controls.Add(_statusLabel);
        bottomPanel.Controls.Add(_btnCancel);

        // WebView2
        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Theme.BgDark,
        };

        mainPanel.Controls.Add(_webView);
        mainPanel.Controls.Add(bottomPanel);

        Controls.Add(mainPanel);
    }

    private async Task InitWebView()
    {
        try
        {
            // Use a unique user data folder per session so multiple accounts can login
            string userDataFolder = Path.Combine(
                Path.GetTempPath(),
                "RobloxLauncher_WebView",
                Guid.NewGuid().ToString("N")[..8]);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            // Clear cookies for fresh login
            _webView.CoreWebView2.CookieManager.DeleteAllCookies();

            // Monitor navigation to detect successful login
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _webView.CoreWebView2.DOMContentLoaded += OnDOMContentLoaded;

            // Navigate to Roblox login
            _webView.CoreWebView2.Navigate("https://www.roblox.com/login");
            _statusLabel.Text = "Sign in with your Roblox account";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"WebView2 error: {ex.Message}";
            _statusLabel.ForeColor = Theme.Danger;
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        await CheckForLoginCookie();
    }

    private async void OnDOMContentLoaded(object? sender, CoreWebView2DOMContentLoadedEventArgs e)
    {
        await CheckForLoginCookie();
    }

    private async Task CheckForLoginCookie()
    {
        if (_cookieCaptured) return;

        try
        {
            var url = _webView.CoreWebView2.Source;

            // Update status based on current page
            BeginInvoke(() =>
            {
                if (url.Contains("/login"))
                    _statusLabel.Text = "Sign in with your Roblox account";
                else if (url.Contains("/signup") || url.Contains("/register"))
                    _statusLabel.Text = "Create your account, then you'll be logged in";
                else if (url.Contains("/home") || url.Contains("/discover") || url.Contains("/charts"))
                    _statusLabel.Text = "Login detected! Grabbing cookie...";
                else
                    _statusLabel.Text = "Waiting for login...";
            });

            // Try to get the .ROBLOSECURITY cookie
            var cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");
            var securityCookie = cookies.FirstOrDefault(c =>
                c.Name == ".ROBLOSECURITY" && !string.IsNullOrEmpty(c.Value));

            if (securityCookie != null)
            {
                _cookieCaptured = true;
                CapturedCookie = securityCookie.Value;

                BeginInvoke(() =>
                {
                    _statusLabel.Text = "Cookie captured! Closing...";
                    _statusLabel.ForeColor = Theme.Success;
                });

                // Small delay so user sees the success message
                await Task.Delay(800);

                BeginInvoke(() =>
                {
                    DialogResult = DialogResult.OK;
                    Close();
                });
            }
        }
        catch { }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try
        {
            _webView?.Dispose();
        }
        catch { }
        base.OnFormClosed(e);
    }
}
