using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyMusicScore;

internal sealed class ArtistSelectionWindow : Window
{
    private readonly ListBox _choices;
    private readonly TextBox _manualEntry;
    public string SelectedArtist { get; private set; } = string.Empty;

    public ArtistSelectionWindow(string title, System.Collections.Generic.IReadOnlyList<ArtistCandidate> candidates)
    {
        Title = "가수 선택";
        Width = 520;
        Height = 510;
        MinWidth = 440;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = Brushes.White;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(22) };
        Content = root;

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "취소", Width = 90, Height = 36, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => { DialogResult = false; };
        var choose = new Button { Content = "선택", Width = 100, Height = 36, IsDefault = true };
        choose.Click += (_, _) => ConfirmSelection();
        footer.Children.Add(cancel);
        footer.Children.Add(choose);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var content = new StackPanel();
        root.Children.Add(content);
        content.Children.Add(new TextBlock
        {
            Text = $"'{title}' 제목으로 검색된 가수",
            FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(32, 41, 56)),
            Margin = new Thickness(0, 0, 0, 8)
        });
        content.Children.Add(new TextBlock
        {
            Text = "제목이 같아도 다른 노래일 수 있습니다. 맞는 가수를 선택해 주세요.",
            TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(105, 118, 139)),
            Margin = new Thickness(0, 0, 0, 14)
        });

        _choices = new ListBox { Height = 270, BorderBrush = new SolidColorBrush(Color.FromRgb(220, 226, 235)) };
        foreach (var candidate in candidates)
        {
            var detail = string.Join(" · ", new[] { candidate.Album, candidate.Year }.Where(x => !string.IsNullOrWhiteSpace(x)));
            _choices.Items.Add(new ListBoxItem
            {
                Content = string.IsNullOrWhiteSpace(detail) ? candidate.Artist : $"{candidate.Artist}    ({detail})",
                Tag = candidate.Artist,
                Padding = new Thickness(9, 8, 9, 8),
                FontSize = 14
            });
        }
        if (_choices.Items.Count > 0) _choices.SelectedIndex = 0;
        content.Children.Add(_choices);

        content.Children.Add(new TextBlock
        {
            Text = "목록에 없으면 가수 이름을 직접 입력할 수 있습니다.",
            Foreground = new SolidColorBrush(Color.FromRgb(105, 118, 139)),
            Margin = new Thickness(0, 16, 0, 6)
        });
        _manualEntry = new TextBox { Height = 34, Padding = new Thickness(8, 5, 8, 5), FontSize = 14 };
        content.Children.Add(_manualEntry);
    }

    private void ConfirmSelection()
    {
        SelectedArtist = !string.IsNullOrWhiteSpace(_manualEntry.Text)
            ? _manualEntry.Text.Trim()
            : (_choices.SelectedItem as ListBoxItem)?.Tag as string ?? string.Empty;

        if (string.IsNullOrWhiteSpace(SelectedArtist))
        {
            MessageBox.Show(this, "가수를 선택하거나 직접 입력해 주세요.", "Easy Music Score",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}