using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace Yafc.Model.Tests;

public class UndoSystemTests {
    [Fact]
    public void ImmediateScheduler_FlushCommitsAfterMutation() {
        var project = new Project(new ImmediateUndoBatchScheduler());
        float changedValue = -1f;
        project.settings.changed += _ => changedValue = project.settings.miningProductivity;

        project.settings.RecordUndo().miningProductivity = 1.25f;
        project.undo.FlushPendingChanges();

        Assert.Equal(1.25f, changedValue);
        Assert.True(project.undo.CanUndo);
    }

    [Fact]
    public void ImmediateScheduler_CanUndoFlushesAfterMutation() {
        var project = new Project(new ImmediateUndoBatchScheduler());

        project.RecordUndo().yafcVersion = "immediate";

        Assert.True(project.undo.CanUndo);
        Assert.False(project.undo.HasChangesPending(project));
    }

    [Fact]
    public void ScheduledCommit_CompletesPendingBatch() {
        var scheduler = new DeferredUndoBatchScheduler();
        var project = new Project(scheduler);

        project.RecordUndo().yafcVersion = "scheduled";

        Assert.False(project.undo.CanUndo);
        Assert.True(project.undo.HasChangesPending(project));

        scheduler.RunNext();

        Assert.True(project.undo.CanUndo);
        Assert.False(project.undo.HasChangesPending(project));

        project.undo.PerformUndo();
        Assert.Null(project.yafcVersion);
    }

    private sealed class DeferredUndoBatchScheduler : IUndoBatchScheduler {
        private readonly Queue<Action> callbacks = [];

        public int PendingCount => callbacks.Count;

        public void ScheduleOnGestureFinish(SendOrPostCallback callback, object state)
            => callbacks.Enqueue(() => callback(state));

        public void RunNext() => callbacks.Dequeue()();
    }
}
