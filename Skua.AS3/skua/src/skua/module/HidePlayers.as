package skua.module {
import flash.display.DisplayObject;
import flash.display.DisplayObjectContainer;
import flash.display.MovieClip;

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
                if (avatar.pMC.visible != targetVis)
                    avatar.pMC.visible = targetVis;
                if (avatar.pMC.mcChar && avatar.pMC.mcChar.visible != targetVis)
                    avatar.pMC.mcChar.visible = targetVis;
                if (avatar.pMC.pname && avatar.pMC.pname.visible != targetVis)
                    avatar.pMC.pname.visible = targetVis;
                if (avatar.pMC.shadow && avatar.pMC.shadow.visible != targetVis)
                    avatar.pMC.shadow.visible = targetVis;
                if (avatar.petMC && avatar.petMC.visible != targetVis)
                    avatar.petMC.visible = targetVis;

                if (enabled && avatar.pMC.mcChar is DisplayObjectContainer) {
                    stopClips(avatar.pMC.mcChar as DisplayObjectContainer, 0);
                }
            }
        }
    }

    override public function onFrame(game:*):void {
        if (++_frameTick < 8) return;
        _frameTick = 0;
        onToggle(game);
    }

    private function stopClips(container:DisplayObjectContainer, depth:int):void {
        if (!container || depth > 4) return;
        if (container is MovieClip) {
            var mc:MovieClip = container as MovieClip;
            if (mc.isPlaying) mc.stop();
        }
        var n:int = container.numChildren;
        for (var i:int = 0; i < n; i++) {
            var child:DisplayObject = container.getChildAt(i);
            if (child is DisplayObjectContainer) {
                stopClips(child as DisplayObjectContainer, depth + 1);
            }
        }
    }
}
}
