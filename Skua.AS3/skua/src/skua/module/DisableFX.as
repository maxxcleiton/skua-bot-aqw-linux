package skua.module {
import flash.display.MovieClip;
import flash.utils.getQualifiedClassName;

public class DisableFX extends Module {

    private var _frameTick:int = 0;

    public function DisableFX() {
        super("DisableFX");
    }

    override public function onToggle(game:*):void {
        _frameTick = 0;
        var world:* = game.world;
        if (!world) return;
        var monsters:* = world.monsters;
        var currentFrame:String = world.strFrame;
        
        for (var mid:* in monsters) {
            var monster:* = monsters[mid];
            if (monster && monster.dataLeaf && monster.dataLeaf.strFrame == currentFrame && monster.pMC && monster.dataLeaf.intState > 0) {
                var mmc:MovieClip = monster.pMC as MovieClip;
                if (enabled && mmc.numChildren > 1) {
                    mmc = mmc.getChildAt(1) as MovieClip;
                }
                if (mmc) {
                    toggleAnims(mmc, !enabled);
                    if (enabled && mmc.currentLabel != "Idle") {
                        mmc.gotoAndStop("Idle");
                    }
                }
            }
        }

        for (var aid:* in world.avatars) {
            var avatar:* = world.avatars[aid];
            if (avatar && avatar.objData && avatar.pMC && avatar.pMC.mcChar) {
                if (avatar.pMC.mcChar.weapon && avatar.pMC.mcChar.weapon.mcWeapon) {
                    toggleAnims(avatar.pMC.mcChar.weapon.mcWeapon as MovieClip, !enabled);
                }
                if (avatar.pMC.mcChar.weaponOff && avatar.pMC.mcChar.weaponOff.numChildren > 0) {
                    toggleAnims(avatar.pMC.mcChar.weaponOff.getChildAt(0) as MovieClip, !enabled);
                }
            }
        }
    }

    override public function onFrame(game:*):void {
        if (++_frameTick < 10) return;
        _frameTick = 0;
        onToggle(game);
    }

    private function toggleAnims(root:MovieClip, enabled:Boolean):void {
        if (!root) return;
        toggleAnim(root, enabled);
        var count:int = root.numChildren;
        for (var i:int = 0; i < count; i++) {
            var child:* = root.getChildAt(i);
            if (child is MovieClip) {
                var mc:MovieClip = child as MovieClip;
                if (getQualifiedClassName(mc).indexOf("Display") == -1) {
                    try {
                        toggleAnims(mc, enabled);
                    } catch (e:Error) {

                    }
                }
            }
        }
    }

    private function toggleAnim(mc:MovieClip, enabled:Boolean):void {
        if (enabled) {
            if (!mc.isPlaying && mc.totalFrames > 1) {
                mc.gotoAndPlay(0);
            }
        } else {
            if (mc.isPlaying) {
                mc.gotoAndStop(0);
            }
        }
    }
}

}