using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Web.Script.Serialization;
using DeepSeekWhaleWpf;
using DeepSeekWhaleStandalone;
using DeepSeekBalanceViewer;
class VerifyNaturalPet {
 static BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Get(WhaleWindow w,string n){return typeof(WhaleWindow).GetField(n,F).GetValue(w);}
 static void Set(WhaleWindow w,string n,object v){typeof(WhaleWindow).GetField(n,F).SetValue(w,v);}
 static void Call(WhaleWindow w,string n,params object[] a){typeof(WhaleWindow).GetMethod(n,F).Invoke(w,a);}
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 static void Layout(WhaleWindow w){var root=(FrameworkElement)w.Content;root.Measure(new Size(w.Width,w.Height));root.Arrange(new Rect(0,0,w.Width,w.Height));root.UpdateLayout();}
 static BitmapFrame Frame(WhaleWindow w){Layout(w);var bitmap=new RenderTargetBitmap((int)w.Width,(int)w.Height,96,96,PixelFormats.Pbgra32);bitmap.Render((Visual)w.Content);return BitmapFrame.Create(bitmap);}
 static void Save(WhaleWindow w,string name){var e=new PngBitmapEncoder();e.Frames.Add(Frame(w));using(var f=File.Create("work/"+name+".png"))e.Save(f);}
 static BalanceResult Balance(string amount){return BalanceClient.ParseResponse("{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\""+amount+"\",\"granted_balance\":\"0\",\"topped_up_balance\":\""+amount+"\"}]}");}
 [STAThread] static void Main(){try{Run();}catch(Exception e){while(e.InnerException!=null)e=e.InnerException;Console.WriteLine("FAIL: "+e.GetType().Name+": "+e.Message+"\n"+e.StackTrace);Environment.ExitCode=1;}}
 static void Run(){
  var legacy=new JavaScriptSerializer().Deserialize<WhaleSettings>("{\"ScalePercent\":100}");Check(legacy.TargetFps==120,"Old settings lost fps default");
  var soft=new SoftValue(0);double prev=0;for(int i=0;i<240;i++){double p=soft.Step(3,1.0/120,13);Check(p>=prev&&p<3.001,"Spring overshoots monotone target");prev=p;}
  Check(Math.Abs(soft.Value-3)<.001,"Spring never settles");
  var a=new SoftValue(0);var b=new SoftValue(0);for(int i=0;i<120;i++)a.Step(1,1.0/120,13);for(int i=0;i<30;i++)b.Step(1,1.0/30,13);Check(Math.Abs(a.Value-b.Value)<.00001,"Frame-dependent smoothing");
  int samples=0;
  foreach(string pose in new[]{"stand","sit","prone"})foreach(string action in new[]{"tilt","twirl","hop","pet","shy","annoyed","charge","sleep"})for(int frame=0;frame<120;frame++){
   var m=PetMotionEngine.Sample(pose,action,frame/120.0*PetMotionEngine.Duration(pose,action),frame/120.0);
   Check(Math.Abs(m.HeadAngle)<5&&m.ScaleX>.85&&m.ScaleY>.85&&Math.Abs(m.Angle)<2,"Extreme body deformation");
   for(int y=0;y<24;y++)for(int x=0;x<24;x++){
    Point p=PetSprite.Deform(x/24.0,y/24.0,pose,m),q=PetSprite.Deform((x+1)/24.0,y/24.0,pose,m),r=PetSprite.Deform(x/24.0,(y+1)/24.0,pose,m);
    Check((q.X-p.X)*(r.Y-p.Y)-(q.Y-p.Y)*(r.X-p.X)>0,"Mesh triangle folded");
   }
   var foot=PetSprite.Deform(.5,.98,pose,m);Check(Math.Abs(foot.X-150)<.001&&Math.Abs(foot.Y-294)<.001,"Ground contact slides");samples++;
  }
  var app=new Application();var w=new WhaleWindow(true);var settings=(WhaleSettings)Get(w,"settings");
  settings.AutoPose=false;settings.HideDataPanel=true;Set(w,"nextAmbient",10000.0);Call(w,"BuildMenus");
  var sprite=(PetSprite)Get(w,"mascot");
  Call(w,"CompleteSpeech");((Border)Get(w,"speech")).Visibility=Visibility.Collapsed;Call(w,"UpdateLayoutMetrics");
  double clock=10;
  foreach(string pose in new[]{"stand","sit","prone"}){
   Call(w,"ChangePose",pose,clock);
   for(int i=0;i<120;i++)Call(w,"RenderPet",clock+i/60.0);
   Save(w,"natural-"+pose);
   var bitmap=Frame(w);var rgba=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);byte[] pixels=new byte[rgba.PixelWidth*rgba.PixelHeight*4];rgba.CopyPixels(pixels,rgba.PixelWidth*4,0);
   Check(pixels[3]==0,"Background not transparent");
   var petBitmap=new RenderTargetBitmap(300,300,96,96,PixelFormats.Pbgra32);petBitmap.Render(sprite.Child);byte[] petPixels=new byte[300*300*4];petBitmap.CopyPixels(petPixels,1200,0);
   int visible=0;for(int i=3;i<petPixels.Length;i+=4)if(petPixels[i]>100)visible++;Console.WriteLine(pose+" visible pixels: "+visible);Check(visible>8000,"Mesh is invisible");clock+=3;
  }
  Set(w,"transitionStart",-10.0);Set(w,"touchedHead",true);Call(w,"ReactToPetClick",100.0);Check((string)Get(w,"activeAction")=="pet","Head pat missing");
  Call(w,"ReactToPetClick",100.2);Check((string)Get(w,"activeAction")=="shy","Shy response is delayed");
  Call(w,"ReactToPetClick",100.4);Call(w,"ReactToPetClick",100.6);Check((string)Get(w,"activeAction")=="annoyed","Annoyed response is delayed");
  Set(w,"activeAction","");Set(w,"queuedAction","");settings.PetPose="stand";
  Call(w,"SetSleeping",true);Call(w,"RenderPet",200.0);Call(w,"RenderPet",201.0);
  Check((string)Get(w,"currentPose")=="prone"&&settings.PetPose=="stand","Sleep overwrote chosen pose");Check(sprite.Source==(BitmapSource)Get(w,"eyesClosed"),"Sleep eyes remain open");
  Call(w,"ReactToPetClick",201.1);Call(w,"RenderPet",202.0);Check(!(bool)Get(w,"sleeping")&&(string)Get(w,"currentPose")=="stand","Wake failed");
  settings.QuietMode=true;settings.AutoPose=true;Set(w,"nextAmbient",0.0);Set(w,"lastPoseChange",0.0);Call(w,"RenderPet",300.0);Check((string)Get(w,"activeAction")==""&&(string)Get(w,"currentPose")=="stand","Quiet mode starts unsolicited action");settings.QuietMode=false;settings.AutoPose=false;
  var drops=BalanceDrop.Between(Balance("10"),Balance("9.99"));Check(drops.Count==1&&drops[0].FloatingText=="−0.01","Charge incorrect");
  Check(BalanceDrop.Between(null,Balance("9")).Count==0&&BalanceDrop.Between(Balance("9"),Balance("10")).Count==0,"False charge");
  Call(w,"CompleteSpeech");((Border)Get(w,"speech")).Visibility=Visibility.Collapsed;Call(w,"UpdateLayoutMetrics");
  Call(w,"ShowCharge",drops[0]);var particles=(List<ChargeParticle>)Get(w,"charges");Call(w,"UpdateCharges",particles[0].Start+.4);Save(w,"natural-charge");Call(w,"UpdateCharges",10000.0);Check(particles.Count==0,"Particle leaked");
  var history=(List<ChatTurn>)Get(w,"chatHistory");history.Add(new ChatTurn("user","今天有点累，陪我一会儿吧"));history.Add(new ChatTurn("assistant","那就先松一口气吧。我就在这里，等你想说话了，轻轻叫我就好。"));Call(w,"RenderChatHistory",null,false,false);Call(w,"CompleteSpeech");Save(w,"natural-chat");
  int layouts=0;foreach(bool hidden in new[]{true,false})foreach(bool inputHidden in new[]{true,false})foreach(int scale in new[]{40,100,220})foreach(int font in new[]{13,18}){
   settings.HideDataPanel=hidden;settings.HideInput=inputHidden;settings.ScalePercent=scale;settings.TextSize=font;Call(w,"UpdateLayoutMetrics");Layout(w);
   var input=(Grid)Get(w,"petInput");Check((input.Visibility==Visibility.Collapsed)==inputHidden,"Input visibility incorrect");
   if(!inputHidden){Point p=input.TranslatePoint(new Point(input.ActualWidth,input.ActualHeight),(UIElement)w.Content);Check(p.X<=w.Width+1&&p.Y<=w.Height+1,"Input clipped");}layouts++;
  }
  settings.HideDataPanel=true;settings.HideInput=false;settings.ScalePercent=100;settings.TextSize=14;
  ((Border)Get(w,"speech")).Visibility=Visibility.Collapsed;Call(w,"UpdateLayoutMetrics");Set(w,"nextAmbient",10000.0);Set(w,"activeAction","");
  var sw=Stopwatch.StartNew();for(int i=0;i<1200;i++)Call(w,"RenderPet",400+i/120.0);sw.Stop();Console.WriteLine("Motion/mesh update mean: "+(sw.Elapsed.TotalMilliseconds/1200).ToString("F3")+" ms (excludes compositor)");
  sw.Restart();for(int i=0;i<120;i++){Call(w,"RenderPet",420+i/120.0);Frame(w);}sw.Stop();Console.WriteLine("Offline full-window render mean: "+(sw.Elapsed.TotalMilliseconds/120).ToString("F3")+" ms (not live FPS)");
  var gif=new GifBitmapEncoder();
  string[] sequence={"stand","sit","prone"};
  for(int segment=0;segment<3;segment++){
   double start=500+segment*3.6;Call(w,"ChangePose",sequence[segment],start);Set(w,"activeAction",segment==0?"pet":segment==1?"shy":"twirl");Set(w,"actionStart",start+.4);
   for(int i=0;i<108;i++){Call(w,"RenderPet",start+i/30.0);gif.Frames.Add(Frame(w));}
  }
  using(var f=File.Create("work/natural-motion-preview.gif"))gif.Save(f);PatchGif("work/natural-motion-preview.gif");
  Console.WriteLine("PASS: "+samples+" motion samples; mesh triangles/contact; damping at 30/120 Hz; alpha renders; immediate click tiers; sleep/wake; quiet mode; charge cleanup; "+layouts+" layouts. Offline only, no API requests.");w.Close();app.Shutdown();
 }
 static void PatchGif(string path){byte[] g=File.ReadAllBytes(path);int n=0;for(int i=0;i+7<g.Length;i++)if(g[i]==33&&g[i+1]==249&&g[i+2]==4&&g[i+7]==0){g[i+3]=(byte)((g[i+3]&227)|8);g[i+4]=(byte)(n++%3==2?4:3);g[i+5]=0;}int at=13;if((g[10]&128)!=0)at+=3*(1<<((g[10]&7)+1));byte[] loop={33,255,11,78,69,84,83,67,65,80,69,50,46,48,3,1,0,0,0};using(var s=File.Create(path)){s.Write(g,0,at);s.Write(loop,0,loop.Length);s.Write(g,at,g.Length-at);}}
}
