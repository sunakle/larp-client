package com.sunakle.larpbridge;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.larpclient.module.SkinChanger;
import com.larpclient.module.Setting;
import com.larpclient.module.ModeSetting;
import com.larpclient.module.TextSetting;
import java.nio.file.Files;
import java.nio.file.Path;
import java.net.URI;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayConnectionEvents;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.class_634;

/** Reads only explicit launcher choices. It never uploads a file or changes authentication. */
public final class SkinBridge implements ClientModInitializer {
    private final Path preferences=FabricLoader.getInstance().getGameDir().resolve("config/larp-client/launcher-skin.json");
    private long appliedRevision=Long.MIN_VALUE;
    private int ticks,joinWait;
    private class_634 pending;
    @Override public void onInitializeClient() {
        ClientPlayConnectionEvents.JOIN.register((handler,sender,client)->{pending=handler;joinWait=0;});
        ClientPlayConnectionEvents.DISCONNECT.register((handler,client)->{pending=null;});
        ClientTickEvents.END_CLIENT_TICK.register(client->{
            if(++ticks%20!=0)return;
            try {
                JsonObject config=read();
                if(config==null)return;
                applyLocal(config);
                if(pending!=null){
                    if(client.method_1562()!=pending||++joinWait>120){pending=null;return;}
                    String command=serverCommand(config);
                    if(command.isEmpty()){pending=null;return;}
                    // Wait for the server to advertise the command, including after /login.
                    if(pending.method_2886().getRoot().getChild("skin")!=null){
                        pending.method_45730(command);
                        System.out.println("[LARP Skin Bridge] Requested server skin; server permissions and acceptance still apply.");
                        pending=null;
                    }
                }
            } catch(Exception error) {
                System.err.println("[LARP Skin Bridge] "+error.getClass().getSimpleName()+": skin settings could not be applied.");
            }
        });
        System.out.println("[LARP Skin Bridge] Launcher skin integration ready.");
    }
    private JsonObject read() throws Exception {
        if(!Files.isRegularFile(preferences)||Files.size(preferences)>8192)return null;
        return JsonParser.parseString(Files.readString(preferences)).getAsJsonObject();
    }
    private static boolean bool(JsonObject c,String key){return c.has(key)&&c.get(key).getAsBoolean();}
    private static String text(JsonObject c,String key){return c.has(key)&&!c.get(key).isJsonNull()?c.get(key).getAsString():"";}
    private void applyLocal(JsonObject config) throws Exception {
        long revision=Files.getLastModifiedTime(preferences).toMillis();
        if(revision==appliedRevision)return;
        if(!bool(config,"hasLocalChoice")){appliedRevision=revision;return;}
        SkinChanger module=SkinChanger.pAsSkinChanger();
        if(module==null)return;
        if(!bool(config,"localEnabled")){module.setEnabled(false);appliedRevision=revision;return;}
        Path image=preferences.getParent().resolve("skins/launcher.png");
        if(!Files.isRegularFile(image))return;
        ModeSetting skin=null,model=null;
        for(Setting<?> setting:module.getSettings()){
            if(setting instanceof ModeSetting mode && setting.getName().equals("Skin"))skin=mode;
            if(setting instanceof ModeSetting mode && setting.getName().equals("Model"))model=mode;
            if(setting instanceof TextSetting text && setting.getName().equals("Copy Player"))text.setValue("");
        }
        if(skin==null||model==null)return;
        if(!skin.acceptsValue("launcher.png")){module.fAsVoid();return;}
        model.setValue(bool(config,"slim")?"Slim":"Wide");
        skin.setValue("launcher.png");
        module.setEnabled(true);
        module.KAsVoid();
        appliedRevision=revision;
        System.out.println("[LARP Skin Bridge] Local launcher skin applied to Skin Changer, including the title-screen preview.");
    }
    public static String serverCommand(JsonObject config) {
        if(!bool(config,"serverAutoApply"))return "";
        String mode=text(config,"serverMode"),value=text(config,"serverValue").trim();
        if(mode.equals("name"))return value.matches("[A-Za-z0-9_]{3,16}")?"skin "+value:"";
        if(!mode.equals("url")||value.length()>190||value.matches(".*[\\s\"\\\\].*"))return "";
        try {
            URI uri=new URI(value);
            boolean https="https".equals(uri.getScheme())&&uri.getHost()!=null&&uri.getUserInfo()==null;
            boolean opaque="skinsrestorer-axolotl".equals(uri.getScheme())&&value.startsWith("skinsrestorer-axolotl://")&&value.length()>24;
            if(!https&&!opaque)return "";
            return "skin url \""+value+"\" "+(bool(config,"slim")?"slim":"classic");
        }catch(Exception error){return "";}
    }
}
