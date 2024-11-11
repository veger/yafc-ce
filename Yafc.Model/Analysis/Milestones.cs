using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Serilog;
using Yafc.UI;

namespace Yafc.Model;

public class Milestones : Analysis {
    public static readonly Milestones Instance = new Milestones();

    private static readonly ILogger logger = Logging.GetLogger<Milestones>();

    public FactorioObject[] currentMilestones = [];
    private Mapping<FactorioObject, Bits> milestoneResult;
    public Bits lockedMask { get; private set; } = new();
    private Project? project;

    public bool IsAccessibleWithCurrentMilestones(FactorioId obj) => (milestoneResult[obj] & lockedMask) == 1;

    public bool IsAccessibleWithCurrentMilestones(FactorioObject obj) => (milestoneResult[obj] & lockedMask) == 1;

    public bool IsAccessibleAtNextMilestone(FactorioObject obj) {
        var milestoneMask = milestoneResult[obj] & lockedMask;
        if (milestoneMask == 1) {
            return true;
        }

        if (milestoneMask[0]) {
            return false;
        }
        // TODO Always returns false -> milestoneMask is a power of 2 + 1 always has bit 0 set, as x pow 2 sets one (high) bit,
        // so the + 1 adds bit 0, which is detected by (milestoneMask & 1) != 0
        // return ((milestoneMask - 1) & (milestoneMask - 2)) == 0; // milestoneMask is a power of 2 + 1
        return false;
    }
    /// <summary>
    /// Return a copy of Bits
    /// </summary>
    public Bits GetMilestoneResult(FactorioId obj) => new Bits(milestoneResult[obj]);

    /// <summary>
    /// Return a copy of Bits
    /// </summary>
    public Bits GetMilestoneResult(FactorioObject obj) => new Bits(milestoneResult[obj]);

    private void GetLockedMaskFromProject() {
        if (project is null) {
            throw new InvalidOperationException($"{nameof(project)} must be set before calling {nameof(GetLockedMaskFromProject)}");
        }

        Bits bits = new(true); // The first bit is skipped (index is increased before the first bit is written) and always set
        int index = 0;
        foreach (var milestone in currentMilestones) {
            index++;
            bits[index] = !project.settings.Flags(milestone).HasFlags(ProjectPerItemFlags.MilestoneUnlocked);
        }
        lockedMask = bits;
    }

    private void ProjectSettingsChanged(bool visualOnly) {
        if (!visualOnly) {
            GetLockedMaskFromProject();
        }
    }

    public FactorioObject? GetHighest(FactorioObject target, bool all) {
        if (target == null) {
            return null;
        }

        var ms = milestoneResult[target];
        if (!all) {
            ms &= lockedMask;
        }

        if (ms == 0) {
            return null;
        }

        int msb = ms.HighestBitSet() - 1;

        return msb < 0 || msb >= currentMilestones.Length ? null : currentMilestones[msb];
    }

    [Flags]
    private enum ProcessingFlags : byte {
        InQueue = 1,
        Initial = 2,
        MilestoneNeedOrdering = 4,
        ForceInaccessible = 8
    }

    public override void Compute(Project project, ErrorCollector warnings) {
        if (project.settings.milestones.Count == 0) {
            FactorioObject[] milestones = new List<FactorioObject>([.. Database.allSciencePacks, .. Database.locations.all])
                .Where(m => m is not Location { name: "nauvis" or "space-location-unknown" }).ToArray();
            ComputeWithParameters(project, warnings, milestones, true);
        }
        else {
            ComputeWithParameters(project, warnings, [.. project.settings.milestones], false);
        }
    }

    public void ComputeWithParameters(Project project, ErrorCollector warnings, FactorioObject[] milestones, bool autoSort) {
        if (this.project == null) {
            this.project = project;
            project.settings.changed += ProjectSettingsChanged;
        }

        Stopwatch time = Stopwatch.StartNew();
        Dictionary<FactorioId, HashSet<FactorioObject>> accessibility = [];
        const FactorioId noObject = (FactorioId)(-1);

        List<FactorioObject> sortedMilestones = [];
        HashSet<FactorioObject> markedInaccessible = [];
        foreach ((FactorioObject item, ProjectPerItemFlags flags) in project.settings.itemFlags) {
            if (flags.HasFlag(ProjectPerItemFlags.MarkedInaccessible)) {
                markedInaccessible.Add(item);
            }
        }

        foreach (FactorioObject? milestone in milestones.Prepend(null)) {
            logger.Information("Processing milestone {Milestone}", milestone?.locName);
            // Queue the known-accessible items for graph walking, and mark them accessible without the current milestone.
            Queue<FactorioObject> processingQueue = new(Database.rootAccessible);
            foreach ((FactorioObject obj, ProjectPerItemFlags flag) in project.settings.itemFlags) {
                if (flag.HasFlags(ProjectPerItemFlags.MarkedAccessible)) {
                    processingQueue.Enqueue(obj);
                }
            }
            HashSet<FactorioObject> accessibleWithoutMilestone = accessibility[milestone?.id ?? noObject] = new(processingQueue);

            // Walk the dependency graph to find accessible items. The first walk, when milestone == null, is for basic accessibility.
            // The rest of the walks prune the graph at the selected milestone and are for milestone flags.
            while (processingQueue.TryDequeue(out FactorioObject? node)) {
                if (node == milestone || markedInaccessible.Contains(node)) {
                    // We're looking for things that can be accessed without this milestone, or the user flagged this as inaccessible.
                    continue;
                }

                bool accessible = true;
                if (!accessibleWithoutMilestone.Contains(node)) {
                    // This object is accessible if all its parents are accessible.
                    foreach (DependencyList list in Dependencies.dependencyList[node]) {
                        if (list.flags.HasFlag(DependencyList.Flags.RequireEverything)) {
                            accessible &= list.elements.All(e => accessibleWithoutMilestone.Contains(Database.objects[e]));
                        }
                        else {
                            accessible &= list.elements.Any(e => accessibleWithoutMilestone.Contains(Database.objects[e]));
                        }
                    }
                }

                if (accessible) {
                    accessibleWithoutMilestone.Add(node);
                    if (milestones.Contains(node) && !sortedMilestones.Contains(node)) {
                        // Sort milestones in the order we unlock them in the accessibility walk.
                        sortedMilestones.Add(node);
                    }

                    // Recheck this objects children, if necessary.
                    foreach (FactorioObject child in Dependencies.reverseDependencies[node].Select(id => Database.objects[id])) {
                        if (!accessibleWithoutMilestone.Contains(child) && !processingQueue.Contains(child)) {
                            processingQueue.Enqueue(child);
                        }
                    }
                }
            }
        }

        // Apply the milestone sort results, if requested.
        if (autoSort) {
            // If the user has inaccessible milestones, restore them at the end of the list.
            currentMilestones = [.. sortedMilestones.Union(milestones)];
        }
        else {
            currentMilestones = milestones;
        }

        // Turn the walk results into milestone bitmasks.
        Mapping<FactorioObject, Bits> result = Database.objects.CreateMapping<Bits>();
        foreach (FactorioObject obj in accessibility[noObject]) {
            Bits bits = new(true);

            for (int i = 0; i < currentMilestones.Length; i++) {
                FactorioObject milestone = currentMilestones[i];
                if (!accessibility[milestone.id].Contains(obj)) {
                    bits[i + 1] = true;
                }
            }
            result[obj] = bits;
        }

        if (!project.settings.milestones.SequenceEqual(currentMilestones)) {
            _ = project.settings.RecordUndo();
            project.settings.milestones.Clear();
            project.settings.milestones.AddRange(currentMilestones);
        }
        GetLockedMaskFromProject();

        int accessibleObjects = accessibility[noObject].Count;
        bool hasAutomatableRocketLaunch = result[Database.objectsByTypeName["Special.launch"]] != 0;
        List<FactorioObject> milestonesNotReachable = [.. milestones.Except(sortedMilestones)];
        if (accessibleObjects < Database.objects.count / 2) {
            warnings.Error("More than 50% of all in-game objects appear to be inaccessible in this project with your current mod list. This can have a variety of reasons like objects " +
                "being accessible via scripts," + MaybeBug + MilestoneAnalysisIsImportant + UseDependencyExplorer, ErrorSeverity.AnalysisWarning);
        }
        else if (!hasAutomatableRocketLaunch) {
            warnings.Error("Rocket launch appear to be inaccessible. This means that rocket may not be launched in this mod pack, or it requires mod script to spawn or unlock some items," +
                MaybeBug + MilestoneAnalysisIsImportant + UseDependencyExplorer, ErrorSeverity.AnalysisWarning);
        }
        else if (milestonesNotReachable.Count > 0) {
            warnings.Error("There are some milestones that are not accessible: " + string.Join(", ", milestonesNotReachable.Select(x => x.locName)) +
                ". You may remove these from milestone list," + MaybeBug + MilestoneAnalysisIsImportant + UseDependencyExplorer, ErrorSeverity.AnalysisWarning);
        }

        logger.Information("Milestones calculation finished in {ElapsedTime}ms.", time.ElapsedMilliseconds);
        milestoneResult = result;
    }

    private const string MaybeBug = " or it might be due to a bug inside a mod or YAFC.";
    private const string MilestoneAnalysisIsImportant = "\nA lot of YAFC's systems rely on objects being accessible, so some features may not work as intended.";
    private const string UseDependencyExplorer = "\n\nFor this reason YAFC has a Dependency Explorer that allows you to manually enable some of the core recipes. " +
        "YAFC will iteratively try to unlock all the dependencies after each recipe you manually enabled. " +
        "For most modpacks it's enough to unlock a few early recipes like any special recipes for plates that everything in the mod is based on.";

    public override string description => "Milestone analysis starts from objects that are placed on map by the map generator and tries to find all objects that are accessible from that, " +
        "taking notes about which objects are locked behind which milestones.";
}
