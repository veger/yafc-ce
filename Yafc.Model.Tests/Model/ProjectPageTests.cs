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
        Assert.Equal(2, switcher.modelThreadSwitches);
    }

    [Fact]
    public async Task ExternalSolve_ModelThreadContinuationRunsInsideSwitcherDispatch() {
        Project project = new();
        GuardedModelThreadSwitcher switcher = new();
        project.modelThreadSwitcher = switcher;
        ProjectPage page = new(project, typeof(Summary));

        _ = await page.ExternalSolve();

        Assert.Equal(2, switcher.modelThreadSwitches);
        Assert.False(page.IsSolutionStale());
    }

    private sealed class CountingModelThreadSwitcher : IModelThreadSwitcher {
        public int backgroundSwitches { get; private set; }
        public int modelThreadSwitches { get; private set; }

        public ModelThreadSwitch SwitchToBackground() {
            backgroundSwitches++;
            return default;
        }

        public ModelThreadSwitch SwitchToModelThread() {
            modelThreadSwitches++;
            return default;
        }
    }

    private sealed class GuardedModelThreadSwitcher : IModelThreadSwitcher {
        private bool inModelThreadDispatch;

        public int modelThreadSwitches { get; private set; }

        public ModelThreadSwitch SwitchToBackground() => default;

        public ModelThreadSwitch SwitchToModelThread() {
            modelThreadSwitches++;
            return new ModelThreadSwitch(new GuardedAwaitable(this));
        }

        private sealed class GuardedAwaitable(GuardedModelThreadSwitcher owner) : IModelThreadSwitchAwaitable, IModelThreadSwitchAwaiter {
            public IModelThreadSwitchAwaiter GetAwaiter() => this;
            public bool IsCompleted => false;

            public void GetResult() {
                if (!owner.inModelThreadDispatch) {
                    throw new InvalidOperationException("Continuation did not run inside the model thread dispatch.");
                }
            }

            public void OnCompleted(Action continuation) {
                owner.inModelThreadDispatch = true;
                try {
                    continuation();
                }
                finally {
                    owner.inModelThreadDispatch = false;
                }
            }
        }
    }
}
