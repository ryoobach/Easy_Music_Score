using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyMusicScore;

public partial class MainWindow : Window
{
    private const string Placeholder = "방송 주소를 입력하세요";
    private bool _placeholderVisible = true;

    public MainWindow()
    {
        InitializeComponent();
        SourceTextBox.Text = Placeholder;
        SourceTextBox.Foreground = new SolidColorBrush(Color.FromRgb(137, 148, 165));
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) { }

    private void SourceTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_placeholderVisible)
        {
            SourceTextBox.Clear();
            SourceTextBox.Foreground = new SolidColorBrush(Color.FromRgb(32, 41, 56));
            _placeholderVisible = false;
        }
    }

    private void SourceTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceTextBox.Text))
        {
            SourceTextBox.Text = Placeholder;
            SourceTextBox.Foreground = new SolidColorBrush(Color.FromRgb(137, 148, 165));
            _placeholderVisible = true;
        }
    }

    private void SourceTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Confirm_Click(sender, e);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "음악 파일 선택",
            Filter = "MP3 음악 파일 (*.mp3)|*.mp3",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            SourceTextBox.Text = dialog.FileName;
            SourceTextBox.Foreground = new SolidColorBrush(Color.FromRgb(32, 41, 56));
            _placeholderVisible = false;
            StatusText.Text = "파일을 선택했습니다. 확인을 눌러 주세요.";
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var value = _placeholderVisible ? string.Empty : SourceTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            StatusText.Text = "방송 주소를 입력하거나 음악 파일을 선택해 주세요.";
            return;
        }
        StatusText.Text = "화면 시안 단계입니다. 음악 재생 및 곡·악보 검색 기능은 다음 개발 단계에서 연결합니다.";
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() == true)
            printDialog.PrintVisual(FindScoreVisual(), "Easy Music Score");
    }

    private Visual FindScoreVisual()
    {
        return ScoreSurface;
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
}
