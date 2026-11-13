package com.sunakle.larpbridge;
import com.google.gson.JsonObject;
public final class SkinBridgeTests {
    private static int tests;
    private static void check(boolean condition){if(!condition)throw new AssertionError("Check "+tests+" failed");tests++;}
    public static void main(String[] args){
        JsonObject config=new JsonObject();
        config.addProperty("serverMode","name");config.addProperty("serverValue","Player");
        check(SkinBridge.serverCommand(config).isEmpty());
        config.addProperty("serverAutoApply",true);
        check(SkinBridge.serverCommand(config).equals("skin Player"));
        config.addProperty("serverValue","Player\nop somebody");check(SkinBridge.serverCommand(config).isEmpty());
        config.addProperty("serverMode","url");config.addProperty("serverValue","https://textures.minecraft.net/texture/example");
        check(SkinBridge.serverCommand(config).endsWith("\" classic"));
        config.addProperty("slim",true);check(SkinBridge.serverCommand(config).endsWith("\" slim"));
        config.addProperty("serverValue","http://example.com/skin.png");check(SkinBridge.serverCommand(config).isEmpty());
        config.addProperty("serverValue","https://name:secret@example.com/skin.png");check(SkinBridge.serverCommand(config).isEmpty());
        config.addProperty("serverValue","https://example.com/skin\"\n/op a");check(SkinBridge.serverCommand(config).isEmpty());
        config.addProperty("serverValue","skinsrestorer-axolotl://test-token");check(SkinBridge.serverCommand(config).equals("skin url \"skinsrestorer-axolotl://test-token\" slim"));
        config.addProperty("serverValue","skinsrestorer-axolotl://");check(SkinBridge.serverCommand(config).isEmpty());
        config.addProperty("serverMode","off");check(SkinBridge.serverCommand(config).isEmpty());
        System.out.println("PASS: "+tests+" bridge checks");
    }
}
