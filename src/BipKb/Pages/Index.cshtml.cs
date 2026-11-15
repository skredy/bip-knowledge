using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace BipKb.Pages;
public class IndexModel(Workspace workspace):PageModel
{
    public Knowledge Knowledge {get;private set;}=new();
    public List<Claim> Claims {get;private set;}=[];
    public List<Decision> Decisions {get;private set;}=[];
    public string View {get;private set;}="search";
    public string Query {get;private set;}="";
    public string? Error {get;private set;}
    public void OnGet(string? view,string? q,string? id)
    {
        Knowledge=workspace.Load();Decisions=workspace.Decisions();View=view??"search";Query=q??"";
        if(View=="search"&&!string.IsNullOrWhiteSpace(Query)){var snapshot=workspace.Query(Query);Knowledge=snapshot.Knowledge;Claims=snapshot.Claims;return;}
        Claims=View switch{"review"=>workspace.Review(Knowledge).ToList(),"detail"=>Knowledge.Claims.Where(c=>c.Id==id||c.EntityId==id).ToList(),_=>string.IsNullOrWhiteSpace(Query)?Knowledge.Claims.Where(c=>!c.Obsolete).Take(30).ToList():workspace.Search(Query)};
    }
    public IActionResult OnPost(string claimId,string action,string reason,string actor)
    {
        try{workspace.Decide(claimId,action,reason,actor);return RedirectToPage(new{view="review"});}
        catch(Exception e) when(e is ArgumentException or InvalidOperationException){OnGet("review",null,null);Error=e.Message;return Page();}
    }
}
