namespace Yafc.Model;

public interface IModelThreadSwitcher {
    ModelThreadSwitch SwitchToBackground();
    ModelThreadSwitch SwitchToModelThread();
}
