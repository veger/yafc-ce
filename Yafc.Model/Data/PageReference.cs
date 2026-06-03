using System;

namespace Yafc.Model;

public sealed class PageReference(Guid guid) {
    public PageReference(ProjectPage page) : this(page.guid) => this.page = page;

    public Guid guid { get; } = guid;

    public ProjectPage? page {
        get {
            if (field == null) {
                field = Project.current.FindPage(guid);
            }
            else if (field.deleted) {
                return null;
            }

            return field;
        }
    }
}
