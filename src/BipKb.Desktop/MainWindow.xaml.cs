using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BipKb.Desktop;

internal record Reference(string Label, string Kind, string Id);
internal record Display(string Text, object? Detail = null, IEnumerable<Reference>? References = null);

public partial class MainWindow : Window
{
    private Workspace? workspace;
    private Workspace Space => workspace ?? throw new InvalidOperationException("Ouvrez un workspace.");
    private readonly Dictionary<int, UIElement[]> pages = [];
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch elapsed = new();
    private CancellationTokenSource? job;
    private volatile string stage = "";
    private string displayedStage = "";
    private string? taskId;
    private Process? web;
    private bool closing;
    private static readonly string recentFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"bip-kb","desktop-recent.json");

    public MainWindow()
    {
        InitializeComponent();
        if (SystemParameters.HighContrast) { Background = SystemColors.WindowBrush; Heading.Foreground = SystemColors.WindowTextBrush; }
        clock.Tick += (_,_) =>
        {
            var current = stage;
            Status.Text = $"{elapsed.Elapsed:hh\\:mm\\:ss}   {current}";
            if(displayedStage!=current){Log(current);displayedStage=current;}
        };
        Nav.SelectedIndex = 0;
        var args = Environment.GetCommandLineArgs();
        if (args.Length == 3 && args[1] == "--workspace") Loaded += async (_,_) => await SelectWorkspace(args[2],false);
    }

    private void Navigate(object sender, SelectionChangedEventArgs e)
    {
        if (Controls == null || Nav.SelectedIndex < 0) return;
        Controls.Children.Clear();
        Heading.Text = ((ListBoxItem)Nav.SelectedItem).Content.ToString();
        if (pages.TryGetValue(Nav.SelectedIndex,out var saved))
        { foreach (var child in saved) Controls.Children.Add(child); return; }
        if (workspace == null)
        {
            Info("Créez un workspace pour votre projet ou ouvrez une base existante.");
            Button("Vérifier Copilot sur ce poste",()=>Execute("Vérification du poste",async ct=>
            {
                await using var temp=CopilotScratch.Create();
                var test=new Workspace(temp.DirectoryPath);test.Init();
                var report=await new CopilotAnalyzer(test).Doctor(false,ct);
                return new Display(Render(report),report);
            }));
            foreach (var path in Recent()) Button(path, () => SelectWorkspace(path,false));
            return;
        }
        switch (Nav.SelectedIndex)
        {
            case 0: ProjectPage(); break;
            case 1: SourcesPage(); break;
            case 2: KnowledgePage(); break;
            case 3: CasesPage(); break;
            case 4: ReviewPage(); break;
            case 5: ToolsPage(); break;
        }
        pages[Nav.SelectedIndex] = Controls.Children.Cast<UIElement>().ToArray();
    }

    private static string[] Recent()
    {
        try { return File.Exists(recentFile) ? Json.Read<string[]>(File.ReadAllText(recentFile)) : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
    private async void OpenWorkspace(object sender,RoutedEventArgs e)
    { if (Dialog.Pick(this,"folder") is {} path) await SelectWorkspace(path,false); }
    private async void CreateWorkspace(object sender,RoutedEventArgs e)
    { if (Dialog.Pick(this,"folder") is {} path) await SelectWorkspace(path,true); }

    private async Task SelectWorkspace(string path,bool create)
    {
        var selected = new Workspace(path);
        await Execute(create ? "Initialisation" : "Ouverture", ct =>
        {
            var drive = new DriveInfo(Path.GetPathRoot(selected.Root)!);
            if (selected.Root.StartsWith(@"\\") || drive.DriveType == DriveType.Network)
                throw new IOException("Choisissez une copie de travail sur un volume local de la session VDI pour SQLite. Les documents sources peuvent résider sur le réseau.");
            if (create) selected.Init(business:true);
            if (!File.Exists(selected.StatePath)) throw new IOException("Ce dossier ne contient pas de workspace. Utilisez Créer pour initialiser un dossier vide.");
            var config = selected.Config;
            Dispatcher.Invoke(() =>
            {
                StopWeb();
                workspace = selected; taskId = null; reviewId = null; pages.Clear(); History.Clear(); Proof.Clear();
                WorkspaceLabel.Text = selected.Root; WorkspaceLabel.ToolTip = selected.Root;
                Nav.SelectedIndex = 0; Navigate(Nav,null!);
            });
            try { Json.Write(recentFile,new[]{selected.Root}.Concat(Recent()).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray()); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log("Liste des espaces récents indisponible."); }
            return Task.FromResult(new Display(create ? "Workspace créé. Décrivez votre projet pour commencer le cadrage." :
                $"Workspace ouvert · {config.Sources.Count} source(s) configurée(s)."));
        });
    }

    private async Task Execute(string name,Func<CancellationToken,Task<Display>> work,bool proof=false)
    {
        if (job != null) return;
        using var active = new CancellationTokenSource(); job = active;
        OperationEntry entry = new(name,DateTimeOffset.Now,"Démarrée");
        string? logFile=null;
        var state="Échec";
        if(workspace!=null && name is not ("Ouverture" or "Initialisation"))
            try {logFile=DesktopSession.Begin(workspace,entry);} catch(Exception e) when(e is IOException or UnauthorizedAccessException) {Log("Historique local indisponible.");}
        Nav.IsEnabled = Controls.IsEnabled = WorkspaceButtons.IsEnabled = false;
        CancelButton.IsEnabled = true; stage = name; elapsed.Restart(); clock.Start(); Status.Text = name; Log(name);
        try
        {
            var (result,detail,refs) = await Task.Run(async () =>
            {
                var value=await work(active.Token);
                var formatted=value.Detail switch { null=>"", string raw=>raw, _=>Json.Serialize(value.Detail) };
                return (value,formatted,value.References?.ToArray());
            });
            if (proof) { Proof.Text = result.Text; OutputTabs.SelectedIndex = 1; }
            else { Answer.Text = result.Text; Links.ItemsSource = refs;
                Links.Visibility = Links.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible; OutputTabs.SelectedIndex = 0; }
            if(!proof)Technical.Text = detail;
            state="Terminée"; Status.Text = $"{name} · terminé en {elapsed.Elapsed.TotalSeconds:N1} s"; Log(Status.Text);
        }
        catch (OperationCanceledException) { state="Interrompue"; Status.Text = "Opération interrompue"; Log(Status.Text); }
        catch (Exception e)
        {
            Status.Text = $"{name} · échec"; Log(Status.Text + " · " + e.Message);
            Answer.Text = e.Message; Links.ItemsSource = null; Proof.Clear();
            Technical.Text = e.ToString(); OutputTabs.SelectedIndex = 0;
        }
        finally
        {
            if(logFile!=null)try{Json.Write(logFile,entry with{State=state,Ended=DateTimeOffset.Now});}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){Log("Historique local indisponible.");}
            clock.Stop(); job = null; Nav.IsEnabled = Controls.IsEnabled = WorkspaceButtons.IsEnabled = true; CancelButton.IsEnabled = false;
            if (closing) Close();
        }
    }

    private void Log(string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => Log(text)); return; }
        if (History.Text.Length > 48000) History.Text = History.Text[^32000..];
        History.AppendText($"{DateTime.Now:T}  {text}\n"); History.ScrollToEnd();
    }
    private void Progress(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        stage = text;
    }
    private void CancelJob(object sender,RoutedEventArgs e) { job?.Cancel(); stage = "Interruption demandée"; CancelButton.IsEnabled = false; }
    private void ClosingWindow(object? sender,CancelEventArgs e)
    {
        if (job != null)
        {
            e.Cancel = true;
            if (closing || MessageBox.Show(this,"Interrompre l’opération puis fermer l’application ?","bip-kb",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
            closing = true; job.Cancel(); return;
        }
        StopWeb();
    }

    private void Info(string text) => Controls.Children.Add(new TextBlock { Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,6) });
    private TextBox Input(string label,string initial="",bool multiline=false)
    {
        var text=new TextBox {Text=initial,AcceptsReturn=multiline,MaxHeight=85,MinHeight=multiline?45:28};
        Controls.Children.Add(new Label { Content=label, Target=text, Padding=new Thickness(0,2,0,2) }); Controls.Children.Add(text); return text;
    }
    private WrapPanel Row() { var row=new WrapPanel(); Controls.Children.Add(row); return row; }
    private void Button(string title,Func<Task> action,Panel? row=null)
    {
        var button=new Button {Content=title};
        button.Click+=async (_,_)=>{try { await action(); } catch(Exception e) { MessageBox.Show(this,e.Message,title); }};
        (row??Controls).Children.Add(button);
    }
    private Task Run(string title,Func<CancellationToken,Display> action) => Execute(title,ct=>Task.FromResult(action(ct)));
    private bool Confirm(string message) => MessageBox.Show(this,message,"bip-kb",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    private static string Required(TextBox text) => !string.IsNullOrWhiteSpace(text.Text) ? text.Text.Trim() : throw new ArgumentException("Saisissez une valeur.");
    private Task OpenProof(string id) => Execute("Lecture des preuves",ct =>
    {
        IndexedPage<JsonElement> page;
        try { page = (IndexedPage<JsonElement>)new McpQueries(Space).Call("evidence",id,null,25,0,ct); }
        catch(McpIndexUnavailableException)
        {
            // Research publishes a new dossier after the last indexed search. Resolve the citation
            // against the published version while the index still describes the previous revision.
            Progress("Lecture de la preuve dans la mémoire publiée");
            var k=Space.Load();
            var ids=new HashSet<string>(StringComparer.Ordinal){id};
            var related=k.Claims.FirstOrDefault(c=>c.Id==id)?.EvidenceIds
                ??k.Objects.FirstOrDefault(o=>o.Id==id)?.EvidenceIds??k.Questions.FirstOrDefault(q=>q.Id==id)?.EvidenceIds??[];
            ids.UnionWith(related);
            var matched=k.Evidence.Where(e=>ids.Contains(e.Id)).Take(25).Select(e=>JsonSerializer.SerializeToElement(e,Json.Options)).ToArray();
            page=new(matched,matched.Length,0,null);
        }
        var proofs = page.Items.Select(item=>item.Deserialize<Evidence>(Json.Options)!);
        return Task.FromResult(new Display(page.Total==0?"Aucune preuve indexée pour cette référence.":
            string.Join("\n\n",proofs.Select(p=>$"{p.SourceId} / {p.RelativePath} · {p.Locator}\n{p.Excerpt}\nVersion {p.Version}")),page));
    },true);
    private async void Follow(object sender,MouseButtonEventArgs e) => await OpenSelected();
    private async void FollowKey(object sender,KeyEventArgs e) { if(e.Key==Key.Enter){e.Handled=true;await OpenSelected();} }
    private async Task OpenSelected()
    {
        if (Links.SelectedItem is not Reference r || job!=null) return;
        if (r.Kind=="task") await OpenCase(r.Id);
        else if (r.Kind=="review") await ReviewItem(r.Id);
        else if (r.Kind=="question") await AnswerQuestion(r.Id);
        else if (r.Kind=="source") await SourceDetail(r.Id);
        else await OpenProof(r.Id);
    }
}
