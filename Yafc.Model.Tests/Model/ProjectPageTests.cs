using System;
using System.Threading.Tasks;
using Xunit;

namespace Yafc.Model.Tests;

public class ProjectPageTests {
    [Fact]
    public async Task ExternalSolve_DefaultThreadSwitcher_CompletesWithoutUiInitialization() {
        ProjectPage page = new(new Project(), typeof(Summary));

        var error = await page.ExternalSolve();

        Assert.Null(error);
        Assert.False(page.IsSolutionStale());
    }

    [Fact]
    public async Task ExternalSolve_UsesProjectThreadSwitcher() {
        Project project = new();
        CountingModelThreadSwitcher switcher = new();
        project.modelThreadSwitcher = switcher;
        ProjectPage page = new(project, typeof(Summary));

        _ = await page.ExternalSolve();

        Assert.Equal(0, switcher.backgroundSwitches);
        Assert.Equal(2, switcher.foregroundSwitches);
    }

    [Fact]
    public async Task ExternalSolve_ForegroundContinuationRunsInsideSwitcherDispatch() {
        Project project = new();
        GuardedModelThreadSwitcher switcher = new();
        project.modelThreadSwitcher = switcher;
        ProjectPage page = new(project, typeof(Summary));

        _ = await page.ExternalSolve();

        Assert.Equal(2, switcher.foregroundSwitches);
        Assert.False(page.IsSolutionStale());
    }

    private sealed class CountingModelThreadSwitcher : IModelThreadSwitcher {
        public int backgroundSwitches { get; private set; }
        public int foregroundSwitches { get; private set; }

        public ModelThreadSwitch SwitchToBackground() {
            backgroundSwitches++;
            return default;
        }

        public ModelThreadSwitch SwitchToForeground() {
            foregroundSwitches++;
            return default;
        }
    }

    private sealed class GuardedModelThreadSwitcher : IModelThreadSwitcher {
        private bool inForegroundDispatch;

        public int foregroundSwitches { get; private set; }

        public ModelThreadSwitch SwitchToBackground() => default;

        public ModelThreadSwitch SwitchToForeground() {
            foregroundSwitches++;
            return new ModelThreadSwitch(new GuardedAwaitable(this));
        }

        private sealed class GuardedAwaitable(GuardedModelThreadSwitcher owner) : IModelThreadSwitchAwaitable, IModelThreadSwitchAwaiter {
            public IModelThreadSwitchAwaiter GetAwaiter() => this;
            public bool IsCompleted => false;

            public void GetResult() {
                if (!owner.inForegroundDispatch) {
                    throw new InvalidOperationException("Continuation did not run inside the foreground dispatch.");
                }
            }

            public void OnCompleted(Action continuation) {
                owner.inForegroundDispatch = true;
                try {
                    continuation();
                }
                finally {
                    owner.inForegroundDispatch = false;
                }
            }
        }
    }
}
