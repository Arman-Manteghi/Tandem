using Tandem.Core.Claude;
using Tandem.Core.Processes;
using Tandem.Core.Profiles;

namespace Tandem.Core.Tests;

public sealed class LaunchArgumentsTests
{
    private static readonly Profile Default = new() { Id = Profile.DefaultId, DisplayName = "Default", Color = "#E07A5F" };

    private static Profile Custom(string folder) => new() { Id = "abcdefgh", DisplayName = "Work", Color = "#3B82C4", DataFolder = folder };

    [Fact]
    public void Default_profile_gets_no_arguments_at_all()
    {
        Assert.Empty(LaunchArguments.Build(Default));
    }

    [Fact]
    public void Custom_profile_gets_exactly_one_user_data_dir_switch()
    {
        var args = LaunchArguments.Build(Custom(@"C:\Users\me\AppData\Local\Tandem\Profiles\abcdefgh"));

        Assert.Equal([@"--user-data-dir=C:\Users\me\AppData\Local\Tandem\Profiles\abcdefgh"], args);
    }

    [Fact]
    public void Folder_path_is_normalized()
    {
        var args = LaunchArguments.Build(Custom(@"  ""C:\Users\me\AppData\Local\Tandem\Profiles\x\..\abcdefgh\""  "));

        Assert.Equal(@"--user-data-dir=C:\Users\me\AppData\Local\Tandem\Profiles\abcdefgh", Assert.Single(args));
    }

    [Fact]
    public void Link_is_appended_unchanged()
    {
        const string link = "claude://claude.ai/login?code=abc&state=xyz";

        Assert.Equal([link], LaunchArguments.Build(Default, link));
        Assert.Equal(link, LaunchArguments.Build(Custom(@"C:\p\abcdefgh"), link)[1]);
    }

    [Theory]
    [InlineData("https://claude.ai")]
    [InlineData("claude:")]
    [InlineData("claude://with space")]
    [InlineData("claude://x\" --inject")]
    [InlineData("--remote-debugging-port=9222")]
    [InlineData("")]
    public void Anything_but_a_claude_link_is_rejected(string link)
    {
        Assert.Throws<ArgumentException>(() => LaunchArguments.Build(Default, link));
    }

    [Fact]
    public void Custom_profile_without_folder_is_an_error()
    {
        var broken = Custom(" ");

        Assert.Throws<InvalidOperationException>(() => LaunchArguments.Build(broken));
    }

    [Theory]
    [InlineData(@"C:\Users\Jane Doe\AppData\Local\Tandem\Profiles\abcdefgh")]
    [InlineData(@"C:\odd\path with ""quotes"" and trailing slash\")]
    public void Arguments_survive_windows_quoting(string folder)
    {
        var args = LaunchArguments.Build(Custom(folder), "claude://claude.ai/x?y=1");

        var roundTrip = CommandLine.Split("claude.exe " + CommandLine.Join(args));

        Assert.Equal(args, roundTrip.Skip(1));
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), CommandLine.GetSwitchValue(roundTrip, "user-data-dir"));
    }
}
