package skua.module {
import flash.display.DisplayObject;
import flash.display.DisplayObjectContainer;
import flash.display.MovieClip;

public class FpsBoost extends Module {
    private var _frameTick:int = 0;
    private var _prefsApplied:Boolean = false;

    public function FpsBoost() {
        super("FpsBoost");
    }

    override public function onToggle(game:*):void {
        _frameTick = 0;
        _prefsApplied = false;
        if (!game) return;

        applyLitePrefs(game, enabled);

        if (!game.world || !game.world.avatars) return;
        var avatars:* = game.world.avatars;
        for (var id:* in avatars) {
            var avatar:* = avatars[id];
            if (!avatar || !avatar.pMC) continue;

            if (!avatar.isMyAvatar) {
                var mcChar:MovieClip = avatar.pMC.mcChar as MovieClip;
                if (enabled) {
                    optimizeOtherAvatar(avatar, mcChar);
                } else {
                    if (mcChar) {
                        mcChar.cacheAsBitmap = false;
                        resumeClips(mcChar, 0);
                    }
                    if (avatar.pMC.shadow) avatar.pMC.shadow.visible = true;
                    if (avatar.petMC) avatar.petMC.visible = true;
                }
            }
        }
    }

    override public function onFrame(game:*):void {
        if (++_frameTick < 15) return;
        _frameTick = 0;

        if (!game) return;
        if (!_prefsApplied) {
            applyLitePrefs(game, true);
        }

        var world:* = game.world;
        if (!world || !world.avatars) return;

        var avatars:* = world.avatars;
        for (var id:* in avatars) {
            var avatar:* = avatars[id];
            if (!avatar || !avatar.pMC) continue;

            if (avatar.isMyAvatar) {
                if (avatar.pMC.pname && avatar.pMC.pname.filters && avatar.pMC.pname.filters.length > 0) {
                    avatar.pMC.pname.filters = null;
                }
                continue;
            }

            var mcChar:MovieClip = avatar.pMC.mcChar as MovieClip;
            optimizeOtherAvatar(avatar, mcChar);
        }
    }

    private function applyLitePrefs(game:*, active:Boolean):void {
        try {
            if (!game.litePreference || !game.litePreference.data) return;
            var data:* = game.litePreference.data;
            data.bCachePlayers = active;
            data.bDisDmgStrobe = active;
            data.bDisHealBubble = active;
            data.bDisSkillAnim = active;
            data.bDisWepAnim = active;
            data.bDisGround = active;
            data.bDisAuraAnim = active;

            if (data.dOptions) {
                data.dOptions["animSelf"] = active;
                data.dOptions["wepSelf"] = active;
                data.dOptions["groundSelf"] = active;
                data.dOptions["auraAnimSelf"] = active;
            }
            _prefsApplied = true;
        } catch (e:Error) {}
    }

    private function optimizeOtherAvatar(avatar:*, mcChar:MovieClip):void {
        try {
            if (avatar.petMC && avatar.petMC.visible) {
                avatar.petMC.visible = false;
            }
            if (avatar.pMC.shadow && avatar.pMC.shadow.visible) {
                avatar.pMC.shadow.visible = false;
            }
            if ("cShadow" in avatar.pMC && avatar.pMC.cShadow && avatar.pMC.cShadow.visible) {
                avatar.pMC.cShadow.visible = false;
            }
            if (avatar.pMC.pname && avatar.pMC.pname.filters && avatar.pMC.pname.filters.length > 0) {
                avatar.pMC.pname.filters = null;
            }
            if (avatar.pMC.filters && avatar.pMC.filters.length > 0) {
                avatar.pMC.filters = null;
            }
            if (mcChar) {
                if (mcChar.filters && mcChar.filters.length > 0) {
                    mcChar.filters = null;
                }
                stopClips(mcChar, 0);
                if (!mcChar.cacheAsBitmap) {
                    mcChar.cacheAsBitmap = true;
                }
            }
        } catch (e:Error) {}
    }

    private function stopClips(container:DisplayObjectContainer, depth:int):void {
        if (!container || depth > 5) return;
        if (container is MovieClip) {
            var mc:MovieClip = container as MovieClip;
            if (mc.isPlaying) {
                mc.stop();
            }
        }
        var n:int = container.numChildren;
        for (var i:int = 0; i < n; i++) {
            var child:DisplayObject = container.getChildAt(i);
            if (child is DisplayObjectContainer) {
                stopClips(child as DisplayObjectContainer, depth + 1);
            }
        }
    }

    private function resumeClips(container:DisplayObjectContainer, depth:int):void {
        if (!container || depth > 4) return;
        if (container is MovieClip) {
            var mc:MovieClip = container as MovieClip;
            if (!mc.isPlaying && mc.totalFrames > 1) {
                mc.play();
            }
        }
        var n:int = container.numChildren;
        for (var i:int = 0; i < n; i++) {
            var child:DisplayObject = container.getChildAt(i);
            if (child is DisplayObjectContainer) {
                resumeClips(child as DisplayObjectContainer, depth + 1);
            }
        }
    }
}
}
