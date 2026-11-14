using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyMusicScore;

public partial class MainWindow : Window
{
    private const string Placeholder = "방송 주소를 입력하세요";
    private readonly string _settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyMusicScore");
    private string SettingsPath => Path.Combine(_settingsDirectory, "settings.json");
    private AppSettings _settings = new();
    private bool _placeholderVisible = true;
    private bool _hasPrintableScore = false;

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
            StatusText.Text = "이전 실행의 입력 기록을 불러왔습니다.";
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
            StatusText.Text = "MP3 파일을 선택했습니다. 확인을 눌러 주세요.";
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var value = _placeholderVisible ? string.Empty : SourceComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value) || value == Placeholder)
        {
            StatusText.Text = "방송 주소를 입력하거나 MP3 파일을 선택해 주세요.";
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

        if (File.Exists(value))
        {
            if (!string.Equals(Path.GetExtension(value), ".mp3", StringComparison.OrdinalIgnoreCase))
            {
                NoticeTitleText.Text = "MP3 파일을 선택해 주세요.";
                NoticeReasonText.Text = "현재 버전에서는 MP3 파일의 곡 정보 분석을 지원합니다.";
                StatusText.Text = "지원하지 않는 파일 형식입니다.";
                return;
            }

            _settings.LastFile = value;
            _settings.LastFolder = Path.GetDirectoryName(value) ?? _settings.LastFolder;
            SaveSettings();
            AnalyzeMp3Metadata(value);
            return;
        }

        NoticeTitleText.Text = "방송 분석 기능 준비 중";
        NoticeReasonText.Text = "인터넷 방송의 곡 식별 기능은 아직 연결되지 않았습니다. 이번 버전에서는 컴퓨터에 저장된 MP3 파일의 곡 정보를 먼저 확인합니다.";
        StatusText.Text = "방송 주소를 기억했습니다. 방송 곡 식별 기능은 후속 버전에서 제공됩니다.";
    }

    private async void AnalyzeMp3Metadata(string path)
    {
        string tagTitle = string.Empty;
        string tagArtist = string.Empty;

        try
        {
            using var media = TagLib.File.Create(path);
            tagTitle = media.Tag.Title?.Trim() ?? string.Empty;
            tagArtist = media.Tag.Performers?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
        }
        catch
        {
            // 태그가 없거나 손상되어도 파일 이름과 무료 공개 음악정보를 확인합니다.
        }

        var fileNameInfo = ParseFileName(Path.GetFileNameWithoutExtension(path));
        string title = tagTitle;
        string artist = tagArtist;
        bool confirmedByPublicDatabase = false;

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
        {
            NoticeTitleText.Text = "곡 정보를 확인하고 있습니다.";
            NoticeReasonText.Text = "파일 이름의 앞뒤를 모두 살펴보고, 무료 음악 정보 자료와 대조하고 있습니다.";
            StatusText.Text = "무료 음악 정보 확인 중...";
            var match = await MusicBrainzLookup.FindAsync(fileNameInfo.Candidates);
            if (match.HasValue)
            {
                if (string.IsNullOrWhiteSpace(title)) title = match.Value.Title;
                if (string.IsNullOrWhiteSpace(artist)) artist = match.Value.Artist;
                confirmedByPublicDatabase = true;
            }
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            NoticeTitleText.Text = "곡 제목을 확인하지 못했습니다.";
            NoticeReasonText.Text = "파일 이름에서 제목을 확실하게 구분하지 못했습니다. 잘못된 정보를 정답처럼 표시하지 않았습니다.";
            StatusText.Text = "곡 제목을 확인하지 못했습니다.";
            return;
        }

        SongTitleText.Text = title;
        SongCreditsText.Text = string.IsNullOrWhiteSpace(artist) ? "가수 정보 확인 필요" : artist;

        if (confirmedByPublicDatabase)
        {
            NoticeTitleText.Text = "곡 정보를 무료 자료에서 확인했습니다.";
            NoticeReasonText.Text = "공개 음악 정보 자료에서 제목과 가수의 조합을 확인했습니다. 아직 악보 자체를 찾은 것은 아닙니다.";
            StatusText.Text = $"곡 정보 확인: {artist} - {title}";
        }
        else if (!string.IsNullOrWhiteSpace(tagTitle) || !string.IsNullOrWhiteSpace(tagArtist))
        {
            NoticeTitleText.Text = "MP3에 저장된 곡 정보를 읽었습니다.";
            NoticeReasonText.Text = "파일에 저장된 정보를 표시했습니다. 공개 자료에서 제목과 가수의 조합은 확인하지 못했습니다.";
            StatusText.Text = "MP3 저장 정보 확인 완료";
        }
        else if (fileNameInfo.HasTwoParts)
        {
            NoticeTitleText.Text = "제목과 가수를 확실히 구분하지 못했습니다.";
            NoticeReasonText.Text = "파일 이름의 앞부분과 뒷부분을 모두 확인했지만 무료 자료에서 일치하는 곡을 찾지 못했습니다. 추측한 정보를 정답처럼 표시하지 않았습니다.";
            StatusText.Text = "곡 정보 확인이 필요합니다.";
        }
        else
        {
            NoticeTitleText.Text = "파일 이름에서 곡 제목 후보를 읽었습니다.";
            NoticeReasonText.Text = "가수 정보는 확인되지 않았습니다. 파일 이름만으로 확실하지 않은 정보는 추측하지 않았습니다.";
            StatusText.Text = $"곡 제목 후보: {title}";
        }
    }

    private static (List<(string Title, string Artist)> Candidates, bool HasTwoParts) ParseFileName(string fileName)
    {
        var cleaned = fileName.Trim();
        var separator = Regex.Match(cleaned, @"\\s*[-–—_]\\s*");
        if (!separator.Success)
            return (new List<(string, string)> { (cleaned, string.Empty) }, false);

        var left = cleaned[..separator.Index].Trim();
        var right = cleaned[(separator.Index + separator.Length)..].Trim();
        var candidates = new List<(string Title, string Artist)>();

        void AddCandidate(string possibleTitle, string possibleArtist)
        {
            possibleTitle = possibleTitle.Trim();
            possibleArtist = possibleArtist.Trim();
            if (string.IsNullOrWhiteSpace(possibleTitle) || string.IsNullOrWhiteSpace(possibleArtist)) return;
            if (!candidates.Any(x => string.Equals(x.Title, possibleTitle, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(x.Artist, possibleArtist, StringComparison.OrdinalIgnoreCase)))
                candidates.Add((possibleTitle, possibleArtist));
        }

        // '가수 - 제목'인지 '제목 - 가수'인지 미리 단정하지 않습니다.
        AddCandidate(left, right);
        AddCandidate(right, left);

        // 숫자는 제목의 일부일 수 있으므로 원본을 보존하고, 트랙 번호를 뺀 후보도 별도로 확인합니다.
        var leftWithoutTrackNumber = Regex.Replace(left, @"^\\s*\\d{1,3}[\\s._]+", string.Empty).Trim();
        var rightWithoutTrackNumber = Regex.Replace(right, @"^\\s*\\d{1,3}[\\s._]+", string.Empty).Trim();
        if (!string.Equals(left, leftWithoutTrackNumber, StringComparison.Ordinal))
        {
            AddCandidate(leftWithoutTrackNumber, right);
            AddCandidate(right, leftWithoutTrackNumber);
        }
        if (!string.Equals(right, rightWithoutTrackNumber, StringComparison.Ordinal))
        {
            AddCandidate(left, rightWithoutTrackNumber);
            AddCandidate(rightWithoutTrackNumber, left);
        }

        return (candidates, true);
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasPrintableScore)
        {
            MessageBox.Show(this,
                "인쇄할 악보가 아직 없습니다. 실제 악보가 화면에 표시된 후 인쇄해 주세요.",
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