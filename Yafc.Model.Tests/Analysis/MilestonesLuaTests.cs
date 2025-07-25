using System.Reflection;
using Xunit;

namespace Yafc.Model.Tests;

[Collection("LuaDependentTests")]
public class MilestonesLuaTests {
    [Fact]
    public void SecondProjectIsOpened_ShouldUpdateProjectField() {
        FieldInfo projectField = typeof(Milestones).GetField("project", BindingFlags.NonPublic | BindingFlags.Instance);

        // Open the first project:
        var project = LuaDependentTestHelper.GetProjectForLua("Yafc.Model.Tests.BareMinimumLuaData.lua");
        Assert.Equal(projectField.GetValue(Milestones.Instance), project);

        // Now open a second project:
        project = LuaDependentTestHelper.GetProjectForLua("Yafc.Model.Tests.BareMinimumLuaData.lua");
        Assert.Equal(projectField.GetValue(Milestones.Instance), project);

        // Just for the fun of it, open a third too:
        project = LuaDependentTestHelper.GetProjectForLua("Yafc.Model.Tests.BareMinimumLuaData.lua");
        Assert.Equal(projectField.GetValue(Milestones.Instance), project);
    }
}
