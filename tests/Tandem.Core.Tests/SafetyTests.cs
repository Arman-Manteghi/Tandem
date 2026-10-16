using Tandem.Core.Claude;
using Tandem.Core.Logging;
using Tandem.Core.Profiles;
using Tandem.Core.Safety;
using Tandem.Core.Shell;

namespace Tandem.Core.Tests;

public sealed class SafetyTests
{
    private const string Roaming = @"C:\Users\me\AppData\Roaming";
    private const string Local = @"C:\Users\me\AppData\Local";

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Tandem\Profiles\abcdefgh", @"C:\Users\me\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\Tandem\Profiles\abcdefgh")]
    [InlineData(@"C:\Users\me\AppData\Roaming\Tandem\x", @"C:\Users\me\AppData\Roaming\..\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Tandem\x")]
    public void Mirror_path_points_into_the_package_cache(string folder, string expected)
    {
        Assert.Equal(Path.GetFullPath(expected), MsixVirtualization.GetPackageMirror(folder, Roaming, Local));
    }

    [Theory]
    [InlineData(@"D:\Profiles\abcdefgh")]
    [InlineData(@"C:\Users\me\Documents\x")]
    [InlineData(@"C:\Users\me\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\x")]
    [InlineData(@"C:\Users\me\AppData\Local")]
    public void Folders_outside_appdata_or_inside_packages_have_no_mirror(string folder)
    {
        Assert.Null(MsixVirtualization.GetPackageMirror(folder, Roaming, Local));
    }

    [Theory]
    [InlineData("claude://claude.ai/login?code=SECRET&state=S", "claude:<redacted>")]
    [InlineData("forwarded CLAUDE://x/y?z=1 to abc", "forwarded claude:<redacted> to abc")]
    [InlineData("Claude: not installed", "Claude: not installed")]
    public void Redactor_hides_links(string input, string expected)
    {
        Assert.Equal(expected, Redactor.Redact(input, @"C:\Users\me"));
    }

    [Fact]
    public void Redactor_hides_the_user_profile_path()
    {
        Assert.Equal(@"Created %USERPROFILE%\AppData\Local\Tandem", Redactor.Redact(@"Created c:\users\ME\AppData\Local\Tandem", @"C:\Users\me"));
    }

    [Theory]
    [InlineData("claude://claude.ai/new", true)]
    [InlineData("CLAUDE://x", true)]
    [InlineData("claude:x", true)]
    [InlineData("claude:", false)]
    [InlineData("https://claude.ai", false)]
    [InlineData("claude://a b", false)]
    [InlineData("claude://a\nb", false)]
    [InlineData(null, false)]
    public void Only_well_formed_claude_links_are_accepted(string? link, bool valid)
    {
        Assert.Equal(valid, ClaudeLink.IsValid(link));
    }

    [Fact]
    public void Overlong_links_are_rejected()
    {
        Assert.False(ClaudeLink.IsValid("claude://" + new string('a', ClaudeLink.MaxLength)));
    }

    [Fact]
    public void Running_instances_map_to_profiles_by_data_folder()
    {
        Profile[] profiles =
        [
            new() { Id = Profile.DefaultId, DisplayName = "Default", Color = "#E07A5F" },
            new() { Id = "workwork", DisplayName = "Work", Color = "#3B82C4", DataFolder = @"C:\Users\me\AppData\Local\Tandem\Profiles\workwork" },
        ];

        Assert.True(ProfileMatcher.Match(profiles, null)!.IsDefault);
        Assert.Equal("workwork", ProfileMatcher.Match(profiles, @"c:\users\ME\appdata\local\tandem\profiles\WORKWORK\")!.Id);
        Assert.Null(ProfileMatcher.Match(profiles, @"C:\somewhere\else"));
    }

    [Fact]
    public void Generated_icon_is_a_valid_ico_file()
    {
        var ico = IconFactory.CreateIco("#3B82C4", IconFactory.Cream, [16, 32, 256]);

        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));
        Assert.Equal(3, BitConverter.ToUInt16(ico, 4));
        Assert.Equal(16, ico[6]);
        Assert.Equal(0, ico[6 + 32]); // 256 is stored as 0
        var lastOffset = BitConverter.ToInt32(ico, 6 + 32 + 12);
        var lastSize = BitConverter.ToInt32(ico, 6 + 32 + 8);
        Assert.Equal(ico.Length, lastOffset + lastSize);
    }

    [Fact]
    public void Icon_corners_are_transparent_and_center_is_opaque()
    {
        var pixels = IconFactory.RenderBgra(32, "#3B82C4", IconFactory.Cream);

        Assert.Equal(0, pixels[3]);
        Assert.Equal(255, pixels[((16 * 32) + 16) * 4 + 3]);
    }

    [Fact]
    public void Palette_normalizes_colors()
    {
        Assert.Equal("#ABCDEF", ProfilePalette.Normalize(" abcdef "));
        Assert.Null(ProfilePalette.Normalize("#abcd"));
        Assert.Equal(ProfilePalette.Colors[0].Hex, ProfilePalette.NextUnused([]));
    }
}
