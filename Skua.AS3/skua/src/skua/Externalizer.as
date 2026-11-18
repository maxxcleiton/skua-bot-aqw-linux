package skua {
import flash.events.Event;
import flash.events.IOErrorEvent;
import flash.events.ProgressEvent;
import flash.events.SecurityErrorEvent;
import flash.external.ExternalInterface;
import flash.net.Socket;
import flash.net.URLLoader;
import flash.net.URLRequest;
import flash.utils.ByteArray;

import skua.api.Auras;
import skua.api.Combat;
import skua.api.Inventory;
import skua.api.Monster;
import skua.api.Player;
import skua.api.Server;
import skua.api.Shop;
import skua.api.Skills;
import skua.api.World;
import skua.module.Modules;
import skua.remote.RemoteRegistry;

public class Externalizer {

    private var _callbacks:Object = {};
    private var _socket:Socket;
    private var _socketConnected:Boolean = false;
    private var _useSocket:Boolean = false;
    private var _readBuffer:ByteArray = new ByteArray();
    private var _pendingMessages:Array = [];
    private var _portLoader:URLLoader;

    public function Externalizer() {
        super();
    }

    public function init(root:Main):void {
        // Core initialization
        this.addCallback("loadClient", Main.loadGame);
        this.addCallback("setBackgroundValues", Main.setBackgroundValues);
        this.addCallback("isTrue", Main.isTrue);
        this.addCallback("isNull", Main.isNull);
        this.addCallback("setFrameRate", Main.setFrameRate);
        this.addCallback("setStageQuality", Main.setStageQuality);
        this.addCallback("setShowFPS", Main.setShowFPS);

        // Game object access
        this.addCallback("getGameObject", Main.getGameObject);
        this.addCallback("getGameObjectS", Main.getGameObjectS);
        this.addCallback("getGameObjectKey", Main.getGameObjectKey);
        this.addCallback("setGameObject", Main.setGameObject);
        this.addCallback("setGameObjectKey", Main.setGameObjectKey);
        this.addCallback("getArrayObject", Main.getArrayObject);
        this.addCallback("setArrayObject", Main.setArrayObject);
        this.addCallback("callGameFunction", Main.callGameFunction);
        this.addCallback("callGameFunction0", Main.callGameFunction0);
        this.addCallback("selectArrayObjects", Main.selectArrayObjects);

        // Server
        this.addCallback("connectToServer", Server.connectToServer);
        this.addCallback("clickServer", Server.clickServer);
        this.addCallback("autoLogin", Server.autoLogin);

        // Player
        this.addCallback("isLoggedIn", Player.isLoggedIn);
        this.addCallback("isKicked", Player.isKicked);
        this.addCallback("walkTo", Player.walkTo);
        this.addCallback("untargetSelf", Player.untargetSelf);
        this.addCallback("attackPlayer", Player.attackPlayer);
        this.addCallback("getAvatar", Player.getAvatar);
        this.addCallback("getLoadouts", Player.getLoadouts);
        this.addCallback("gender", Player.Gender);
        this.addCallback("rejectExcept", Player.rejectExcept);


        // World
        this.addCallback("jumpCorrectRoom", World.jumpCorrectRoom);
        this.addCallback("disableDeathAd", World.disableDeathAd);
        this.addCallback("skipCutscenes", World.skipCutscenes);
        this.addCallback("hidePlayers", World.hidePlayers);
        this.addCallback("disableFX", World.disableFX);
        this.addCallback("killLag", World.killLag);

        // Monster
        this.addCallback("availableMonsters", Monster.availableMonstersInCell);
        this.addCallback("getMonsters", Monster.getMonsters);
        this.addCallback("getTargetMonster", Monster.getTargetMonster);
        this.addCallback("attackMonsterName", Monster.attackMonsterByName);
        this.addCallback("attackMonsterID", Monster.attackMonsterByID);
        this.addCallback("selectMonsterName", Monster.selectMonsterByName);
        this.addCallback("selectMonsterID", Monster.selectMonsterByID);

        // Skills
        this.addCallback("canUseSkill", Skills.canUseSkill);
        this.addCallback("skillCooldownRemaining", Skills.skillCooldownRemaining);
        this.addCallback("globalCooldownRemaining", Skills.globalCooldownRemaining);
        this.addCallback("useSkill", Skills.useSkill);

        // Combat
        this.addCallback("infiniteRange", Combat.infiniteRange);
        this.addCallback("magnetize", Combat.magnetize);

        // Inventory
        this.addCallback("hasInventoryCategories", Inventory.hasInventoryCategories);
        this.addCallback("inventoryBagUsedSlots", Inventory.inventoryBagUsedSlots);
        this.addCallback("inventoryMiscSlots", Inventory.inventoryMiscSlots);
        this.addCallback("inventoryMiscUsedSlots", Inventory.inventoryMiscUsedSlots);
        this.addCallback("isFavoriteItem", Inventory.isFavoriteItem);
        this.addCallback("inventoryPool", Inventory.inventoryPool);
        this.addCallback("inventoryHasSpaceFor", Inventory.inventoryHasSpaceFor);

        // Shop
        this.addCallback("buyItemByName", Shop.buyItemByName);
        this.addCallback("buyItemByID", Shop.buyItemByID);
        this.addCallback("getShopItem", Shop.getShopItem);
        this.addCallback("getShopItemByID", Shop.getShopItemByID);
        this.addCallback("getUnmetPurchaseRequirements", Shop.getUnmetPurchaseRequirements);

        // Auras
        this.addCallback("getSubjectAuras", Auras.getSubjectAuras);
        this.addCallback("GetAuraSnapshots", Auras.GetAuraSnapshots);
        this.addCallback("GetAuraRuleStacks", Auras.GetAuraRuleStacks);
        this.addCallback("HasAnyActiveAura", Auras.HasAnyActiveAura);
        this.addCallback("GetPlayerAura", Auras.GetPlayerAura);
        this.addCallback("GetMonsterAuraByName", Auras.GetMonsterAuraByName);
        this.addCallback("GetMonsterAuraByID", Auras.GetMonsterAuraByID);
        this.addCallback("rebuildAuraArray", Auras.rebuildAuraArray);
        this.addCallback("GetAurasValue", Auras.GetAurasValue);
        this.addCallback("rebuilduoTree", Auras.rebuilduoTree);
        this.addCallback("rebuildmonTree", Auras.rebuildmonTree);

        // Packets
        this.addCallback("sendClientPacket", Main.sendClientPacket);
        this.addCallback("catchPackets", Main.catchPackets);

        // Utilities
        this.addCallback("injectScript", Main.injectScript);

        // Remote Registry
        this.addCallback("lnkCreate", RemoteRegistry.ext_create);
        this.addCallback("lnkDestroy", RemoteRegistry.ext_destroy);
        this.addCallback("lnkGetChild", RemoteRegistry.ext_getChild);
        this.addCallback("lnkDeleteChild", RemoteRegistry.ext_deleteChild);
        this.addCallback("lnkGetValue", RemoteRegistry.ext_getValue);
        this.addCallback("lnkSetValue", RemoteRegistry.ext_setValue);
        this.addCallback("lnkCall", RemoteRegistry.ext_call);
        this.addCallback("lnkGetArray", RemoteRegistry.ext_getArray);
        this.addCallback("lnkSetArray", RemoteRegistry.ext_setArray);

        // Function Call Registry
        this.addCallback("fcCreate", RemoteRegistry.ext_fcCreate);
        this.addCallback("fcPush", RemoteRegistry.ext_fcPushArgs);
        this.addCallback("fcPushDirect", RemoteRegistry.ext_fcPushArgsDirect);
        this.addCallback("fcClear", RemoteRegistry.ext_fcClearArgs);
        this.addCallback("fcCallFlash", RemoteRegistry.ext_fcCallFlash);
        this.addCallback("fcCall", RemoteRegistry.ext_fcCall);

        // Modules
        this.addCallback("modEnable", Modules.enable);
        this.addCallback("modDisable", Modules.disable);

        this.initBridge(root);
    }

    private function initBridge(root:Main):void {
        var hasExternal:Boolean = false;
        try {
            hasExternal = ExternalInterface.available;
        } catch (e:Error) {
            hasExternal = false;
        }

        if (hasExternal) {
            this.debug("Externalizer::init done.");
            this.call("requestLoadGame");
            return;
        }

        _useSocket = true;
        var paramPort:String = null;
        try {
            if (root && root.loaderInfo && root.loaderInfo.parameters && root.loaderInfo.parameters["port"]) {
                paramPort = String(root.loaderInfo.parameters["port"]);
            }
        } catch (e:Error) {}

        if (paramPort != null && int(paramPort) > 0) {
            connectSocket(int(paramPort));
            return;
        }

        _portLoader = new URLLoader();
        _portLoader.addEventListener(Event.COMPLETE, function(e:Event):void {
            var p:int = int(String(_portLoader.data).replace(/^\s+|\s+$/g, ""));
            connectSocket(p > 0 ? p : 41523);
        });
        _portLoader.addEventListener(IOErrorEvent.IO_ERROR, function(e:IOErrorEvent):void {
            connectSocket(41523);
        });
        _portLoader.addEventListener(SecurityErrorEvent.SECURITY_ERROR, function(e:SecurityErrorEvent):void {
            connectSocket(41523);
        });
        try {
            _portLoader.load(new URLRequest("skua_bridge.port"));
        } catch (e:Error) {
            connectSocket(41523);
        }
    }

    private function connectSocket(port:int):void {
        _socket = new Socket();
        _socket.addEventListener(Event.CONNECT, onSocketConnect);
        _socket.addEventListener(ProgressEvent.SOCKET_DATA, onSocketData);
        _socket.addEventListener(IOErrorEvent.IO_ERROR, onSocketError);
        _socket.addEventListener(SecurityErrorEvent.SECURITY_ERROR, onSocketSecurityError);
        _socket.connect("127.0.0.1", port);
    }

    private function onSocketConnect(e:Event):void {
        _socketConnected = true;
        while (_pendingMessages.length > 0) {
            var msg:String = _pendingMessages.shift();
            sendRawSocket(msg);
        }
        this.debug("Externalizer::init done (Socket Bridge).");
        this.call("requestLoadGame");
    }

    private function onSocketError(e:IOErrorEvent):void {
    }

    private function onSocketSecurityError(e:SecurityErrorEvent):void {
    }

    private function sendRawSocket(xmlPayload:String):void {
        if (!_socket || !_socketConnected) {
            _pendingMessages.push(xmlPayload);
            return;
        }
        try {
            _socket.writeUTFBytes(xmlPayload);
            _socket.writeByte(0);
            _socket.flush();
        } catch (e:Error) {}
    }

    private function onSocketData(e:ProgressEvent):void {
        try {
            while (_socket.bytesAvailable > 0) {
                var b:int = _socket.readByte();
                if (b == 0) {
                    _readBuffer.position = 0;
                    var frame:String = _readBuffer.readUTFBytes(_readBuffer.length);
                    _readBuffer.clear();
                    if (frame.length > 0) {
                        handleSocketFrame(frame);
                    }
                } else {
                    _readBuffer.writeByte(b);
                }
            }
        } catch (err:Error) {}
    }

    private function handleSocketFrame(frame:String):void {
        var prevIgnoreWhitespace:Boolean = XML.ignoreWhitespace;
        XML.ignoreWhitespace = false;
        try {
            var xml:XML = new XML(frame);
            var reqId:String = String(xml.@id);
            var invokeNodes:XMLList = xml.elements("invoke");
            var invokeXml:XML = invokeNodes.length() > 0 ? invokeNodes[0] : xml;
            var funcName:String = String(invokeXml.@name);
            var args:Array = [];
            var argRootList:XMLList = invokeXml.elements("arguments");
            if (argRootList.length() > 0) {
                var children:XMLList = argRootList[0].children();
                for each (var argChild:XML in children) {
                    if (argChild.nodeKind() == "element") {
                        args.push(fromFlashXml(argChild));
                    }
                }
            }
            var func:Function = _callbacks[funcName] as Function;
            var result:* = undefined;
            if (func != null) {
                result = func.apply(null, args);
            }
            var resultXml:String = toFlashXml(result);
            sendRawSocket('<res id="' + escapeXmlAttr(reqId) + '">' + resultXml + '</res>');
        } catch (err:Error) {
            sendRawSocket('<res id="0"><null/></res>');
        } finally {
            XML.ignoreWhitespace = prevIgnoreWhitespace;
        }
    }

    private static function fromFlashXml(el:XML):* {
        var name:String = el.localName();
        switch (name) {
            case "null":
            case "undefined":
                return null;
            case "true":
                return true;
            case "false":
                return false;
            case "number":
                var numStr:String = el.text().toString();
                return numStr.indexOf(".") >= 0 ? Number(numStr) : int(numStr);
            case "string":
                return el.text().toString();
            case "array":
                var arr:Array = [];
                for each (var prop:XML in el.elements("property")) {
                    var propChildren:XMLList = prop.children();
                    for each (var pc:XML in propChildren) {
                        if (pc.nodeKind() == "element") {
                            arr.push(fromFlashXml(pc));
                            break;
                        }
                    }
                }
                return arr;
            case "object":
                var obj:Object = {};
                for each (var oprop:XML in el.elements("property")) {
                    var key:String = String(oprop.@id);
                    for each (var oc:XML in oprop.children()) {
                        if (oc.nodeKind() == "element") {
                            obj[key] = fromFlashXml(oc);
                            break;
                        }
                    }
                }
                return obj;
            default:
                return el.text().toString();
        }
    }

    private static function toFlashXml(val:*):String {
        if (val === undefined || val === null) {
            return "<null/>";
        }
        if (val is Boolean) {
            return (val as Boolean) ? "<string>true</string>" : "<string>false</string>";
        }
        if (val is int || val is uint || val is Number) {
            return "<number>" + String(val) + "</number>";
        }
        if (val is Array) {
            var arrSb:String = "<array>";
            var arr:Array = val as Array;
            for (var i:int = 0; i < arr.length; i++) {
                arrSb += '<property id="' + i + '">' + toFlashXml(arr[i]) + '</property>';
            }
            return arrSb + "</array>";
        }
        return "<string>" + escapeXmlText(String(val)) + "</string>";
    }

    private static function escapeXmlText(s:String):String {
        if (s == null || s.length == 0) return "";
        if (s.indexOf("&") == -1 && s.indexOf("<") == -1 && s.indexOf(">") == -1) return s;
        return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    }

    private static function escapeXmlAttr(s:String):String {
        if (s == null || s.length == 0) return "";
        var esc:String = escapeXmlText(s);
        return esc.indexOf('"') == -1 ? esc : esc.replace(/"/g, "&quot;");
    }

    public function addCallback(name:String, func:Function):void {
        _callbacks[name] = func;
        try {
            if (ExternalInterface.available) {
                ExternalInterface.addCallback(name, func);
            }
        } catch (e:Error) {}
    }

    public function call(name:String, ...rest):* {
        if (_useSocket) {
            var xml:String = '<call><invoke name="' + escapeXmlAttr(name) + '" returntype="xml">';
            if (rest != null && rest.length > 0) {
                xml += "<arguments>";
                for (var i:int = 0; i < rest.length; i++) {
                    xml += toFlashXml(rest[i]);
                }
                xml += "</arguments>";
            }
            xml += "</invoke></call>";
            sendRawSocket(xml);
            return null;
        }
        try {
            if (ExternalInterface.available) {
                return ExternalInterface.call(name, rest);
            }
        } catch (e:Error) {}
        return null;
    }

    public function debug(message:String):void {
        this.call("debug", message);
    }
}
}
