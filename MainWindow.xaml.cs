using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
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
            StatusText.Text = "곡 제목·가수 이름을 입력하거나 MP3 파일 또는 방송 주소를 입력해 주세요.";
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

        StatusText.Text = "입력 내용을 확인하고 있습니다.";
        var resolved = await SongInputResolver.ResolveAsync(value);

        if (resolved.IsAiSong)
        {
            SearchWebView.Visibility = Visibility.Collapsed;
            EmptyScorePanel.Visibility = Visibility.Visible;
            NoticeTitleText.Text = "AI 곡입니다.";
            NoticeReasonText.Text = "AI 생성곡으로 판단되어 악보 검색을 진행하지 않습니다.";
            SongTitleText.Text = string.Empty;
            SongCreditsText.Text = string.Empty;
            StatusText.Text = "AI 곡 확인";
            return;
        }

        if (string.IsNullOrWhiteSpace(resolved.Title))
        {
            SearchWebView.Visibility = Visibility.Collapsed;
            EmptyScorePanel.Visibility = Visibility.Visible;
            NoticeTitleText.Text = "곡 정보를 확인하지 못했습니다.";
            NoticeReasonText.Text = resolved.Message;
            StatusText.Text = "곡 제목 확인 실패";
            return;
        }

        if (!string.IsNullOrWhiteSpace(resolved.Message))
            StatusText.Text = resolved.Message;

        await SearchNaverImagesAsync(resolved.Title, resolved.Artist);
    }

    private async Task SearchNaverImagesAsync(string title, string artist)
    {
        var query = string.Join(" ", new[] { "\"" + title + "\"", artist }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        var firstSearchText = query;
        var imageSearchText = query + " 악보";
        var firstSearchUrl = "https://search.naver.com/search.naver?query=" + Uri.EscapeDataString(firstSearchText);
        var imageSearchUrl = "https://search.naver.com/search.naver?where=image&sm=tab_jum&query=" +
                             Uri.EscapeDataString(imageSearchText);

        NoticeTitleText.Text = "곡 정보를 확인하고 악보를 검색하고 있습니다.";
        NoticeReasonText.Text = "네이버에서 곡 정보를 먼저 확인한 다음, 확인한 제목과 가수명에 '악보'를 더해 이미지 검색을 진행합니다.";
        StatusText.Text = "네이버 1차 곡 정보 검색 중...";
        SearchWebView.Visibility = Visibility.Visible;
        EmptyScorePanel.Visibility = Visibility.Collapsed;
        SongTitleText.Text = title;
        SongCreditsText.Text = string.IsNullOrWhiteSpace(artist) ? "곡 정보 검색" : artist;

        try
        {
            if (!_searchBrowserInitialized)
            {
                await SearchWebView.EnsureCoreWebView2Async();
                SearchWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                SearchWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                SearchWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                SearchWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                _searchBrowserInitialized = true;
            }

            await NavigateAndWaitAsync(firstSearchUrl);
            // 첫 검색으로 곡 정보 확인 단계를 거친 뒤, 같은 곡명·가수명으로 악보 이미지 검색을 실행합니다.
            await NavigateAndWaitAsync(imageSearchUrl);
            StatusText.Text = "네이버 이미지 결과에서 악보 후보를 분석하고 있습니다.";
            var candidates = await CollectScoreImageCandidatesAsync(title);
            if (candidates.Count == 0)
                throw new InvalidOperationException("이미지 검색 결과에서 표시 가능한 악보 후보를 찾지 못했습니다.");

            var best = candidates[0];
            var html = BuildSelectedScoreHtml(title, artist, best.Url, candidates.Count);
            SearchWebView.NavigateToString(html);
            StatusText.Text = $"악보 후보 {candidates.Count}개를 확인했습니다. 가장 적합한 이미지 1개를 표시합니다.";
            SongTitleText.Text = title;
            SongCreditsText.Text = string.IsNullOrWhiteSpace(artist)
                ? "선택된 악보 이미지"
                : artist + " · 선택된 악보 이미지";
        }
        catch (Exception ex)
        {
            // 검색 실패 시 외부 브라우저를 띄우지 않습니다.
            // 앱 내부 검색 화면을 유지하고 실제 오류를 상태 표시줄에 남겨 원인을 확인합니다.
            SearchWebView.Visibility = Visibility.Visible;
            EmptyScorePanel.Visibility = Visibility.Collapsed;
            SongCreditsText.Text = string.IsNullOrWhiteSpace(artist)
                ? "앱 내부 검색 처리 중 오류"
                : artist + " · 앱 내부 검색 처리 중 오류";
            StatusText.Text = "앱 내부 검색 처리 오류: " + ex.Message;
        }
    }

    private async Task<List<ScoreImageCandidate>> CollectScoreImageCandidatesAsync(string title)
    {
        var titleJson = JsonSerializer.Serialize(title);
        var script = $@"(() => {{
          const wanted = normalize({titleJson});
          function normalize(value) {{
            return (value || '').normalize('NFKC').toLocaleLowerCase('ko-KR')
              .replace(/[^\p{{L}}\p{{N}}]/gu, '');
          }}
          const seen = new Set();
          const items = [];
          const imgs = Array.from(document.images);
          for (const img of imgs) {{
            const url = img.currentSrc || img.src || img.getAttribute('data-src') || '';
            if (!url || !/^https?:/i.test(url) || seen.has(url)) continue;
            const anchorText = img.closest('a')?.innerText || '';
            const labels = [img.alt || '', img.title || '', anchorText]
              .flatMap(value => value.split(/[\r\n]+/))
              .map(value => value.trim()).filter(Boolean);
            // 제목이 결과 제목/설명 한 줄과 완전히 일치하는 경우만 통과시킵니다.
            if (!labels.some(label => normalize(label) === wanted)) continue;
            seen.add(url);
            const w = img.naturalWidth || img.width || 0;
            const h = img.naturalHeight || img.height || 0;
            if (w < 180 || h < 100) continue;
            const text = labels.join(' ').slice(0,500);
            const area = Math.min(w*h, 3000000);
            let score = Math.min(area / 12000, 100);
            if (/(악보|score|sheet music|music sheet|기타 코드|코드 악보|가사)/i.test(text)) score += 100;
            if (/(광고|배너|프로필|앨범|포스터|로고)/i.test(text)) score -= 80;
            items.push({{url, text, w, h, score}});
          }}
          return items.sort((a,b)=>b.score-a.score).slice(0,50);
        }})()";
        var json = await SearchWebView.CoreWebView2.ExecuteScriptAsync(script);
        var decoded = JsonSerializer.Deserialize<string>(json) ?? "[]";
        return JsonSerializer.Deserialize<List<ScoreImageCandidate>>(decoded, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }

    private static string BuildSelectedScoreHtml(string title, string artist, string imageUrl, int count)
    {
        var safeTitle = System.Net.WebUtility.HtmlEncode(title);
        var safeArtist = System.Net.WebUtility.HtmlEncode(artist);
        var safeUrl = System.Net.WebUtility.HtmlEncode(imageUrl);
        return $@"<!doctype html><html lang='ko'><head><meta charset='utf-8'>
          <meta name='viewport' content='width=device-width,initial-scale=1'>
          <style>
          *{{box-sizing:border-box}} body{{margin:0;background:#fff;color:#202938;font-family:'Malgun Gothic',sans-serif}}
          header{{padding:16px 20px;text-align:center;border-bottom:1px solid #e5e9f0}}
          h2{{font-size:19px;margin:0 0 6px}} p{{font-size:12px;color:#778397;margin:0}}
          main{{height:calc(100vh - 86px);display:flex;align-items:center;justify-content:center;padding:12px;overflow:auto}}
          img{{max-width:100%;max-height:100%;object-fit:contain}} .note{{padding:20px;color:#778397}}
          </style></head><body><header><h2>{safeTitle}</h2><p>{safeArtist} · 후보 {count}개 중 우선순위가 가장 높은 악보 이미지</p></header>
          <main><img src='{safeUrl}' alt='선택된 악보 이미지' onerror=""this.style.display='none';document.getElementById('err').style.display='block'""/>
          <div id='err' class='note' style='display:none'>선택한 이미지에 접근할 수 없습니다. 다음 단계에서 후보 대체 선택을 추가합니다.</div></main></body></html>";
    }

    private sealed class ScoreImageCandidate
    {
        public string Url { get; set; } = "";
        public string Text { get; set; } = "";
        public int W { get; set; }
        public int H { get; set; }
        public double Score { get; set; }
    }

    private async Task NavigateAndWaitAsync(string url)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = (_, args) =>
        {
            SearchWebView.CoreWebView2.NavigationCompleted -= handler;
            completion.TrySetResult(args.IsSuccess);
        };
        SearchWebView.CoreWebView2.NavigationCompleted += handler;
        SearchWebView.CoreWebView2.Navigate(url);

        var completed = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(25)));
        if (completed != completion.Task)
        {
            SearchWebView.CoreWebView2.NavigationCompleted -= handler;
            throw new TimeoutException("네이버 검색 페이지 응답 시간이 초과되었습니다.");
        }
        if (!await completion.Task)
            throw new InvalidOperationException("네이버 검색 페이지를 불러오지 못했습니다.");
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // 새 창을 Windows 기본 브라우저로 보내지 않고 현재 앱의 WebView에 연결합니다.
        // Navigate()를 별도로 호출하는 대신 WebView2가 새 창 탐색을 직접 현재 화면에서 처리하게 합니다.
        e.NewWindow = SearchWebView.CoreWebView2;
        e.Handled = true;
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