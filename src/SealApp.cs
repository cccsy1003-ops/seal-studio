using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Forms=System.Windows.Forms;

namespace SealStudio {
public static class Program {
 [STAThread] public static int Main(string[] args){
  if(args.Contains("--selftest")){try{return SelfTest.Run(args.Length>1?args[1]:AppDomain.CurrentDomain.BaseDirectory);}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-error.txt"),ex.ToString());return 1;}}
  var app=new Application();app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show(e.Exception.Message,"印章2",MessageBoxButton.OK,MessageBoxImage.Warning);e.Handled=true;};
  try{new Studio(app);app.Run();return 0;}catch(Exception ex){MessageBox.Show(ex.Message,"印章2启动失败");return 1;}
 }
}
public sealed class Studio {
 Window window;List<FontEntry> fonts=new List<FontEntry>();DispatcherTimer timer=new DispatcherTimer();bool ready=false,sync=false;string lastDir="";string beforeConversion=null;HashSet<int> selectedCharacters=new HashSet<int>();
 string folder=AppDomain.CurrentDomain.BaseDirectory;
 T C<T>(string name) where T:class{return window.FindName(name) as T;}
 TextBox T(string name){return C<TextBox>(name);} ComboBox B(string name){return C<ComboBox>(name);} CheckBox K(string name){return C<CheckBox>(name);}
 public Studio(Application app){
  using(var st=Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))window=(Window)XamlReader.Load(st);
  app.MainWindow=window;
  // Keep the native title bar and all system buttons inside the visible desktop.
  Rect workArea=SystemParameters.WorkArea;
  double availableWidth=Math.Max(1,workArea.Width-24),availableHeight=Math.Max(1,workArea.Height-24);
  window.MinWidth=Math.Min(window.MinWidth,availableWidth);
  window.MinHeight=Math.Min(window.MinHeight,availableHeight);
  window.Width=Math.Min(window.Width,availableWidth);
  window.Height=Math.Min(window.Height,availableHeight);
  window.Left=workArea.Left+(workArea.Width-window.Width)/2;
  window.Top=workArea.Top+(workArea.Height-window.Height)/2;
  foreach(string n in new[]{"Mode","Layout","BlackMeaning"})B(n).SelectedIndex=0;
  LoadFonts();LoadSettings();RebuildCharacters(false);UpdateReversalControls();
  timer.Interval=TimeSpan.FromMilliseconds(300);timer.Tick+=(s,e)=>{timer.Stop();Preview();};
  foreach(string n in TextNames){string key=n;T(n).TextChanged+=(s,e)=>{if(sync||!ready)return;if(key=="StampText")RebuildCharacters(true);if((key=="WidthCm"||key=="HeightCm")&&K("Square").IsChecked==true){sync=true;T(key=="WidthCm"?"HeightCm":"WidthCm").Text=T(key).Text;sync=false;}Schedule();};}
  foreach(string n in ComboNames)B(n).SelectionChanged+=(s,e)=>Schedule();
  B("MirrorMode").SelectionChanged+=(s,e)=>{UpdateReversalControls();Schedule();};
  B("Dpi").AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((s,e)=>Schedule()));
  B("FontChoice").SelectionChanged+=(s,e)=>{UpdateFontHelp();Schedule();};
  foreach(string n in CheckNames){K(n).Checked+=(s,e)=>Schedule();K(n).Unchecked+=(s,e)=>Schedule();}
  K("Square").Checked+=(s,e)=>{if(ready){T("HeightCm").Text=T("WidthCm").Text;Schedule();}};
  T("FontSearch").TextChanged+=(s,e)=>FilterFonts();
  C<Button>("ImportFont").Click+=(s,e)=>Import();C<Button>("ReloadFonts").Click+=(s,e)=>{LoadFonts();Schedule();};
  C<Button>("PickDir").Click+=(s,e)=>{using(var d=new Forms.FolderBrowserDialog()){d.Description="选择印章图片保存文件夹";if(Directory.Exists(T("OutputDir").Text))d.SelectedPath=T("OutputDir").Text;if(d.ShowDialog()==Forms.DialogResult.OK)T("OutputDir").Text=d.SelectedPath;}};
  C<Button>("OpenOutput").Click+=(s,e)=>{try{string dir=lastDir.Length>0?lastDir:T("OutputDir").Text;Directory.CreateDirectory(dir);Process.Start("explorer.exe",'"'+dir+'"');}catch(Exception ex){SetStatus("无法打开文件夹："+ex.Message,true);}};
  C<Button>("Export").Click+=(s,e)=>Export();
  C<Button>("HanStyle").Click+=(s,e)=>ApplyStyle(true);
  C<Button>("XiaoStyle").Click+=(s,e)=>ApplyStyle(false);
  C<Button>("ToTraditional").Click+=(s,e)=>{try{beforeConversion=T("StampText").Text;T("StampText").Text=ConvertTraditional(beforeConversion);C<Button>("RestoreText").IsEnabled=true;timer.Stop();Preview();C<TextBlock>("FontHelp").Text+=" 已转繁体，请核对姓名与一简多繁字；可撤销。";}catch(Exception ex){SetStatus(ex.Message,true);}};
  C<Button>("RestoreText").Click+=(s,e)=>{if(beforeConversion!=null)T("StampText").Text=beforeConversion;beforeConversion=null;C<Button>("RestoreText").IsEnabled=false;UpdateFontHelp();};
  window.Closing+=(s,e)=>SaveSettings();ready=true;UpdateFontHelp();Preview();window.Show();
 }
 static string[] TextNames={"StampText","Columns","TextSize","Spacing","MarginSize","BorderSize","WidthCm","HeightCm","OutputDir"};
 static string[] ComboNames={"Mode","Layout","BlackMeaning","Dpi"};
 static string[] CheckNames={"Stretch","BorderOn","Square","AlsoBmp"};

 public static ReversalMode ReadSavedReversal(XElement root){int mode;var value=root.Element("MirrorMode");if(value!=null&&int.TryParse(value.Value,out mode)&&Enum.IsDefined(typeof(ReversalMode),mode))return (ReversalMode)mode;bool old;var legacy=root.Element("Mirror");return legacy!=null&&bool.TryParse(legacy.Value,out old)&&!old?ReversalMode.None:ReversalMode.Whole;}
 public static HashSet<int> ReadSavedCharacters(XElement root){var result=new HashSet<int>();var value=root.Element("ReversedCharacters");if(value!=null)foreach(string part in value.Value.Split(',')){int n;if(int.TryParse(part,out n)&&n>0)result.Add(n);}return result;}
 ReversalMode GetReversal(){var item=B("MirrorMode").SelectedItem as ComboBoxItem;return item==null?ReversalMode.Whole:(ReversalMode)int.Parse(item.Tag.ToString());}
 void SetReversal(ReversalMode mode){foreach(ComboBoxItem item in B("MirrorMode").Items)if(int.Parse(item.Tag.ToString())==(int)mode){B("MirrorMode").SelectedItem=item;break;}}
 void UpdateReversalControls(){C<StackPanel>("CharacterPanel").Visibility=GetReversal()==ReversalMode.Selected?Visibility.Visible:Visibility.Collapsed;}
 void RebuildCharacters(bool reset){
  var cps=SealCore.Codepoints(T("StampText").Text).Where(c=>!SealCore.White(c)).ToList();
  if(reset)selectedCharacters.Clear();selectedCharacters.RemoveWhere(i=>i>cps.Count);
  var panel=C<WrapPanel>("CharacterChoices");panel.Children.Clear();
  for(int i=0;i<Math.Min(cps.Count,144);i++){int number=i+1;var box=new CheckBox{Content=number+" · "+char.ConvertFromUtf32(cps[i]),IsChecked=selectedCharacters.Contains(number),Margin=new Thickness(0,5,15,3),ToolTip="只翻转这个字的字形，原位置不变"};box.Checked+=(sender,e)=>{selectedCharacters.Add(number);Schedule();};box.Unchecked+=(sender,e)=>{selectedCharacters.Remove(number);Schedule();};panel.Children.Add(box);}
 }
 void Schedule(){if(!ready)return;C<Button>("Export").IsEnabled=false;timer.Stop();timer.Start();}
 void SetStatus(string text,bool bad){C<TextBlock>("Status").Text=text;C<TextBlock>("Status").Foreground=bad?Brushes.Firebrick:new SolidColorBrush(Color.FromRgb(66,92,71));}
 public static List<FontEntry> Discover(string folder){
  var result=new List<FontEntry>();
  Action<FontFamily,bool> add=(fam,imported)=>{try{
   string name=fam.FamilyNames.ContainsKey(XmlLanguage.GetLanguage("zh-cn"))?fam.FamilyNames[XmlLanguage.GetLanguage("zh-cn")]:(fam.FamilyNames.ContainsKey(XmlLanguage.GetLanguage("zh-tw"))?fam.FamilyNames[XmlLanguage.GetLanguage("zh-tw")]:fam.FamilyNames.Values.FirstOrDefault());
   if(string.IsNullOrEmpty(name))name=fam.Source;
   var faces=fam.GetTypefaces().OrderBy(x=>Math.Abs(x.Weight.ToOpenTypeWeight()-400)+(x.Style==FontStyles.Normal?0:500));
   foreach(var face in faces){GlyphTypeface glyph;if(face.TryGetGlyphTypeface(out glyph)){result.Add(new FontEntry{Name=name,Face=face,Glyph=glyph,Imported=imported});break;}}
  }catch{}};
  foreach(var fam in Fonts.SystemFontFamilies)add(fam,false);
  string local=Path.Combine(folder,"Fonts");Directory.CreateDirectory(local);
  try{foreach(var fam in Fonts.GetFontFamilies(local+Path.DirectorySeparatorChar))add(fam,true);}catch{}
  return result.GroupBy(f=>f.Name).Select(g=>g.OrderByDescending(f=>f.Imported).First()).OrderBy(f=>FontRank(f.Name)).ThenBy(f=>f.Name,StringComparer.CurrentCulture).ToList();
 }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
 static extern int LCMapStringEx(string locale,uint flags,string source,int length,[Out] char[] target,int capacity,IntPtr version,IntPtr reserved,IntPtr sort);
 public static string ConvertTraditional(string text){if(string.IsNullOrEmpty(text))return text;int n=LCMapStringEx("zh-CN",0x04000000,text,text.Length,null,0,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);if(n<=0)throw new InvalidOperationException("简繁转换失败，请直接输入繁体字。");var b=new char[n];int written=LCMapStringEx("zh-CN",0x04000000,text,text.Length,b,n,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);if(written<=0)throw new InvalidOperationException("简繁转换失败。");return new string(b,0,written);}
 static bool IsHan(FontEntry f){return f!=null&&(f.Name.Contains("印篆")||f.Glyph.FamilyNames.Values.Any(x=>x.Contains("HanWangInJang")));}
 static bool IsXiao(FontEntry f){return f!=null&&(f.Name.Contains("崇羲")||f.Glyph.FamilyNames.Values.Any(x=>x.Contains("Chong Xi Small Seal")));}
 void UpdateFontHelp(){var f=B("FontChoice").SelectedItem as FontEntry;C<TextBlock>("FontHelp").Text=IsHan(f)?"汉白文风格参考：王汉宗印篆体繁。个人学习参考；商用需向版权方核实。主要支持繁体。":IsXiao(f)?"小篆：崇羲篆体（王心怡、季旭昇等）。主要支持繁体；缺字可先点“简 → 繁”。":"两个按钮会选择字库并切换阴阳刻；厘米尺寸保持当前设置。";}
 void ApplyStyle(bool han){var f=fonts.FirstOrDefault(x=>han?IsHan(x):IsXiao(x));if(f==null){SetStatus("对应字库未找到，请保留软件 Fonts 文件夹后刷新字体。",true);return;}bool old=ready;ready=false;T("FontSearch").Text="";FilterFonts(f.Name);B("FontChoice").SelectedItem=f;B("Mode").SelectedIndex=han?1:0;B("Layout").SelectedIndex=0;T("Spacing").Text=han?"2":"6";T("TextSize").Text=han?"98":"92";K("Stretch").IsChecked=true;K("BorderOn").IsChecked=true;ready=old;timer.Stop();UpdateFontHelp();Preview();}
 static int FontRank(string n){if(n.Contains("篆")||n.Contains("印")||n.Contains("Seal"))return 0;if(n.Contains("隶"))return 1;if(n.Contains("楷"))return 2;if(n.Contains("行")||n.Contains("魏")||n.Contains("宋"))return 3;return 4;}
 void LoadFonts(){string name=B("FontChoice").SelectedItem is FontEntry?((FontEntry)B("FontChoice").SelectedItem).Name:"楷体";fonts=Discover(folder);FilterFonts(name);C<TextBlock>("FontCount").Text="已读取 "+fonts.Count+" 种字体（含程序 Fonts 文件夹）。";}
 void FilterFonts(){FilterFonts(B("FontChoice").SelectedItem is FontEntry?((FontEntry)B("FontChoice").SelectedItem).Name:null);}
 void FilterFonts(string selected){string q=T("FontSearch").Text.Trim();var list=fonts.Where(f=>f.Name.IndexOf(q,StringComparison.CurrentCultureIgnoreCase)>=0).ToList();B("FontChoice").ItemsSource=list;B("FontChoice").SelectedItem=list.FirstOrDefault(f=>f.Name==selected)??list.FirstOrDefault();Schedule();}
 void Import(){
  var d=new Microsoft.Win32.OpenFileDialog{Filter="字体文件 (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc",Multiselect=true,Title="导入篆书、隶书等字体"};if(d.ShowDialog(window)!=true)return;
  int count=0;var errors=new List<string>();
  foreach(string file in d.FileNames)try{
   string dest=Path.Combine(folder,"Fonts",Path.GetFileName(file));if(string.Equals(Path.GetFullPath(file),Path.GetFullPath(dest),StringComparison.OrdinalIgnoreCase)){count++;continue;}
   if(File.Exists(dest))dest=Path.Combine(folder,"Fonts",Path.GetFileNameWithoutExtension(file)+"_"+Guid.NewGuid().ToString("N").Substring(0,6)+Path.GetExtension(file));
   File.Copy(file,dest,false);count++;
  }catch(Exception ex){errors.Add(Path.GetFileName(file)+"："+ex.Message);}
  T("FontSearch").Text="";LoadFonts();var imported=fonts.FirstOrDefault(f=>f.Imported);if(imported!=null)B("FontChoice").SelectedItem=imported;Preview();
  SetStatus("已复制 "+count+" 个字体文件，识别 "+fonts.Count(f=>f.Imported)+" 种导入字体。"+(errors.Count>0?string.Join("；",errors.ToArray()):""),errors.Count>0);
 }
 double Number(string n,string title,double min,double max){double d;if(!double.TryParse(T(n).Text,NumberStyles.Float,CultureInfo.InvariantCulture,out d)&&!double.TryParse(T(n).Text,out d))throw new ArgumentException(title+"请输入有效数字。");if(double.IsNaN(d)||double.IsInfinity(d)||d<min||d>max)throw new ArgumentException(title+"应在 "+min+"～"+max+" 之间。");return d;}
 SealSettings Read(){
  double dpi;if(!double.TryParse(B("Dpi").Text,out dpi)||double.IsNaN(dpi)||double.IsInfinity(dpi))throw new ArgumentException("DPI 请输入有效数字。");
  double cols=Number("Columns","列数",1,12);if(cols!=Math.Truncate(cols))throw new ArgumentException("列数必须是整数。");
  return new SealSettings{Text=T("StampText").Text,WidthCm=Number("WidthCm","宽度 cm",.3,20),HeightCm=Number("HeightCm","高度 cm",.3,20),Dpi=dpi,MarginCm=Number("MarginSize","边距 cm",0,5),BorderCm=Number("BorderSize","边框 cm",0,2),Columns=(int)cols,SizePercent=Number("TextSize","文字大小 %",20,100),SpacingPercent=Number("Spacing","字间留白 %",0,40),Mode=B("Mode").SelectedIndex,Layout=B("Layout").SelectedIndex,BlackRemoves=B("BlackMeaning").SelectedIndex==0,Reversal=GetReversal(),ReversedCharacters=new HashSet<int>(selectedCharacters),Stretch=K("Stretch").IsChecked==true,Border=K("BorderOn").IsChecked==true,OutputDir=T("OutputDir").Text.Trim()};
 }
 void Preview(){try{
  var s=Read();C<TextBlock>("MirrorInfo").Text=SealCore.ReversalName(s);C<TextBlock>("Hint").Text=SealCore.ReversalHelp(s);var font=B("FontChoice").SelectedItem as FontEntry;SealCore.Validate(s,font);
  double factor=650/Math.Max(s.WidthCm,s.HeightCm);int w=Math.Max(1,(int)(s.WidthCm*factor)),h=Math.Max(1,(int)(s.HeightCm*factor));
  BitmapSource engraving,paper;SealCore.Render(s,font,w,h,out engraving,out paper);C<Image>("PaperPreview").Source=paper;C<Image>("CutPreview").Source=engraving;
  C<TextBlock>("SizeInfo").Text=s.WidthCm+" × "+s.HeightCm+" cm   ·   "+SealCore.Pixels(s.WidthCm,s.Dpi)+" × "+SealCore.Pixels(s.HeightCm,s.Dpi)+" px   ·   "+s.Dpi+" DPI";
  C<TextBlock>("MirrorInfo").Text=SealCore.ReversalName(s);
  C<TextBlock>("BlackInfo").Text=s.BlackRemoves?"黑色去除 / 白色保留":"黑色保留 / 白色去除";
  C<TextBlock>("Hint").Text=(s.Mode==0?"阳刻：字凸起，盖在纸上是红字白底。":"阴刻：字凹下，盖在纸上是白字红底。")+"\n"+SealCore.ReversalHelp(s)+"  请核对设备的黑色去除 / 保留约定。";
  B("Layout").ToolTip="传统竖排：先右列从上到下，再移向左列。";T("Columns").IsEnabled=s.Layout<=1;
  C<Button>("Export").Content=s.OutputDir.StartsWith(@"D:\",StringComparison.OrdinalIgnoreCase)?"生成并保存到 D 盘":"生成并保存图片";
  C<Button>("Export").IsEnabled=true;SetStatus("预览已更新 · 字体："+font.Name+" · 图片按实际厘米尺寸导出",false);
 }catch(Exception ex){C<Image>("PaperPreview").Source=null;C<Image>("CutPreview").Source=null;C<Button>("Export").IsEnabled=false;SetStatus(ex.Message,true);}}
 void Export(){timer.Stop();try{var s=Read();var f=B("FontChoice").SelectedItem as FontEntry;SealCore.Validate(s,f);C<Button>("Export").IsEnabled=false;window.Cursor=System.Windows.Input.Cursors.Wait;lastDir=SealCore.Export(s,f,K("AlsoBmp").IsChecked==true);SaveSettings();SetStatus("已保存："+lastDir,false);MessageBox.Show(window,"图片已生成。\n\n"+lastDir+"\n\n包含雕刻图、实际盖印参考图和尺寸说明。\n点击“打开输出文件夹”即可查看。","保存完成",MessageBoxButton.OK,MessageBoxImage.Information);}catch(Exception ex){SetStatus("保存失败："+ex.Message,true);MessageBox.Show(window,"保存失败："+ex.Message+"\n\n可在左侧修改保存位置后重试。","印章2",MessageBoxButton.OK,MessageBoxImage.Warning);}finally{window.Cursor=null;C<Button>("Export").IsEnabled=true;}}
 void SaveSettings(){try{var root=new XElement("SealStudio");foreach(string n in TextNames)root.Add(new XElement(n,T(n).Text));foreach(string n in ComboNames)root.Add(new XElement(n,n=="Dpi"?B(n).Text:B(n).SelectedIndex.ToString()));foreach(string n in CheckNames)root.Add(new XElement(n,K(n).IsChecked==true));var f=B("FontChoice").SelectedItem as FontEntry;if(f!=null)root.Add(new XElement("Font",f.Name));root.Add(new XElement("MirrorMode",(int)GetReversal()));root.Add(new XElement("ReversedCharacters",string.Join(",",selectedCharacters.OrderBy(x=>x).Select(x=>x.ToString()).ToArray())));root.Save(Path.Combine(folder,"设置.xml"));}catch{}}
 void LoadSettings(){try{string p=Path.Combine(folder,"设置.xml");if(!File.Exists(p))return;var root=XElement.Load(p);SetReversal(ReadSavedReversal(root));selectedCharacters=ReadSavedCharacters(root);foreach(string n in TextNames){var el=root.Element(n);if(el!=null)T(n).Text=el.Value;}foreach(string n in ComboNames){var el=root.Element(n);if(el==null)continue;if(n=="Dpi")B(n).Text=el.Value;else B(n).SelectedIndex=int.Parse(el.Value);}foreach(string n in CheckNames){var el=root.Element(n);if(el!=null)K(n).IsChecked=bool.Parse(el.Value);}if(root.Element("Font")!=null)B("FontChoice").SelectedItem=fonts.FirstOrDefault(f=>f.Name==root.Element("Font").Value)??fonts.FirstOrDefault();if(K("Square").IsChecked==true)T("HeightCm").Text=T("WidthCm").Text;}catch{}}
}
}
