using System;
using System.IO;
using Xunit;

namespace Yafc.Model.Tests;

public class ProjectTests {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadFromFile_CanLoadWithEmptyString(bool useMostRecent)
        => Assert.NotNull(Project.ReadFromFile("", new(), useMostRecent));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadFromFile_CanLoadNonexistentFile(bool useMostRecent)
        // Assuming that there are no files named <some-guid>.yafc in the current directory.
        => Assert.NotNull(Project.ReadFromFile(Guid.NewGuid().ToString() + ".yafc", new(), useMostRecent));

    [Fact]
    // PerformAutoSave is expected to be a no-op in this case.
    // This test may need more care and feeding if autosaving is added for nameless projects.
    public void PerformAutoSave_NoThrowWhenLoadedWithEmptyString()
        // No Assert in this test; the test passes if PerformAutoSave does not throw.
        => Project.ReadFromFile("", new(), false).PerformAutoSave();

    [Fact]
    public void Save_FlushesSuspendedUndoBatchBeforeMarkingVersionSaved() {
        string path = CreateTestProjectPath();
        try {
            Project project = new Project();
            project.Save(path);
            project.undo.Suspend();

            project.preferences.RecordUndo().time = 60;
            project.Save(path);

            Assert.Equal(0u, project.unsavedChangesCount);

            project.preferences.RecordUndo().time = 3600;

            Assert.True(project.unsavedChangesCount > 0);

            Assert.Contains("\"time\": 60", File.ReadAllText(path));
        }
        finally {
            DeleteProjectAndAutosaves(path);
        }
    }

    [Fact]
    public void PerformAutoSave_FlushesSuspendedUndoBatchBeforeMarkingVersionAutosaved() {
        string path = CreateTestProjectPath();
        try {
            Project project = new Project();
            project.Save(path);
            project.undo.Suspend();

            project.preferences.RecordUndo().time = 60;
            project.PerformAutoSave();
            project.preferences.RecordUndo().time = 3600;
            project.PerformAutoSave();

            Assert.Contains("\"time\": 3600", File.ReadAllText(Project.GenerateAutosavePath(path, 2)));
        }
        finally {
            DeleteProjectAndAutosaves(path);
        }
    }

    [Fact]
    public void PerformAutoSave_NewUnattachedProject_DoesNotFlushSuspendedUndoBatch()
        => AssertPerformAutoSaveDoesNotFlushSuspendedUndoBatch(new Project());

    [Fact]
    public void PerformAutoSave_LoadedWithEmptyString_DoesNotFlushSuspendedUndoBatch()
        => AssertPerformAutoSaveDoesNotFlushSuspendedUndoBatch(Project.ReadFromFile("", new(), false));

    [Fact]
    public void AutosaveInDotYafcDirectory_DoesNotModifyDirectory()
        // Use Path.Combine to generate host-native path separators. (GenerateAutosavePath coincidentally converts separators.)
        => Assert.Equal(Path.Combine("home", ".yafc", "ProjectName-autosave-1.yafc"), Project.GenerateAutosavePath("home/.yafc/ProjectName.yafc", 1));

    [Fact]
    public void AutosaveInDotYafcDirectoryWithNoExtension_DoesNotModifyDirectory()
        // Use Path.Combine to generate host-native path separators. (GenerateAutosavePath coincidentally converts separators.)
        => Assert.Equal(Path.Combine("home", ".yafc", "ProjectName-autosave-4.yafc"), Project.GenerateAutosavePath("home/.yafc/ProjectName", 4));

    private static string CreateTestProjectPath() {
        string directory = Path.Combine(AppContext.BaseDirectory, "ProjectTestFiles");
        _ = Directory.CreateDirectory(directory);
        return Path.Combine(directory, Guid.NewGuid() + ".yafc");
    }

    private static void DeleteProjectAndAutosaves(string path) {
        File.Delete(path);
        for (int i = 1; i <= 5; i++) {
            File.Delete(Project.GenerateAutosavePath(path, i));
        }
    }

    private static void AssertPerformAutoSaveDoesNotFlushSuspendedUndoBatch(Project project) {
        project.undo.Suspend();

        project.preferences.RecordUndo().time = 60;

        Assert.True(project.undo.HasChangesPending(project.preferences));
        Assert.False(project.undo.CanUndo);

        project.PerformAutoSave();

        Assert.True(project.undo.HasChangesPending(project.preferences));
        Assert.False(project.undo.CanUndo);

        project.preferences.RecordUndo().time = 3600;
        project.undo.Resume();

        Assert.True(project.undo.CanUndo);
    }
}
