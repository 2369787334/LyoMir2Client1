using System.Diagnostics;

namespace MirClient.Forms;

internal sealed class EmbeddedBrowserForm : Form
{
    private readonly WebBrowser _browser;
    private readonly ToolStripTextBox _addressBox;
    private string _currentUrl = string.Empty;

    private static EmbeddedBrowserForm? s_instance;

    public static void ShowUrl(IWin32Window? owner, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        EmbeddedBrowserForm instance = s_instance ??= new EmbeddedBrowserForm();
        if (instance.IsDisposed)
        {
            s_instance = new EmbeddedBrowserForm();
            instance = s_instance;
        }

        instance.Navigate(url);

        if (!instance.Visible)
        {
            if (owner != null)
                instance.Show(owner);
            else
                instance.Show();
        }

        if (instance.WindowState == FormWindowState.Minimized)
            instance.WindowState = FormWindowState.Normal;

        instance.Activate();
    }

    private EmbeddedBrowserForm()
    {
        Text = "Browser";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1024;
        Height = 768;

        _addressBox = new ToolStripTextBox
        {
            ReadOnly = true,
            AutoSize = false,
            Width = 700
        };

        _browser = new WebBrowser
        {
            Dock = DockStyle.Fill,
            ScriptErrorsSuppressed = true
        };
        _browser.Navigated += (_, e) =>
        {
            _currentUrl = e.Url?.ToString() ?? _currentUrl;
            _addressBox.Text = _currentUrl;
        };
        _browser.DocumentTitleChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_browser.DocumentTitle))
                Text = _browser.DocumentTitle;
        };

        ToolStripButton btnBack = new("Back");
        btnBack.Click += (_, _) =>
        {
            if (_browser.CanGoBack)
                _browser.GoBack();
        };

        ToolStripButton btnForward = new("Forward");
        btnForward.Click += (_, _) =>
        {
            if (_browser.CanGoForward)
                _browser.GoForward();
        };

        ToolStripButton btnRefresh = new("Refresh");
        btnRefresh.Click += (_, _) => _browser.Refresh();

        ToolStripButton btnExternal = new("External");
        btnExternal.Click += (_, _) => TryOpenExternal(_currentUrl);

        ToolStrip toolStrip = new()
        {
            Dock = DockStyle.Top
        };
        toolStrip.Items.Add(btnBack);
        toolStrip.Items.Add(btnForward);
        toolStrip.Items.Add(btnRefresh);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(_addressBox);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(btnExternal);

        Controls.Add(_browser);
        Controls.Add(toolStrip);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    private void Navigate(string url)
    {
        _currentUrl = url;
        _addressBox.Text = url;
        Text = url;

        try
        {
            _browser.Navigate(url);
        }
        catch
        {
        }
    }

    private static void TryOpenExternal(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
