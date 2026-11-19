using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace BipKb.Desktop;

internal record Field(string Name, string Label, string Default = "", string[]? Choices = null, bool Optional = false, string? Picker = null);

internal static class Dialog
{
    public static Dictionary<string,string>? Show(Window owner, string title, params Field[] fields)
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        var window = new Window { Owner = owner, Title = title, Width = 540, SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Min(700, SystemParameters.WorkArea.Height), WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        var values = new Dictionary<string,Func<string>>();
        foreach (var field in fields)
        {
            var label = new Label { Content = field.Label, Padding = new Thickness(0,6,0,3) };
            panel.Children.Add(label);
            if (field.Choices != null)
            {
                var list = new ComboBox { ItemsSource = field.Choices, SelectedIndex = 0 };
                label.Target = list; panel.Children.Add(list);
                values[field.Name] = () => (string)list.SelectedItem;
                continue;
            }
            var row = new DockPanel();
            var input = new TextBox { Text = field.Default, MinWidth = 120 };
            label.Target = input;
            if (field.Picker != null)
            {
                var browse = new Button { Content = "Parcourir…" };
                browse.Click += (_,_) => { if (Pick(window,field.Picker) is {} path) input.Text = path; };
                DockPanel.SetDock(browse,Dock.Right); row.Children.Add(browse);
            }
            row.Children.Add(input); panel.Children.Add(row);
            values[field.Name] = () => input.Text.Trim();
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var submit = new Button { Content = title, IsDefault = true };
        submit.Click += (_,_) =>
        {
            var missing = fields.FirstOrDefault(f => !f.Optional && string.IsNullOrWhiteSpace(values[f.Name]()));
            if (missing != null) { MessageBox.Show(window,missing.Label,"Champ requis"); return; }
            window.DialogResult = true;
        };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true }); buttons.Children.Add(submit); panel.Children.Add(buttons);
        return window.ShowDialog() == true ? values.ToDictionary(pair => pair.Key,pair => pair.Value()) : null;
    }

    public static string? Pick(Window owner,string kind)
    {
        if (kind == "folder") { var d = new OpenFolderDialog(); return d.ShowDialog(owner)==true ? d.FolderName : null; }
        if (kind == "save") { var d = new SaveFileDialog { DefaultExt = ".md", Filter = "Markdown|*.md|Tous les fichiers|*.*" }; return d.ShowDialog(owner)==true ? d.FileName : null; }
        var file = new OpenFileDialog(); return file.ShowDialog(owner)==true ? file.FileName : null;
    }
}
