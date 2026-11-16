using BipKb;
using Xunit;

namespace BipKb.Tests;

public class AtomicFileTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransientAccessOrSharingFailureRetriesWithoutTouchingOldFile(bool sharing)
    {
        using var s=new Sandbox();var path=s.File("config.json","{\"old\":true}");var attempts=0;var delays=new List<TimeSpan>();
        AtomicFile.Write(path,"{\"new\":true}",(temp,target)=>
        {
            Assert.Equal("{\"old\":true}",File.ReadAllText(target));
            Assert.Equal("{\"new\":true}",File.ReadAllText(temp));
            if(++attempts<3)
            {
                if(sharing)throw new IOException("sharing violation",unchecked((int)0x80070020));
                throw new UnauthorizedAccessException("access denied");
            }
            File.Move(temp,target,true);
        },delays.Add);
        Assert.Equal(3,attempts);Assert.Equal(2,delays.Count);Assert.Equal("{\"new\":true}",File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(s.Source,"*.tmp"));
    }
    [Fact]
    public void PermanentDenialIsBoundedAndPreservesDestination()
    {
        using var s=new Sandbox();var path=s.File("config.json","original");var attempts=0;
        var error=Assert.Throws<IOException>(()=>AtomicFile.Write(path,"replacement",(_,_)=>{attempts++;throw new UnauthorizedAccessException();},_=>{}));
        Assert.Equal(5,attempts);Assert.Contains(path,error.Message);Assert.Equal("original",File.ReadAllText(path));
        Assert.IsType<UnauthorizedAccessException>(error.InnerException);Assert.Empty(Directory.GetFiles(s.Source,"*.tmp"));
    }
    [Fact]
    public void OtherIoFailuresAreNotRetried()
    {
        using var s=new Sandbox();var path=s.File("config.json","original");var attempts=0;
        Assert.Throws<IOException>(()=>AtomicFile.Write(path,"replacement",(_,_)=>{attempts++;throw new IOException("disk failure",unchecked((int)0x80070070));},_=>throw new Exception("Must not wait")));
        Assert.Equal(1,attempts);Assert.Equal("original",File.ReadAllText(path));
    }
    [WindowsFact]
    public void WindowsReplacementSucceedsAfterReadHandleIsReleased()
    {
        using var s=new Sandbox();var path=s.File("config.json","original");
        using var handle=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        var delays=0;
        AtomicFile.Write(path,"replacement",delay:_=>{delays++;handle.Dispose();});
        Assert.True(delays>0);Assert.Equal("replacement",File.ReadAllText(path));
    }
    [WindowsFact]
    public void WindowsPersistentReadHandleDoesNotDestroyDestination()
    {
        using var s=new Sandbox();var path=s.File("config.json","original");
        using(var handle=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            Assert.Throws<IOException>(()=>AtomicFile.Write(path,"replacement",delay:_=>{}));
        Assert.Equal("original",File.ReadAllText(path));
    }
}
