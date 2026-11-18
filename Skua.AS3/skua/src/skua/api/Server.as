package skua.api {
import flash.display.MovieClip;
import flash.events.Event;
import flash.events.MouseEvent;
import flash.events.TimerEvent;
import flash.utils.Timer;

import skua.Main;
import skua.module.ModalMC;

public class Server {

    public function Server() {
        super();
    }

    public static function autoLogin(username:String, password:String, targetServer:String):String {
        var state:int = 0;
        var attempts:int = 0;
        var maxAttempts:int = 200; // 40 seconds (200 * 200ms)

        var loginTimer:Timer = new Timer(200, maxAttempts);
        loginTimer.addEventListener(TimerEvent.TIMER, function(e:TimerEvent):void {
            attempts++;
            var g:* = (Main.instance != null) ? Main.instance.game : null;
            if (g == null) return;

            // If already in game, finish!
            if (g.world != null && g.world.myAvatar != null && g.sfc != null && g.sfc.isConnected) {
                loginTimer.stop();
                return;
            }

            if (state == 0) {
                if (g.mcLogin != null && g.mcLogin.sl != null && g.mcLogin.sl.iList != null && g.mcLogin.sl.iList.numChildren > 0) {
                    state = 1;
                } else if (g.mcLogin != null && g.mcLogin.ni != null && g.mcLogin.pi != null && g.mcLogin.btnLogin != null && g.mcLogin.visible) {
                    try {
                        g.mcLogin.ni.text = username;
                        g.mcLogin.pi.text = password;
                    } catch (err:Error) {}
                    try {
                        g.login(username.toLowerCase(), password);
                    } catch (err:Error) {}
                    state = 1;
                }
            } else if (state == 1) {
                // Wait for servers screen
                if (g.mcLogin != null && g.mcLogin.sl != null && g.mcLogin.sl.iList != null && g.mcLogin.sl.iList.numChildren > 0) {
                    var source:* = g.mcLogin.sl.iList;
                    var clicked:Boolean = false;
                    for (var i:int = 0; i < source.numChildren; i++) {
                        var child:* = source.getChildAt(i);
                        if (child != null && child.tName != null && child.tName.ti != null && child.tName.ti.text != null) {
                            var sName:String = child.tName.ti.text;
                            if (sName.toLowerCase().indexOf(targetServer.toLowerCase()) > -1) {
                                child.dispatchEvent(new MouseEvent(MouseEvent.CLICK));
                                clicked = true;
                                break;
                            }
                        }
                    }
                    if (!clicked && attempts > 50 && source.numChildren > 0) {
                        // Fallback: click first server if target not found after 10s
                        var fallback:* = source.getChildAt(0);
                        if (fallback != null) {
                            fallback.dispatchEvent(new MouseEvent(MouseEvent.CLICK));
                            clicked = true;
                        }
                    }
                    if (clicked) {
                        state = 2;
                    }
                } else if (g.objLogin != null && g.objLogin.bSuccess == 0) {
                    loginTimer.stop();
                }
            } else if (state == 2) {
                if (g.world != null && g.world.myAvatar != null && g.sfc != null && g.sfc.isConnected) {
                    loginTimer.stop();
                }
            }
        });
        loginTimer.start();
        return "true";
    }

    public static function clickServer(serverName:String):String {
        if (Main.instance == null || Main.instance.game == null || Main.instance.game.mcLogin == null || Main.instance.game.mcLogin.sl == null) {
            return false.toString();
        }
        var source:* = Main.instance.game.mcLogin.sl.iList;
        if (source == null) return false.toString();
        for (var i:int = 0; i < source.numChildren; i++) {
            var child:* = source.getChildAt(i);

            if (child != null && child.tName != null && child.tName.ti != null && child.tName.ti.text != null) {
                if (child.tName.ti.text.toLowerCase().indexOf(serverName.toLowerCase()) > -1) {
                    child.dispatchEvent(new MouseEvent(MouseEvent.CLICK));
                    return true.toString();
                }
            }
        }
        return false.toString();
    }

    public static function connectToServer(server:String):String {
        var serverData:Object = JSON.parse(server);
        var objLogin:Object = null;

        var connectionServerTimer:Timer = new Timer(500, 50);
        connectionServerTimer.addEventListener(TimerEvent.TIMER, connectingServer);
        connectionServerTimer.start();

        function connectingServer(e:Event):void {
            var g:* = (Main.instance != null) ? Main.instance.game : null;
            if (g != null && g.objLogin != null) {
                objLogin = g.objLogin;
            } else {
                try {
                    var s:String = Main.getGameObject("objLogin");
                    if (s && s != "null" && s != "undefined") {
                        objLogin = JSON.parse(s);
                    }
                } catch (err:Error) {}
            }

            if (objLogin != null) {
                connectServer(serverData, objLogin);
                connectionServerTimer.stop();
                connectionServerTimer.removeEventListener(TimerEvent.TIMER, connectingServer);
            }
        }

        return true.toString();
    }

    private static function connectServer(server:Object, objLoginData:Object):* {
        Main.instance.game.showTracking("4");
        if (Main.instance.game.serialCmdMode) {
            return;
        }

        if (server.bOnline == 0) {
            Main.instance.game.MsgBox.notify("Server currently offline!");
        } else if (server.iCount >= server.iMax) {
            Main.instance.game.MsgBox.notify("Server is Full!");
        } else if (server.iChat > 0 && objLoginData.bCCOnly == 1) {
            Main.instance.game.MsgBox.notify("Account Restricted to Moglin Sage Server Only.");
        } else if (server.iChat > 0 && objLoginData.iAge < 13 && objLoginData.iUpgDays < 0) {
            Main.instance.game.MsgBox.notify("Ask your parent to upgrade your account in order to play on chat enabled servers.");
        } else if (server.bUpg == 1 && objLoginData.iUpgDays < 0) {
            showModal("Member Server! Do you want to upgrade your account to access this premium server now?", "white,medium", "dual");
        } else if (Number(server.iMax) % 2 > 0) {
            showModal("Testing Server! Do you want to switch to the testing game client?", "white,medium", "dual");
        } else if (server.iLevel > 0 && objLoginData.iEmailStatus <= 2) {
            showModal("This server requires a confirmed email address.", "red,medium", "mono");
        } else {
            Main.instance.game.objServerInfo = server;
            Main.instance.game.chatF.iChat = server.iChat;
            killLoginModals();
            Main.instance.game.connectTo(server.sIP, server.iPort);
        }
    }

    private static function showModal(message:String, glow:String, buttons:String):void {
        var modal:ModalMC = new ModalMC();
        var params:Object = {
            strBody: message,
            params: {},
            glow: glow,
            btns: buttons
        };
        Main.instance.game.mcLogin.ModalStack.addChild(modal);
        modal.init(params);
    }

    private static function killLoginModals():void {
        var modalStack:MovieClip = Main.instance.game.mcLogin.ModalStack;
        for (var i:int = 0; i < modalStack.numChildren; i++) {
            var modal:MovieClip = modalStack.getChildAt(i) as MovieClip;
            if (modal && "fClose" in modal) {
                modal.fClose();
            }
        }
    }
}
}
