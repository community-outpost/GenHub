using System.IO;
using System.Text;
using GenHub.Core.Services.Tools.GenHotkeys;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for <see cref="CommandMapFile"/>.
/// </summary>
public class CommandMapFileTests
{
    /// <summary>
    /// Verifies that CommandMap.ini preserves entries and properties through serialization.
    /// </summary>
    [Fact]
    public void LoadAndSave_RoundTrip_PreservesCommandEntries()
    {
        var iniContent = @"
; Generals CommandMap test
CommandMap Command_ConstructAmericaDozer
  Key = KEY_D
  Transition = DOWN
  Modifiers = NONE
  Category = INTERFACE
End

CommandMap Command_ConstructAmericaRanger
  Key = KEY_R
  Transition = DOWN
  Modifiers = NONE
  Category = INTERFACE
End
";

        using var readStream = new MemoryStream(Encoding.UTF8.GetBytes(iniContent));
        var cmdMap = CommandMapFile.Load(readStream);

        Assert.Equal(2, cmdMap.Entries.Count);
        var dozer = cmdMap.Entries.Find(e => e.Name == "Command_ConstructAmericaDozer");
        var ranger = cmdMap.Entries.Find(e => e.Name == "Command_ConstructAmericaRanger");

        Assert.NotNull(dozer);
        Assert.NotNull(ranger);
        Assert.Equal("KEY_D", dozer.Key);
        Assert.Equal("KEY_R", ranger.Key);

        using var writeStream = new MemoryStream();
        cmdMap.Save(writeStream);
        var writtenString = Encoding.UTF8.GetString(writeStream.ToArray());

        Assert.Contains("CommandMap Command_ConstructAmericaDozer", writtenString);
        Assert.Contains("Key = KEY_D", writtenString);
        Assert.Contains("CommandMap Command_ConstructAmericaRanger", writtenString);
        Assert.Contains("Key = KEY_R", writtenString);
    }
}
