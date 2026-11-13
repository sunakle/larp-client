using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class LauncherTests
{
    private static int count;
    private static void Check(bool value,string name){if(!value)throw new Exception("FAILED: "+name);count++;}
    private static void Reject(Action action,string name){try{action();}catch(InvalidDataException){count++;return;}throw new Exception("Expected rejection: "+name);}
    [STAThread] private static int Main()
    {
        try{
            Application.EnableVisualStyles();
            Check(MemoryBudget.MaximumFor(0)==4,"RAM API fallback");
            Check(MemoryBudget.MaximumFor(8192)==6,"8 GB machine reserves 2 GB");
            Check(MemoryBudget.MaximumFor(16384)==14,"16 GB supports above 12 GB");
            Check(MemoryBudget.MaximumFor(65536)==62,"64 GB machine");
            Check(MemoryBudget.MaximumGb>=2,"live memory detection");
            string root=Path.Combine(Path.GetTempPath(),"larp-launcher-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string valid=Path.Combine(root,"skin.png");
            using(var image=new Bitmap(64,64,PixelFormat.Format32bppArgb))using(var g=Graphics.FromImage(image)){
                g.Clear(Color.Transparent);using(var b=new SolidBrush(Color.Red))g.FillRectangle(b,8,8,8,8);
                using(var b=new SolidBrush(Color.Lime))g.FillRectangle(b,20,20,8,12);
                using(var b=new SolidBrush(Color.Blue)){g.FillRectangle(b,4,20,4,12);g.FillRectangle(b,20,52,4,12);}
                image.Save(valid,ImageFormat.Png);
            }
            using(var decoded=SkinPreferences.Decode(valid)){Check(decoded.Width==64&&decoded.Height==64,"valid modern PNG");}
            string legacy=Path.Combine(root,"legacy.png");using(var image=new Bitmap(64,32))image.Save(legacy,ImageFormat.Png);
            using(var decoded=SkinPreferences.Decode(legacy))Check(decoded.Height==32,"legacy PNG");
            string invalid=Path.Combine(root,"invalid.png");using(var image=new Bitmap(128,128))image.Save(invalid,ImageFormat.Png);
            Reject(delegate{using(var ignored=SkinPreferences.Decode(invalid)){};},"oversized dimensions");
            File.WriteAllText(invalid,"not an image");Reject(delegate{using(var ignored=SkinPreferences.Decode(invalid)){};},"fake PNG");
            File.WriteAllBytes(invalid,new byte[131073]);Reject(delegate{using(var ignored=SkinPreferences.Decode(invalid)){};},"oversized file");
            var prefs=new SkinPreferences{serverMode="name",serverValue="Player_1"};Check(prefs.ServerCommand()=="skin Player_1","server nickname");
            prefs.serverMode="url";prefs.serverValue="https://textures.minecraft.net/texture/example";prefs.slim=true;
            Check(prefs.ServerCommand()=="skin url \"https://textures.minecraft.net/texture/example\" slim","server URL and slim model");
            prefs.serverValue="skinsrestorer-axolotl://test-token";Check(prefs.ServerCommand().EndsWith("\" slim"),"SkinsRestorer opaque token");
            prefs.serverValue="skinsrestorer-axolotl://";Reject(delegate{prefs.ServerCommand();},"empty opaque token");
            prefs.serverValue="http://example.com/skin.png";Reject(delegate{prefs.ServerCommand();},"HTTP URL");
            prefs.serverValue="https://user:pass@example.com/skin.png";Reject(delegate{prefs.ServerCommand();},"URL credentials");
            prefs.serverValue="https://example.com/a\"\n/op";Reject(delegate{prefs.ServerCommand();},"command injection");
            prefs.serverMode="off";prefs.hasLocalChoice=true;prefs.localEnabled=true;prefs.slim=true;prefs.Save();
            var restored=SkinPreferences.Load();Check(restored.hasLocalChoice&&restored.localEnabled&&restored.slim,"skin preferences round trip");
            prefs.slim=false;prefs.Save();Check(!SkinPreferences.Load().slim,"atomic replacement of existing skin settings");
            using(var preview=new SkinPreview{Width=240,Height=285})using(var image=SkinPreferences.Decode(valid))using(var bitmap=new Bitmap(240,285)){
                preview.SetImage(image);preview.DrawToBitmap(bitmap,new Rectangle(0,0,240,285));
                int red=0,green=0;for(int y=0;y<285;y++)for(int x=0;x<240;x++){var p=bitmap.GetPixel(x,y);if(p.R>220&&p.G<20)red++;if(p.G>220&&p.R<20)green++;}
                Check(red>1000&&green>1000,"front skin UV preview");
            }
            using(var form=new Form{BackColor=Brand.Background,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000)})
            using(var button=new ModernButton{Width=300,Height=56,Primary=true,Text="ИГРАТЬ"}){
                form.Controls.Add(button);form.AcceptButton=button;form.Show();Application.DoEvents();
                int clicks=0;button.Click+=delegate{clicks++;};button.PerformClick();Check(clicks==1,"IButtonControl click");
                button.Enabled=false;button.PerformClick();Check(clicks==1,"disabled button does not click");
                using(var bitmap=new Bitmap(button.Width,button.Height)){button.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));Check(bitmap.GetPixel(0,0).ToArgb()==Brand.Background.ToArgb()&&bitmap.GetPixel(299,55).ToArgb()==Brand.Background.ToArgb(),"disabled rounded corners are cleared");}
                button.Enabled=true;
                typeof(ModernButton).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(button,new object[]{new KeyEventArgs(Keys.Space)});
                typeof(ModernButton).GetMethod("OnKeyUp",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(button,new object[]{new KeyEventArgs(Keys.Space)});
                Check(clicks==2,"Space activates button exactly once");form.Close();
            }
            Console.WriteLine("PASS: "+count+" checks");
            Console.WriteLine("Test skin: "+valid);
            return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
