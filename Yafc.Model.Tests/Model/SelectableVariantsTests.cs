using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Yafc.Model.Tests.Model;

[Collection("LuaDependentTests")]
public class SelectableVariantsTests {
    [Fact]
    public async Task CanSelectVariantFuel_VariantFuelChanges() {
        Project project = LuaDependentTestHelper.GetProjectForLua();

        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        ProductionTable table = (ProductionTable)page.content;
        table.AddRecipe(Database.recipes.all.Single(r => r.name == "generator.electricity"), DataUtils.DeterministicComparer);
        RecipeRow row = table.GetAllRecipes().Single();

        // Solve is not necessary in this test, but I'm calling it in case we decide to hide the fuel on disabled recipes.
        await table.Solve((ProjectPage)table.owner);
        Assert.Equal("steam@165", row.FuelInformation.Goods.name);

        row.fuel = row.FuelInformation.Variants[1];
        await table.Solve((ProjectPage)table.owner);
        Assert.Equal("steam@500", row.FuelInformation.Goods.name);
    }

    [Fact]
    public async Task CanSelectVariantFuelWithFavorites_VariantFuelChanges() {
        Project project = LuaDependentTestHelper.GetProjectForLua();
        project.preferences.ToggleFavorite(Database.fluids.all.Single(c => c.name == "steam@500"));

        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        ProductionTable table = (ProductionTable)page.content;
        table.AddRecipe(Database.recipes.all.Single(r => r.name == "generator.electricity"), DataUtils.DeterministicComparer);
        RecipeRow row = table.GetAllRecipes().Single();

        // Solve is not necessary in this test, but I'm calling it in case we decide to hide the fuel on disabled recipes.
        await table.Solve((ProjectPage)table.owner);
        Assert.Equal("steam@500", row.FuelInformation.Goods.name);

        row.fuel = row.FuelInformation.Variants[0];
        await table.Solve((ProjectPage)table.owner);
        Assert.Equal("steam@165", row.FuelInformation.Goods.name);
    }

    [Fact]
    public async Task CanSelectVariantIngredient_VariantIngredientChanges() {
        Project project = LuaDependentTestHelper.GetProjectForLua();

        ProjectPage page = new ProjectPage(project, typeof(ProductionTable));
        ProductionTable table = (ProductionTable)page.content;
        table.AddRecipe(Database.recipes.all.Single(r => r.name == "steam_void"), DataUtils.DeterministicComparer);
        RecipeRow row = table.GetAllRecipes().Single();

        // Solve is necessary here: Disabled recipes have null ingredients (and products), and Solve is the call that updates hierarchyEnabled.
        await table.Solve((ProjectPage)table.owner);
        Assert.Equal("steam@165", row.Ingredients.Single().Goods.name);

        row.ChangeVariant(row.Ingredients.Single().Goods, row.Ingredients.Single().Variants[1]);
        await table.Solve((ProjectPage)table.owner);
        Assert.Equal("steam@500", row.Ingredients.Single().Goods.name);
    }

    // No corresponding CanSelectVariantIngredientWithFavorites: Favorites control fuel selection, but not ingredient selection.
}
