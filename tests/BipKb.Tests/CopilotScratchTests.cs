using BipKb;
using Xunit;

namespace BipKb.Tests;

public sealed class WindowsFactAttribute:FactAttribute
{
    public WindowsFactAttribute(){if(!OperatingSystem.IsWindows())Skip="Exercises a Windows file handle that denies deletion.";}
}

public class CopilotScratchTests
{
    [WindowsFact]
    public async Task WindowsOpenHandleLeavesWarningInsteadOfFailingAnalysis()
    {
        var warnings=new StringWriter();var scratch=CopilotScratch.Create(warnings:warnings);
        try
        {
            using(var locked=new FileStream(Path.Combine(scratch.DirectoryPath,"locked.txt"),FileMode.Create,FileAccess.Write,FileShare.Read))
            {
                await scratch.DisposeAsync();Assert.True(Directory.Exists(scratch.DirectoryPath));Assert.Contains("non supprimé",warnings.ToString());
            }
        }
        finally{Directory.Delete(scratch.DirectoryPath,true);}
    }
    [Fact]
    public async Task ReadOnlyFilesAndDirectoriesAreRemovedFromOwnedScratch()
    {
        var scratch=CopilotScratch.Create();var directory=Path.Combine(scratch.DirectoryPath,"nested");Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,"generated.txt");File.WriteAllText(file,"temporary");
        File.SetAttributes(file,File.GetAttributes(file)|FileAttributes.ReadOnly);File.SetAttributes(directory,File.GetAttributes(directory)|FileAttributes.ReadOnly);
        await scratch.DisposeAsync();Assert.False(Directory.Exists(scratch.DirectoryPath));
    }
    [Fact]
    public async Task TemporaryLockIsRetriedAndDoesNotWarnAfterSuccess()
    {
        var attempts=0;var warnings=new StringWriter();
        var scratch=CopilotScratch.Create(delete:path=>{if(++attempts<3)throw new IOException("locked");Directory.Delete(path,true);},warnings:warnings);
        await scratch.DisposeAsync();Assert.Equal(3,attempts);Assert.Empty(warnings.ToString());Assert.False(Directory.Exists(scratch.DirectoryPath));
    }
    [Fact]
    public async Task PermanentAccessDenialDoesNotReplaceSuccessfulProviderResult()
    {
        var warnings=new StringWriter();var scratch=CopilotScratch.Create(delete:_=>throw new UnauthorizedAccessException("denied"),warnings:warnings);
        try
        {
            async Task<string> Analyze(){await using var owned=scratch;File.WriteAllText(Path.Combine(owned.DirectoryPath,"attachment.txt"),"private attachment");return "valid result";}
            Assert.Equal("valid result",await Analyze());Assert.True(Directory.Exists(scratch.DirectoryPath));
            Assert.Contains("non supprimé",warnings.ToString());Assert.Contains(scratch.DirectoryPath,warnings.ToString());Assert.DoesNotContain("private attachment",warnings.ToString());
        }
        finally{Directory.Delete(scratch.DirectoryPath,true);}
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailurePreservesOriginalExceptionIncludingCancellation(bool cancelled)
    {
        var scratch=CopilotScratch.Create(delete:_=>throw new UnauthorizedAccessException("cleanup denied"),warnings:TextWriter.Null);
        Exception original=cancelled?new OperationCanceledException("cancelled"):new InvalidOperationException("provider authentication failed");
        try
        {
            async Task Analyze(){await using var owned=scratch;throw original;}
            var error=await Record.ExceptionAsync(Analyze);Assert.Same(original,error);
        }
        finally{Directory.Delete(scratch.DirectoryPath,true);}
    }
    [Fact]
    public async Task MissingAttachmentStillCleansTheNewDirectory()
    {
        string? path=null;
        async Task Attach(){await using var scratch=CopilotScratch.Create();path=scratch.DirectoryPath;File.Copy(Path.Combine(path,"missing.png"),Path.Combine(path,"attachment.png"));}
        await Assert.ThrowsAsync<FileNotFoundException>(Attach);Assert.NotNull(path);Assert.False(Directory.Exists(path));
    }
    [UnixFact]
    public async Task CleanupDoesNotFollowLinksOrChangeExternalReadOnlyFiles()
    {
        using var s=new Sandbox();var external=s.File("keep.txt","keep");File.SetAttributes(external,File.GetAttributes(external)|FileAttributes.ReadOnly);
        var scratch=CopilotScratch.Create();Directory.CreateSymbolicLink(Path.Combine(scratch.DirectoryPath,"linked"),s.Source);
        try
        {
            await scratch.DisposeAsync();Assert.False(Directory.Exists(scratch.DirectoryPath));Assert.Equal("keep",File.ReadAllText(external));Assert.True((File.GetAttributes(external)&FileAttributes.ReadOnly)!=0);
        }
        finally{File.SetAttributes(external,FileAttributes.Normal);}
    }
}
