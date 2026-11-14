using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyMusicScore;

public partial class MainWindow : Window
{
    private const string Placeholder = "곡 제목·가수 이름을 입력하거나 MP3 파일을 선택하세요";
    private readonly string _settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyMusicScore");
    private string SettingsPath => Path.Combine(_settingsDirectory, "settings.json");
    private AppSettings _settings = new();
    private bool _placeholderVisible = true;
    private bool _hasPrintableScore = false;
    private bool _searchBrowserInitialized = false;

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SourceComboBox.Items.Clear();
        foreach (var item in _settings.InputHistory.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            SourceComboBox.Items.Add(item);

        if (!string.IsNullOrWhiteSpace(_settings.LastInput))
        {
            SourceComboBox.Text = _settings.LastInput;
            SourceComboBox.Foreground = new SolidColorBrush(Color.FromRgb(32, 41, 56));
            _placeholderVisible = false;
            StatusText.Text = "이전 검색어나 파일 경로를 불러왔습니다.";
        }
        else ShowPlaceholder();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { _settings = new AppSettings(); }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { StatusText.Text = "설정 저장에 실패했습니다."; }
    }

    private void ShowPlaceholder()
    {
        SourceComboBox.Text = Placeholder;
        SourceComboBox.Foreground = new SolidColorBrush(Color.FromRgb(137, 148, 165));
        _placeholderVisible = true;
    }

    private void SourceComboBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_placeholderVisible)
        {
            SourceComboBox.Text = string.Empty;
            SourceComboBox.Foreground = new SolidColorBrush(Color.FromRgb(32, 41, 56));
            _placeholderVisible = false;
        }
    }

    private void SourceComboBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceComboBox.Text)) ShowPlaceholder();
    }

    private void SourceComboBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Confirm_Click(sender, e);
    }

    private void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceComboBox.SelectedItem is string selected)
        {
            SourceComboBox.Text = selected;
            SourceComboBox.Foreground = new SolidColorBrush(Color.FromRgb(32, 41, 56));
            _placeholderVisible = false;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "MP3 음악 파일 선택",
            Filter = "MP3 음악 파일 (*.mp3)|*.mp3",
            CheckFileExists = true,
            Multiselect = false
        };
        if (Directory.Exists(_settings.LastFolder)) dialog.InitialDirectory = _settings.LastFolder;
        else if (!string.IsNullOrWhiteSpace(_settings.LastFile) && File.Exists(_settings.LastFile))
            dialog.InitialDirectory = Path.GetDirectoryName(_settings.LastFile);

        if (dialog.ShowDialog(this) == true)
        {
            SourceComboBox.Text = dialog.FileName;
            SourceComboBox.Foreground = new SolidColorBrush(Color.FromRgb(32, 41, 56));
            _placeholderVisible = false;
            _settings.LastInput = dialog.FileName;
            _settings.LastFile = dialog.FileName;
            _settings.LastFolder = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            SaveSettings();
            StatusText.Text = "MP3 파일을 선택했습니다. 검색 버튼을 눌러 주세요.";
        }
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        SourceComboBox.ApplyTemplate();
        var inputEditor = SourceComboBox.Template.FindName("PART_EditableTextBox", SourceComboBox) as TextBox;
        var enteredText = inputEditor?.Text ?? SourceComboBox.Text;
        var value = _placeholderVisible ? string.Empty : enteredText.Trim();
        if (string.IsNullOrWhiteSpace(value) || value == Placeholder)
        {
            StatusText.Text = "곡 제목·가수 이름을 입력하거나 MP3 파일을 선택해 주세요.";
            return;
        }

        _settings.LastInput = value;
        _settings.InputHistory.RemoveAll(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        _settings.InputHistory.Insert(0, value);
        if (_settings.InputHistory.Count > 30) _settings.InputHistory = _settings.InputHistory.Take(30).ToList();

        SourceComboBox.Items.Clear();
        foreach (var item in _settings.InputHistory) SourceComboBox.Items.Add(item);
        SourceComboBox.Text = value;
        SaveSettings();

        _hasPrintableScore = false;
        SongTitleText.Text = string.Empty;
        SongCreditsText.Text = string.Empty;

        if (File.Exists(value) && !string.Equals(Path.GetExtension(value), ".mp3", StringComparison.OrdinalIgnoreCase))
        {
            NoticeTitleText.Text = "MP3 파일을 선택해 주세요.";
            NoticeReasonText.Text = "현재 검색 단계에서는 MP3 파일 또는 곡 검색어를 사용할 수 있습니다.";
            StatusText.Text = "지원하지 않는 파일 형식입니다.";
            SearchWebView.Visibility = Visibility.Collapsed;
            EmptyScorePanel.Visibility = Visibility.Visible;
            return;
        }

        if (!File.Exists(value) && Uri.TryCreate(value, UriKind.Absolute, out var enteredUri) &&
            (enteredUri.Scheme == Uri.UriSchemeHttp || enteredUri.Scheme == Uri.UriSchemeHttps))
        {
            NoticeTitleText.Text = "인터넷 방송 분석은 아직 준비 중입니다.";
            NoticeReasonText.Text = "이번 단계에서는 MP3 파일 또는 곡 제목·가수 이름으로 악보 이미지를 검색합니다.";
            StatusText.Text = "방송 주소 입력은 후속 단계에서 연결합니다.";
            SearchWebView.Visibility = Visibility.Collapsed;
            EmptyScorePanel.Visibility = Visibility.Visible;
            return;
        }

        // 파일 선택 시 경로 전체를 검색어로 전달합니다. 직접 입력한 문장도 그대로 사용합니다.
        await SearchNaverImagesAsync(value);
    }

    private async Task SearchNaverImagesAsync(string originalInput)
    {
        var query = originalInput.Trim();
        var searchText = query + " 악보";
        var searchUrl = "https://search.naver.com/search.naver?where=image&sm=tab_jum&query=" +
                        Uri.EscapeDataString(searchText);

        NoticeTitleText.Text = "네이버 이미지 검색 결과를 불러오고 있습니다.";
        NoticeReasonText.Text = "입력한 파일 경로 또는 검색어를 그대로 사용해 악보 이미지를 검색합니다.";
        StatusText.Text = "네이버 이미지 검색 중...";
        SearchWebView.Visibility = Visibility.Visible;
        EmptyScorePanel.Visibility = Visibility.Collapsed;
        SongTitleText.Text = "네이버 이미지 검색 결과";
        SongCreditsText.Text = "검색어: " + query;

        try
        {
            if (!_searchBrowserInitialized)
            {
                await SearchWebView.EnsureCoreWebView2Async();
                SearchWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                SearchWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                SearchWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                SearchWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                SearchWebView.NavigationCompleted += (_, args) =>
                {
                    StatusText.Text = args.IsSuccess
                        ? "네이버 이미지 검색 결과를 표시했습니다. 악보 이미지를 선택해 원본을 확인하세요."
                        : "검색 페이지를 불러오지 못했습니다. 인터넷 연결을 확인해 주세요.";
                };
                _searchBrowserInitialized = true;
            }

            SearchWebView.CoreWebView2.Navigate(searchUrl);
        }
        catch (Exception ex)
        {
            SearchWebView.Visibility = Visibility.Collapsed;
            EmptyScorePanel.Visibility = Visibility.Visible;
            NoticeTitleText.Text = "앱 안에서 검색 화면을 열지 못했습니다.";
            NoticeReasonText.Text = "WebView2 실행 환경이 없거나 초기화에 실패했습니다. 기본 브라우저로 검색 페이지를 엽니다.";
            StatusText.Text = "외부 브라우저에서 네이버 이미지 검색을 엽니다.";

            try
            {
                Process.Start(new ProcessStartInfo(searchUrl) { UseShellExecute = true });
            }
            catch
            {
                StatusText.Text = "검색 화면을 열지 못했습니다: " + ex.Message;
                NoticeReasonText.Text = ex.Message;
            }
        }
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) &&
            (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps))
            SearchWebView.CoreWebView2.Navigate(target.ToString());
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasPrintableScore)
        {
            MessageBox.Show(this,
                "현재는 네이버 이미지 검색 결과를 표시하는 단계입니다. 악보 파일을 직접 가져와 인쇄하는 기능은 다음 단계에서 연결합니다.",
                "Easy Music Score", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StatusText.Text = "악보 전용 인쇄 레이아웃을 준비하고 있습니다.";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (!_placeholderVisible && !string.IsNullOrWhiteSpace(SourceComboBox.Text))
            _settings.LastInput = SourceComboBox.Text.Trim();
        SaveSettings();
        base.OnClosed(e);
    }

    private sealed class AppSettings
    {
        public string LastInput { get; set; } = string.Empty;
        public string LastFile { get; set; } = string.Empty;
        public string LastFolder { get; set; } = string.Empty;
        public List<string> InputHistory { get; set; } = new();
    }
}