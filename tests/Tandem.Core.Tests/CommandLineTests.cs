using Tandem.Core.Processes;

namespace Tandem.Core.Tests;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData(@"a.exe b c", new[] { "a.exe", "b", "c" })]
    [InlineData(@"""C:\Program Files\x\a.exe"" --flag", new[] { @"C:\Program Files\x\a.exe", "--flag" })]
    [InlineData(@"a.exe ""b c"" d", new[] { "a.exe", "b c", "d" })]
    [InlineData(@"a.exe a\\\""b", new[] { "a.exe", @"a\""b" })]
    [InlineData(@"a.exe ""a\\"" b", new[] { "a.exe", @"a\", "b" })]
    [InlineData(@"a.exe a\\b", new[] { "a.exe", @"a\\b" })]
    [InlineData(@"a.exe ""say """"hi""""""", new[] { "a.exe", @"say ""hi""" })]
    [InlineData("a.exe \t  b\t", new[] { "a.exe", "b" })]
    [InlineData(@"C:\dir\a.exe x", new[] { @"C:\dir\a.exe", "x" })]
    public void Split_follows_windows_rules(string commandLine, string[] expected)
    {
        Assert.Equal(expected, CommandLine.Split(commandLine));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Split_of_nothing_is_empty(string? commandLine)
    {
        Assert.Empty(CommandLine.Split(commandLine));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("with space")]
    [InlineData(@"trailing\")]
    [InlineData(@"trailing space\ ")]
    [InlineData(@"quote""inside")]
    [InlineData(@"back\\""slash")]
    [InlineData("")]
    [InlineData(@"C:\Users\Jane Doe\AppData\Local\Tandem\Profiles\abcdefgh\")]
    public void Quote_round_trips(string argument)
    {
        var parsed = CommandLine.Split("x.exe " + CommandLine.Quote(argument));

        Assert.Equal(argument, parsed[1]);
    }

    [Fact]
    public void Switch_lookup_matches_chromium_behaviour()
    {
        var args = CommandLine.Split(@"Claude.exe --type=renderer -User-Data-Dir=C:\a /lang=en --flag -- --user-data-dir=C:\ignored");

        Assert.Equal("renderer", CommandLine.GetSwitchValue(args, "type"));
        Assert.Equal(@"C:\a", CommandLine.GetSwitchValue(args, "user-data-dir"));
        Assert.Equal("en", CommandLine.GetSwitchValue(args, "lang"));
        Assert.True(CommandLine.HasSwitch(args, "flag"));
        Assert.Null(CommandLine.GetSwitchValue(args, "flag"));
        Assert.False(CommandLine.HasSwitch(args, "missing"));
    }

    [Fact]
    public void Program_name_and_links_are_not_switches()
    {
        var args = CommandLine.Split(@"--type=fake.exe claude://claude.ai/x?type=y");

        Assert.False(CommandLine.HasSwitch(args, "type"));
    }

    [Fact]
    public void Main_process_has_no_type_switch_but_children_do()
    {
        var main = CommandLine.Split(@"""C:\Program Files\WindowsApps\Claude_2.1.0.0_x64__pzs8sxrjxfjjc\app\Claude.exe"" ""--user-data-dir=C:\Users\me\AppData\Local\Tandem\Profiles\abcdefgh""");
        var child = CommandLine.Split(@"""C:\Program Files\WindowsApps\Claude_2.1.0.0_x64__pzs8sxrjxfjjc\app\Claude.exe"" --type=gpu-process ""--user-data-dir=C:\Users\me\AppData\Roaming\Claude""");

        Assert.False(CommandLine.HasSwitch(main, "type"));
        Assert.Equal(@"C:\Users\me\AppData\Local\Tandem\Profiles\abcdefgh", CommandLine.GetSwitchValue(main, "user-data-dir"));
        Assert.True(CommandLine.HasSwitch(child, "type"));
    }
}
