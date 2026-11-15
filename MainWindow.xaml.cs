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
    private List<ScoreImageCandidate> _pendingArtistCandidates = new();
    private string _pendingSongTitle = string.Empty;

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
            StatusText.Text = "MP3 파일을 선택했습니다. 확인 버튼을 눌러 주세요.";
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

        StatusText.Text = "준비됨";
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
        // 제목만으로 검색하되, 따옴표 유무가 다른 두 검색 결과를 모아 후보 누락을 줄입니다.
        var queries = new[]
        {
            "\"" + title + "\" 악보",
            title + " 악보"
        };

        NoticeTitleText.Text = "곡 정보를 확인하고 악보를 검색하고 있습니다.";
        NoticeReasonText.Text = "곡 제목을 중심으로 여러 검색 결과를 모아 악보 후보를 비교합니다.";
        StatusText.Text = "";
        SearchWebView.Visibility = Visibility.Hidden;
        SearchWebView.IsHitTestVisible = false;
        EmptyScorePanel.Visibility = Visibility.Collapsed;
        ArtistChoicePanel.Visibility = Visibility.Collapsed;
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
                SearchWebView.CoreWebView2.LaunchingExternalUriScheme += CoreWebView2_LaunchingExternalUriScheme;
                _searchBrowserInitialized = true;
            }

            var allCandidates = new List<ScoreImageCandidate>();
            var anySearchResults = false;
            foreach (var query in queries)
            {
                var imageSearchUrl = "https://search.naver.com/search.naver?where=image&sm=tab_jum&query=" +
                                     Uri.EscapeDataString(query);
                await NavigateAndWaitAsync(imageSearchUrl);
                // 네이버 이미지 결과와 지연 로딩 썸네일이 화면에 채워질 시간을 줍니다.
                await Task.Delay(1800);
                await SearchWebView.CoreWebView2.ExecuteScriptAsync(
                    "window.scrollTo(0, document.body.scrollHeight);");
                await Task.Delay(700);
                await SearchWebView.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0, 0);");
                await Task.Delay(350);

                if (!await IsNaverNoResultsAsync()) anySearchResults = true;
                var found = await CollectScoreImageCandidatesAsync(title);
                allCandidates.AddRange(found);
            }

            var candidates = allCandidates
                .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.Score).First())
                .OrderByDescending(x => x.Score)
                .Take(50)
                .ToList();

            if (candidates.Count == 0)
            {
                if (!anySearchResults)
                {
                    ShowNoScoreNotice();
                    return;
                }

                SearchWebView.Visibility = Visibility.Collapsed;
                EmptyScorePanel.Visibility = Visibility.Visible;
                NoticeTitleText.Text = "악보를 찾을 수 없습니다.";
                NoticeReasonText.Text = "검색 결과는 있지만 악보로 판단할 만한 이미지 후보를 찾지 못했습니다.";
                StatusText.Text = "악보 이미지 후보 없음";
                return;
            }

            var artistChoices = candidates.Where(x => !string.IsNullOrWhiteSpace(x.Artist))
                .GroupBy(x => x.Artist.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new ArtistChoice { Artist = g.Key, Candidate = g.OrderByDescending(x => x.Score).First() })
                .OrderByDescending(x => x.Candidate.Score).ToList();

            if (string.IsNullOrWhiteSpace(artist) && artistChoices.Count > 1)
            {
                _pendingArtistCandidates = candidates;
                _pendingSongTitle = title;
                ArtistChoiceList.ItemsSource = artistChoices;
                ArtistChoiceList.SelectedIndex = 0;
                ArtistChoicePanel.Visibility = Visibility.Visible;
                SearchWebView.Visibility = Visibility.Collapsed;
                StatusText.Text = "같은 제목의 곡이 여러 개 있습니다. 아티스트를 선택해 주세요.";
                return;
            }

            var selectedChoice = artistChoices.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(artist) && string.Equals(x.Artist, artist, StringComparison.OrdinalIgnoreCase));
            var best = selectedChoice?.Candidate ?? candidates[0];
            var finalArtist = selectedChoice?.Artist ?? artist;
            var html = BuildSelectedScoreHtml(title, finalArtist, best.Url, candidates.Count);
            SearchWebView.NavigateToString(html);
            SearchWebView.Visibility = Visibility.Visible;
            SearchWebView.IsHitTestVisible = true;
            StatusText.Text = "악보 후보를 찾았습니다.";
            ArtistChoicePanel.Visibility = Visibility.Collapsed;
            SongTitleText.Text = title;
            SongCreditsText.Text = string.IsNullOrWhiteSpace(finalArtist)
                ? "선택된 악보 이미지"
                : finalArtist + " · 선택된 악보 이미지";
        }
        catch (Exception ex)
        {
            SearchWebView.Visibility = Visibility.Collapsed;
            EmptyScorePanel.Visibility = Visibility.Visible;
            NoticeTitleText.Text = "악보 검색 중 문제가 발생했습니다.";
            NoticeReasonText.Text = "네이버 검색 결과를 확인하는 중 문제가 발생했습니다. 잠시 후 다시 검색해 주세요.";
            StatusText.Text = "검색 처리 오류: " + ex.Message;
            SongCreditsText.Text = artist;
        }
    }

    private async Task<bool> IsNaverNoResultsAsync()
    {
        try
        {
            const string script = @"(() => {
              const text = (document.body?.innerText || '').replace(/\s+/g, ' ');
              return /검색결과가 없습니다|검색 결과가 없습니다|검색 결과가 없어요|찾으시는 이미지가 없습니다|결과가 없습니다/.test(text);
            })()";
            var result = await SearchWebView.CoreWebView2.ExecuteScriptAsync(script);
            return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void ShowNoScoreNotice()
    {
        SearchWebView.Visibility = Visibility.Collapsed;
        EmptyScorePanel.Visibility = Visibility.Visible;
        ArtistChoicePanel.Visibility = Visibility.Collapsed;
        NoticeTitleText.Text = "악보를 찾을 수 없습니다.";
        NoticeReasonText.Text = "네이버 이미지 검색 결과가 없습니다.";
        StatusText.Text = "악보 검색 결과 없음";
    }

    private async Task<List<ScoreImageCandidate>> CollectScoreImageCandidatesAsync(string title)
    {
        var titleJson = JsonSerializer.Serialize(title);
        var script = @"(() => {
          const wanted = normalize(" + titleJson + @");
          function normalize(value) {
            return (value || '').normalize('NFKC').toLocaleLowerCase('ko-KR')
              .replace(/[^\p{L}\p{N}]/gu, '');
          }
          const seen = new Set();
          const items = [];
          const images = Array.from(document.images);
          for (const img of images) {
            const srcset = img.getAttribute('srcset') || '';
            const srcsetUrl = srcset.split(',').map(x => x.trim().split(/\s+/)[0]).filter(Boolean).pop() || '';
            const url = img.currentSrc || img.src ||
              img.getAttribute('data-lazy-src') || img.getAttribute('data-src') ||
              img.getAttribute('data-original') || srcsetUrl || '';
            if (!url || !/^https?:/i.test(url) || seen.has(url)) continue;

            let node = img;
            const context = [];
            for (let level = 0; node && level < 6; level++, node = node.parentElement) {
              const t = (node.innerText || '').trim();
              if (t) context.push(t);
              for (const attr of Array.from(node.attributes || [])) {
                if (/^(aria-label|title|alt|data-title|data-name)$/i.test(attr.name) && attr.value)
                  context.push(attr.value);
              }
              if (level >= 2 && t.length > 20) break;
            }
            const text = Array.from(new Set(context)).join(' ').slice(0, 1800);
            const normalizedText = normalize(text);
            const titleMatch = wanted.length > 0 && normalizedText.includes(wanted);
            const sheetMatch = /(악보|악보집|score|sheetmusic|musicsheet|기타코드|코드악보|피아노악보|반주악보)/i.test(text);
            // 제목 정보가 이미지 카드에 누락된 경우도 있으므로 악보 관련 문구가 있는 후보는 보존합니다.
            if (!titleMatch && !sheetMatch) continue;

            const w = img.naturalWidth || img.width || Number(img.getAttribute('width')) || 0;
            const h = img.naturalHeight || img.height || Number(img.getAttribute('height')) || 0;
            if (w < 150 || h < 80) continue;
            if (/(profile|favicon|logo|icon|banner|adimg)/i.test(url)) continue;

            const ratio = w / Math.max(h, 1);
            const area = Math.min(w * h, 4000000);
            let score = Math.min(area / 10000, 100);
            if (titleMatch) score += 240;
            if (sheetMatch) score += 110;
            if (ratio >= 1.15 && ratio <= 2.8) score += 20;
            if (/(광고|배너|프로필|앨범|포스터|로고|상품|쇼핑)/i.test(text)) score -= 120;
            const artist = text.split(/[\r\n]+/).map(v => v.trim())
              .filter(v => v.length >= 2 && v.length <= 40)
              .filter(v => normalize(v) !== wanted)
              .filter(v => !/(악보|이미지|네이버|블로그|카페|검색|광고|공유|저장|보기|다운로드)/i.test(v))[0] || '';
            seen.add(url);
            items.push({ url, text, artist, w, h, score });
          }
          return items.sort((a,b) => b.score - a.score).slice(0, 60);
        })()";
        var json = await SearchWebView.CoreWebView2.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<List<ScoreImageCandidate>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
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
        public string Artist { get; set; } = "";
        public int W { get; set; }
        public int H { get; set; }
        public double Score { get; set; }
    }

    private sealed class ArtistChoice
    {
        public string Artist { get; set; } = string.Empty;
        public ScoreImageCandidate Candidate { get; set; } = new();
    }

    private void ArtistChoiceConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (ArtistChoiceList.SelectedItem is not ArtistChoice selected)
        {
            StatusText.Text = "아티스트를 선택해 주세요.";
            return;
        }

        var candidate = _pendingArtistCandidates
            .Where(x => string.Equals(x.Artist, selected.Artist, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Score).FirstOrDefault();
        if (candidate == null)
        {
            StatusText.Text = "선택한 아티스트의 악보 후보를 찾지 못했습니다.";
            return;
        }

        ArtistChoicePanel.Visibility = Visibility.Collapsed;
        SearchWebView.NavigateToString(BuildSelectedScoreHtml(_pendingSongTitle, selected.Artist, candidate.Url, 1));
        SearchWebView.Visibility = Visibility.Visible;
        SongTitleText.Text = _pendingSongTitle;
        SongCreditsText.Text = selected.Artist + " · 선택된 악보 이미지";
        StatusText.Text = selected.Artist + "의 악보를 표시합니다.";
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
        // 팝업과 새 탭 요청은 모두 취소합니다. 앱 밖의 브라우저 창도 열지 않습니다.
        e.Handled = true;
    }

    private void CoreWebView2_LaunchingExternalUriScheme(object? sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e)
    {
        // mailto:, 외부 앱 실행 등 Windows에 넘기는 외부 프로토콜도 차단합니다.
        e.Cancel = true;
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