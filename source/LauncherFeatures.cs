using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal static class MemoryBudget
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus { public uint Length,Load; public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual; }
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    internal struct Snapshot { internal long TotalMb, AvailableMb; }
    internal static Snapshot Read()
    {
        var s=new MemoryStatus();s.Length=(uint)Marshal.SizeOf(typeof(MemoryStatus));
        try { if(GlobalMemoryStatusEx(ref s))return new Snapshot{TotalMb=(long)(s.TotalPhysical/1048576),AvailableMb=(long)(s.AvailablePhysical/1048576)}; } catch(DllNotFoundException){} catch(EntryPointNotFoundException){}
        return new Snapshot();
    }
    internal static int MaximumFor(long totalMb) { return totalMb<=0?4:(int)Math.Max(2,Math.Min(int.MaxValue/1024,(totalMb-2048)/1024)); }
    internal static readonly int MaximumGb=MaximumFor(Read().TotalMb);
    internal static int RecommendedGb { get {return Math.Max(2,Math.Min(8,MaximumGb/2));} }
}

internal sealed class SkinPreferences
{
    public bool hasLocalChoice {get;set;}
    public bool localEnabled {get;set;}
    public bool slim {get;set;}
    public string serverMode {get;set;}
    public string serverValue {get;set;}
    public bool serverAutoApply {get;set;}
    internal static string SettingsPath {get{return Client.LocalPath("game/config/larp-client/launcher-skin.json");}}
    internal static string ImagePath {get{return Client.LocalPath("game/config/larp-client/skins/launcher.png");}}
    internal static SkinPreferences Load()
    {
        try { if(File.Exists(SettingsPath))return Client.Json.Deserialize<SkinPreferences>(File.ReadAllText(SettingsPath,Encoding.UTF8))??new SkinPreferences(); }
        catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}
        return new SkinPreferences();
    }
    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
        string temporary=SettingsPath+".tmp";
        File.WriteAllText(temporary,Client.Json.Serialize(this),Client.Utf8);
        if(File.Exists(SettingsPath))File.Replace(temporary,SettingsPath,null);else File.Move(temporary,SettingsPath);
    }
    internal static Bitmap Decode(string file)
    {
        byte[] bytes=File.ReadAllBytes(file);
        byte[] signature={137,80,78,71,13,10,26,10};
        if(bytes.Length<33 || bytes.Length>131072)throw new InvalidDataException("Нужен PNG размером не более 128 КБ.");
        for(int i=0;i<signature.Length;i++)if(bytes[i]!=signature[i])throw new InvalidDataException("Выбранный файл не является PNG.");
        // Check IHDR dimensions before decoding to avoid allocating oversized images.
        if(bytes[12]!=73||bytes[13]!=72||bytes[14]!=68||bytes[15]!=82||bytes[16]!=0||bytes[17]!=0||bytes[18]!=0||bytes[19]!=64||bytes[20]!=0||bytes[21]!=0||bytes[22]!=0||(bytes[23]!=32&&bytes[23]!=64))
            throw new InvalidDataException("Скин должен иметь размер 64×64 или 64×32 пикселя.");
        using(var stream=new MemoryStream(bytes))using(var image=Image.FromStream(stream,true,true))
        {
            if(image.Width!=64||(image.Height!=64&&image.Height!=32))throw new InvalidDataException("Скин должен иметь размер 64×64 или 64×32.");
            var bitmap=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppArgb);
            using(var graphics=Graphics.FromImage(bitmap)){graphics.CompositingMode=CompositingMode.SourceCopy;graphics.DrawImageUnscaled(image,0,0);}
            return bitmap;
        }
    }
    internal string ServerCommand()
    {
        string value=(serverValue??"").Trim();
        if(serverMode=="off"||string.IsNullOrEmpty(serverMode))return "";
        if(serverMode=="name") {
            if(!Regex.IsMatch(value,"^[A-Za-z0-9_]{3,16}$"))throw new InvalidDataException("Укажите никнейм скина: 3–16 латинских букв, цифр или _.");
            return "skin "+value;
        }
        Uri url;
        if(serverMode!="url" || value.Length>190 || Regex.IsMatch(value,"[\\s\"\\\\]") || !Uri.TryCreate(value,UriKind.Absolute,out url)||!((url.Scheme==Uri.UriSchemeHttps&&!string.IsNullOrEmpty(url.Host)&&string.IsNullOrEmpty(url.UserInfo))||(url.Scheme=="skinsrestorer-axolotl"&&value.StartsWith("skinsrestorer-axolotl://",StringComparison.Ordinal)&&value.Length>24)))
            throw new InvalidDataException("Нужна HTTPS-ссылка или код skinsrestorer-axolotl://, до 190 символов, без пробелов.");
        return "skin url \""+value+"\" "+(slim?"slim":"classic");
    }
}

internal sealed class SkinPreview : Control
{
    private Bitmap skin;
    internal bool Slim;
    internal SkinPreview(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Brand.Background;}
    internal void Reload()
    {
        if(skin!=null){skin.Dispose();skin=null;}
        var settings=SkinPreferences.Load();Slim=settings.slim;
        try{if(settings.localEnabled && File.Exists(SkinPreferences.ImagePath))skin=SkinPreferences.Decode(SkinPreferences.ImagePath);}catch(Exception){skin=null;}
        Invalidate();
    }
    internal void SetImage(Bitmap image){if(skin!=null)skin.Dispose();skin=image==null?null:new Bitmap(image);Invalidate();}
    private void Part(Graphics g,Rectangle source,Rectangle destination,bool flip)
    {
        Point[] points=flip?new[]{new Point(destination.Right,destination.Top),new Point(destination.Left,destination.Top),new Point(destination.Right,destination.Bottom)}:new[]{new Point(destination.Left,destination.Top),new Point(destination.Right,destination.Top),new Point(destination.Left,destination.Bottom)};
        g.DrawImage(skin,points,source,GraphicsUnit.Pixel);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if(skin==null){Brand.Logo(e.Graphics,new RectangleF(22,22,Width-44,Height-50),Color.White);return;}
        var g=e.Graphics;g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
        int scale=Math.Max(1,Math.Min((Width-36)/16,(Height-24)/32)),x=(Width-16*scale)/2,y=(Height-32*scale)/2;
        int arm=Slim?3:4;
        Part(g,new Rectangle(8,8,8,8),new Rectangle(x+4*scale,y,8*scale,8*scale),false);
        Part(g,new Rectangle(20,20,8,12),new Rectangle(x+4*scale,y+8*scale,8*scale,12*scale),false);
        Part(g,new Rectangle(44,20,arm,12),new Rectangle(x+(4-arm)*scale,y+8*scale,arm*scale,12*scale),false);
        Part(g,skin.Height==64?new Rectangle(36,52,arm,12):new Rectangle(44,20,arm,12),new Rectangle(x+12*scale,y+8*scale,arm*scale,12*scale),skin.Height==32);
        Part(g,new Rectangle(4,20,4,12),new Rectangle(x+4*scale,y+20*scale,4*scale,12*scale),false);
        Part(g,skin.Height==64?new Rectangle(20,52,4,12):new Rectangle(4,20,4,12),new Rectangle(x+8*scale,y+20*scale,4*scale,12*scale),skin.Height==32);
        Part(g,new Rectangle(40,8,8,8),new Rectangle(x+4*scale,y,8*scale,8*scale),false);
        if(skin.Height==64){
            Part(g,new Rectangle(20,36,8,12),new Rectangle(x+4*scale,y+8*scale,8*scale,12*scale),false);
            Part(g,new Rectangle(44,36,arm,12),new Rectangle(x+(4-arm)*scale,y+8*scale,arm*scale,12*scale),false);
            Part(g,new Rectangle(52,52,arm,12),new Rectangle(x+12*scale,y+8*scale,arm*scale,12*scale),false);
            Part(g,new Rectangle(4,36,4,12),new Rectangle(x+4*scale,y+20*scale,4*scale,12*scale),false);
            Part(g,new Rectangle(4,52,4,12),new Rectangle(x+8*scale,y+20*scale,4*scale,12*scale),false);
        }
    }
    protected override void Dispose(bool disposing){if(disposing&&skin!=null)skin.Dispose();base.Dispose(disposing);}
}

internal sealed class SkinDialog : Form
{
    private readonly SkinPreview preview;
    private readonly ComboBox model,serverMode;
    private readonly TextBox serverValue;
    private readonly CheckBox automatic;
    private readonly Label message;
    private readonly SkinPreferences original;
    private Bitmap imported;
    private bool reset;
    internal SkinDialog()
    {
        original=SkinPreferences.Load();
        Text="LARP • Скин игрока";ClientSize=new Size(680,590);AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
        StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
        BackColor=Brand.Background;ForeColor=Color.White;Font=new Font("Segoe UI",10);
        LabelAt("Скин игрока",26,22,600,42,22,Color.White);
        preview=new SkinPreview{Left=24,Top=84,Width=220,Height=300};preview.Reload();Controls.Add(preview);
        AddButton("Выбрать PNG",24,402,220,44,true,delegate{
            using(var dialog=new OpenFileDialog{Filter="Minecraft skin (*.png)|*.png",Title="Выберите скин 64×64 или 64×32"}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try{var next=SkinPreferences.Decode(dialog.FileName);if(imported!=null)imported.Dispose();imported=next;reset=false;preview.SetImage(imported);message.Text="Скин выбран. Нажмите «Сохранить».";}
                catch(Exception error){message.Text=error.Message;}
            }
        });
        AddButton("Сбросить скин",24,456,220,38,false,delegate{if(imported!=null){imported.Dispose();imported=null;}reset=true;preview.SetImage(null);message.Text="Локальный скин будет сброшен после сохранения.";});
        LabelAt("PNG 64×64 или 64×32 • до 128 КБ",24,504,245,24,8,Brand.Muted);
        LabelAt("Модель персонажа",274,88,365,25,10,Brand.Muted);
        model=AddCombo(274,119,new[]{"Классическая","Тонкие руки"});model.SelectedIndex=original.slim?1:0;
        model.SelectedIndexChanged+=delegate{preview.Slim=model.SelectedIndex==1;preview.Invalidate();};
        LabelAt("Скин для других игроков",274,174,370,26,11,Color.White);
        serverMode=AddCombo(274,210,new[]{"Не применять на сервере","По никнейму скина","По ссылке или коду SkinsRestorer"});
        serverMode.SelectedIndex=original.serverMode=="name"?1:original.serverMode=="url"?2:0;
        serverValue=new TextBox{Left=286,Top=261,Width=335,BorderStyle=BorderStyle.None,BackColor=Brand.Surface,ForeColor=Color.White,MaxLength=190,Text=original.serverValue??""};
        Controls.Add(new FieldPanel(serverValue){Left=274,Top=248,Width=366,Height=48});serverValue.Left=12;serverValue.Top=13;
        automatic=new CheckBox{Text="Применять после входа на сервер\nс поддержкой SkinsRestorer",Left=274,Top=310,Width=370,Height=48,Checked=original.serverAutoApply,ForeColor=Brand.Muted};Controls.Add(automatic);
        serverMode.SelectedIndexChanged+=delegate{serverValue.Enabled=serverMode.SelectedIndex!=0;if(serverMode.SelectedIndex==0)automatic.Checked=false;};serverValue.Enabled=serverMode.SelectedIndex!=0;
        automatic.CheckedChanged+=delegate{if(automatic.Checked&&serverMode.SelectedIndex==0)serverMode.SelectedIndex=1;};
        AddButton("Загрузить PNG на сайт ↗",274,368,366,40,false,delegate{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://skinsrestorer.net/upload"){UseShellExecute=true});}catch(Exception error){message.Text=error.Message;}});
        AddButton("Скопировать команду /skin",274,418,366,40,false,delegate{try{string command=Selection().ServerCommand();if(command=="")throw new InvalidDataException("Выберите никнейм или ссылку для серверного скина.");Clipboard.SetText("/"+command);message.Text="Команда скопирована. Вставьте её в чат сервера.";}catch(Exception error){message.Text=error.Message;}});
        LabelAt("Локальный PNG виден вам и в меню игры.\nДругим игрокам — после применения сервером.\nДля PNG опубликуйте его и вставьте ссылку/код.",274,470,370,60,8,Brand.Muted);
        message=LabelAt("",24,538,400,42,9,Brand.Accent);
        var save=AddButton("Сохранить",458,535,182,42,true,delegate{SaveSelection();});AcceptButton=save;
        FormClosed+=delegate{if(imported!=null)imported.Dispose();};
    }
    private SkinPreferences Selection(){return new SkinPreferences{hasLocalChoice=original.hasLocalChoice||imported!=null||reset,localEnabled=reset?false:imported!=null||original.localEnabled,slim=model.SelectedIndex==1,serverMode=serverMode.SelectedIndex==1?"name":serverMode.SelectedIndex==2?"url":"off",serverValue=serverValue.Text.Trim(),serverAutoApply=automatic.Checked&&serverMode.SelectedIndex!=0};}
    private void SaveSelection()
    {
        try{
            var settings=Selection();settings.ServerCommand();
            if(imported!=null){
                Directory.CreateDirectory(Path.GetDirectoryName(SkinPreferences.ImagePath));
                string temporary=SkinPreferences.ImagePath+".tmp";imported.Save(temporary,ImageFormat.Png);
                if(File.Exists(SkinPreferences.ImagePath))File.Replace(temporary,SkinPreferences.ImagePath,null);else File.Move(temporary,SkinPreferences.ImagePath);
            }
            settings.Save();DialogResult=DialogResult.OK;Close();
        }catch(Exception error){message.Text=error.Message;}
    }
    private Label LabelAt(string text,int x,int y,int w,int h,float size,Color color){var l=new Label{Text=text,Left=x,Top=y,Width=w,Height=h,Font=new Font("Segoe UI",size),ForeColor=color};Controls.Add(l);return l;}
    private ComboBox AddCombo(int x,int y,string[] items){var c=new ComboBox{Left=x,Top=y,Width=366,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Brand.Surface,ForeColor=Color.White,FlatStyle=FlatStyle.Flat};c.Items.AddRange(items);Controls.Add(c);return c;}
    private ModernButton AddButton(string text,int x,int y,int w,int h,bool primary,EventHandler handler){var b=new ModernButton{Text=text,Left=x,Top=y,Width=w,Height=h,Primary=primary,Font=new Font("Segoe UI",10)};b.Click+=handler;Controls.Add(b);return b;}
}
