using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Calisto;

/// <summary>Click-through layer that puts a tiny "def" button right after each abbreviation the passive scan found.</summary>
public partial class TagsOverlayWindow : Window
{
    private List<AbbreviationHit> _shown = new();

    public TagsOverlayWindow()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        SourceInitialized += (_, _) => NativeMethods.AddExStyle(this, NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
    }

    /// <summary>Replaces the tags. Returns false when nothing changed (so callers can skip redraws).</summary>
    public bool ShowTags(List<AbbreviationHit> hits)
    {
        if (hits.Count > 0 && !IsVisible) { Show(); if (SameAs(hits)) return true; }
        if (SameAs(hits)) return false;
        _shown = hits;
        TagCanvas.Children.Clear();
        if (hits.Count == 0) { Hide(); return true; }

        var toDip = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var occupied = new List<Rect>();
        foreach (var hit in hits)
        {
            // Like a superscript: a tiny pill whose bottom half touches the top-right corner of the word, so it sits in the
            // gap between lines and does not cover the letters that follow.
            var corner = toDip.Transform(new Point(hit.ScreenRect.Right, hit.ScreenRect.Top));
            double x = corner.X - Left - 3, y = corner.Y - Top - 7;
            var box = new Rect(x, y, 22, 11);
            while (occupied.Any(o => o.IntersectsWith(box))) { box.X += 20; x += 20; }   // two words ending at the same spot
            occupied.Add(box);

            var tag = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x2E, 0xCC, 0x71)),
                CornerRadius = new CornerRadius(5.5),
                Padding = new Thickness(4, 0, 4, 0),
                Height = 11,
                Cursor = Cursors.Hand,
                ToolTip = hit.Abbreviation,
                Tag = hit,
                Child = new TextBlock
                {
                    Text = "def", FontFamily = new FontFamily("Segoe UI"), FontSize = 8, FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1.5, 0, 0)
                }
            };
            tag.MouseEnter += (s, _) => ((Border)s).Background = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
            tag.MouseLeave += (s, _) => ((Border)s).Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x2E, 0xCC, 0x71));
            tag.MouseLeftButtonUp += Tag_Click;
            Canvas.SetLeft(tag, x);
            Canvas.SetTop(tag, y);
            TagCanvas.Children.Add(tag);
        }
        return true;
    }

    public void Clear() => ShowTags(new List<AbbreviationHit>());

    /// <summary>Hides the tags without forgetting them (while Calisto's own menu or card is in front).</summary>
    public void HideTemporarily() { if (IsVisible) Hide(); }

    private bool SameAs(List<AbbreviationHit> hits)
    {
        if (hits.Count != _shown.Count) return false;
        for (int i = 0; i < hits.Count; i++)
            if (hits[i].Abbreviation != _shown[i].Abbreviation || Math.Abs(hits[i].ScreenRect.X - _shown[i].ScreenRect.X) > 1 || Math.Abs(hits[i].ScreenRect.Y - _shown[i].ScreenRect.Y) > 1)
                return false;
        return true;
    }

    private void Tag_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if ((sender as Border)?.Tag is AbbreviationHit hit)
            DefinitionWindow.Show(hit.Abbreviation, hit.Context, hit.ScreenRect);
    }
}
