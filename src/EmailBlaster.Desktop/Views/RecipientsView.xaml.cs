using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EmailBlaster.Core.Import;
using EmailBlaster.Core.Models;
using Microsoft.Win32;

namespace EmailBlaster.Desktop.Views;

public partial class RecipientsView : UserControl, IRefreshable
{
    private readonly SessionState _session;

    public RecipientsView(SessionState session)
    {
        _session = session;
        InitializeComponent();
        Render();
    }

    public void OnShown() => Render();

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var extensions = string.Join(";", RecipientImporterFactory.SupportedExtensions.Select(x => "*" + x));
        var dialog = new OpenFileDialog
        {
            Title = "Import recipients",
            Filter = $"Recipient files ({extensions})|{extensions}|CSV files (*.csv)|*.csv|"
                     + "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var list = RecipientImporterFactory.ImportFile(dialog.FileName);
            _session.Recipients = list;
            Render();

            if (list.Count == 0)
                MessageBox.Show("No recipients with an email address were found in that file.",
                    "Nothing imported", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _session.Recipients = RecipientList.Empty;
        Render();
    }

    private void Render()
    {
        var list = _session.Recipients;

        // Summary line.
        if (list.Count == 0)
        {
            SummaryText.Text = "No recipients loaded yet.";
            EmptyState.Visibility = Visibility.Visible;
        }
        else
        {
            var skipped = list.SkippedRows > 0 ? $"  •  {list.SkippedRows} row(s) skipped (no email)" : "";
            SummaryText.Text = $"{list.Count:N0} recipient(s) loaded{skipped}";
            EmptyState.Visibility = Visibility.Collapsed;
        }

        // Placeholder chips.
        ColumnChips.Children.Clear();
        foreach (var column in list.Columns)
        {
            ColumnChips.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(13),
                Background = (System.Windows.Media.Brush)FindResource("AppBackgroundBrush"),
                Padding = new Thickness(11, 4, 11, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Child = new TextBlock
                {
                    Text = "{{" + column + "}}",
                    FontSize = 12,
                    FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, monospace"),
                    Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush")
                }
            });
        }

        BuildGrid(list);
    }

    private void BuildGrid(RecipientList list)
    {
        RecipientsGrid.Columns.Clear();

        foreach (var column in list.Columns)
        {
            RecipientsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = column,
                Binding = new Binding($"[{column}]"),
                Width = column is "Email"
                    ? new DataGridLength(2, DataGridLengthUnitType.Star)
                    : new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        // Each row is a case-insensitive dictionary so the [key] bindings always resolve.
        var rows = list.Recipients.Select(r =>
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in list.Columns)
                dict[column] = r.ToMergeFields().TryGetValue(column, out var v) ? v : string.Empty;
            return dict;
        }).ToList();

        RecipientsGrid.ItemsSource = rows;
    }
}
