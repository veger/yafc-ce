using System.Collections.Generic;

namespace Yafc.Model;

public class ProjectModuleTemplate : ModelObject<Project> {
    public ProjectModuleTemplate(Project owner, string name) : base(owner) {
        template = new ModuleTemplateBuilder().Build(this);
        this.name = name;
    }

    public ModuleTemplate template { get; set; }
    public FactorioObject? icon { get; set; }
    public string name { get; set; }
    public List<Entity> filterEntities { get; } = [];
    /// <summary>
    /// If <see langword="true"/>, this template is a candidate for being applied to newly added recipe rows.
    /// </summary>
    public bool autoApplyToNewRows { get; set; }
    /// <summary>
    /// If this and <see cref="autoApplyToNewRows"/> are both <see langword="true"/>, this template is a candidate for being applied to newly
    /// added recipe rows, even if it contains modules that are not compatible with that row (e.g. prod modules in a non-prod recipe)
    /// </summary>
    public bool autoApplyIfIncompatible { get; set; }
}
