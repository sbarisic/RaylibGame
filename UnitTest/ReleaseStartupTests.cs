namespace UnitTest;

public sealed class ReleaseStartupTests
{
    [Fact]
    public void OrdinaryBuildNeverRedirectsToUpdater()
    {
        Assert.False(Voxelgine.ReleaseStartup.RedirectToLauncher(["--data-root", "unused"]));
    }
}
