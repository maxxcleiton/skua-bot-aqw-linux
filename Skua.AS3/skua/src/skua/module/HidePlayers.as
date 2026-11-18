package skua.module {
public class HidePlayers extends Module {
    private var _frameTick:int = 0;

    public function HidePlayers() {
        super("HidePlayers");
    }

    override public function onToggle(game:*):void {
        if (!game || !game.world) return;
        var avatars:* = game.world.avatars;
        var targetVis:Boolean = !enabled;
        for (var id:* in avatars) {
            var avatar:* = avatars[id];
            if (avatar && !avatar.isMyAvatar && avatar.pMC) {
                if (avatar.pMC.mcChar && avatar.pMC.mcChar.visible != targetVis)
                    avatar.pMC.mcChar.visible = targetVis;
                if (avatar.pMC.pname && avatar.pMC.pname.visible != targetVis)
                    avatar.pMC.pname.visible = targetVis;
                if (avatar.pMC.shadow && avatar.pMC.shadow.visible != targetVis)
                    avatar.pMC.shadow.visible = targetVis;
                if (avatar.petMC && avatar.petMC.visible != targetVis)
                    avatar.petMC.visible = targetVis;
            }
        }
    }

    override public function onFrame(game:*):void {
        if (++_frameTick < 6) return;
        _frameTick = 0;
        onToggle(game);
    }
}
}