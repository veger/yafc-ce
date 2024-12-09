using System.Collections.Generic;

namespace Yafc.Model;

public static class Dependencies {
    /// <summary>
    /// The objects the key requires, organized into useful categories. Some categories are requires-any, others are requires-all.
    /// e.g. <c>dependencyList["Item.steel-plate"]</c> will contain the recipes that produce it and the entities that have it as loot.
    /// </summary>
    public static Mapping<FactorioObject, DependencyNode> dependencyList { get; private set; }
    /// <summary>
    /// The objects that require the key. e.g. <c>reverseDependencies["Item.steel-plate"]</c> will contain the recipes that consume steel plate.
    /// </summary>
    public static Mapping<FactorioObject, List<FactorioId>> reverseDependencies { get; private set; }

    public static void Calculate() {
        dependencyList = Database.objects.CreateMapping<DependencyNode>();
        reverseDependencies = Database.objects.CreateMapping<List<FactorioId>>();

        foreach (var obj in Database.objects.all) {
            reverseDependencies[obj] = [];
        }

        foreach (var obj in Database.objects.all) {
            DependencyNode packed = obj.GetDependencies();
            dependencyList[obj] = packed;

            foreach (FactorioId req in packed.Flatten()) {
                if (!reverseDependencies[req].Contains(obj.id)) {
                    reverseDependencies[req].Add(obj.id);
                }
            }
        }
    }
}
