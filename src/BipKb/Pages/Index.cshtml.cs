using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace BipKb.Pages;
public class IndexModel(Workspace workspace):PageModel
{
    public Knowledge Knowledge {get;private set;}=new();
    public List<Claim> Claims {get;private set;}=[];
    public SearchHit[] Hits {get;private set;}=[];
    public List<Decision> Decisions {get;private set;}=[];
    public string View {get;private set;}="search";
    public string Query {get;private set;}="";
    public string? Project {get;private set;}
    public string? Error {get;private set;}
    public WorkTask? SelectedTask {get;private set;}
    public bool Stale {get;private set;}
    public void OnGet(string? view,string? q,string? id,string? project=null)
    {
        Knowledge=workspace.Load();Decisions=workspace.Decisions();View=view??"search";Query=q??"";Project=project;
        try
        {
            if(View=="task"&&id!=null){var research=new Research(workspace,new CopilotAnalyzer(workspace));SelectedTask=research.Get(id);Stale=research.IsStale(SelectedTask);return;}
            if(View=="search"&&!string.IsNullOrWhiteSpace(Query)){Hits=new SearchEngine(workspace).Search(new(Query,project)).Hits;return;}
            Claims=View switch{"review"=>workspace.Review(Knowledge).ToList(),"detail"=>Knowledge.Claims.Where(c=>c.Id==id||c.EntityId==id).ToList(),_=>Knowledge.Claims.Where(c=>!c.Obsolete).Take(30).ToList()};
            if(View=="detail"&&Claims.Count==0&&id!=null)Hits=new SearchEngine(workspace).Search(new(id,project,IncludeHistorical:true)).Hits;
        }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException){Error=e.Message;}
    }
    public IActionResult OnPost(string claimId,string action,string reason,string actor)
    {
        try{workspace.Decide(claimId,action,reason,actor);return RedirectToPage(new{view="review"});}
        catch(Exception e) when(e is ArgumentException or InvalidOperationException){OnGet("review",null,null);Error=e.Message;return Page();}
    }
    public async Task<IActionResult> OnPostTaskAsync(string? id,string? goal,string? project,string? message)
    {
        try
        {
            var research=new Research(workspace,new CopilotAnalyzer(workspace));
            if(id==null){var task=research.Start(goal??"",project);return RedirectToPage(new{view="task",id=task.Id});}
            await research.Run(id,message,HttpContext.RequestAborted);return RedirectToPage(new{view="task",id});
        }
        catch(Exception e) when(e is not OperationCanceledException){OnGet(id==null?"tasks":"task",null,id,project);Error=e.Message;return Page();}
    }
    public IActionResult OnPostRemember(string project,string declaration,string actor,bool confirm)
    {
        try{new Research(workspace,new CopilotAnalyzer(workspace)).Remember(project,declaration,actor,confirm);return RedirectToPage(new{view="questions"});}
        catch(Exception e) when(e is ArgumentException or InvalidOperationException){OnGet("questions",null,null);Error=e.Message;return Page();}
    }
    public IActionResult OnPostAnswer(string id,string answer,string actor,bool confirm)
    {
        try{new Research(workspace,new CopilotAnalyzer(workspace)).AnswerQuestion(id,answer,actor,confirm);return RedirectToPage(new{view="questions"});}
        catch(Exception e) when(e is ArgumentException or InvalidOperationException){OnGet("questions",null,null);Error=e.Message;return Page();}
    }
}
