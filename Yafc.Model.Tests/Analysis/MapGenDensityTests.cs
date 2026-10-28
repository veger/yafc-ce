using System;
using Xunit;

namespace Yafc.Model.Tests;

[Collection("LuaDependentTests")]
public class MapGenDensityTests {
    private static Entity GetEntity(string name) => (Entity)Database.objectsByTypeName["Entity." + name];
    private static Recipe GetMiningRecipe(string name) => (Recipe)Database.objectsByTypeName["Mechanics.mining.basic-solid." + name];

    [Fact]
    public void MapGenDensity_IsReadFromFactorio2NoiseExpressions() {
        _ = LuaDependentTestHelper.GetProjectForLua("Yafc.Model.Tests.Analysis.MapGenDensityTests.lua");

        // base_density from the noise expression referenced by var('...')
        Assert.Equal(10f, GetEntity("iron-ore").mapGenDensity);
        Assert.Equal(0.9f, GetEntity("uranium-ore").mapGenDensity);
        // base_density directly in the autoplace expression
        Assert.Equal(2.5f, GetEntity("inline-ore").mapGenDensity);
        // Hand-written expressions and numeric expressions have no base_density: density unknown
        Assert.Equal(0f, GetEntity("tungsten-ore").mapGenDensity);
        Assert.Equal(0f, GetEntity("numeric-ore").mapGenDensity);

        Assert.True(GetEntity("tungsten-ore").mapGenerated);
        Assert.False(GetEntity("hidden-ore").mapGenerated);
    }

    [Fact]
    public void MiningPenalty_DependsOnDensity_AndIgnoresUnknownDensity() {
        _ = LuaDependentTestHelper.GetProjectForLua("Yafc.Model.Tests.Analysis.MapGenDensityTests.lua");
        var costs = CostAnalysis.Instance.recipeCost;

        // All mining recipes have the same time, products and crafter, so their costs differ only by the mining penalty 1 + ln(30 / density).
        float unknownDensity = costs[GetMiningRecipe("tungsten-ore")];
        float common = costs[GetMiningRecipe("iron-ore")];
        float rare = costs[GetMiningRecipe("uranium-ore")];

        Assert.True(unknownDensity > 0);
        // An unknown density gets no rarity penalty, instead of the maximum one.
        Assert.Equal(1 + MathF.Log(30 / 10f), common / unknownDensity, 1e-3f);
        Assert.Equal(1 + MathF.Log(30 / 0.9f), rare / unknownDensity, 1e-3f);
    }
}
