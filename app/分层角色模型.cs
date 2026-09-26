using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeepSeekWhaleWpf
{
    // Portable, data-only layered model. Cubism .moc3 is a different proprietary format.
    internal sealed class PuppetDefinition
    {
        public string format { get; set; }
        public string name { get; set; }
        public List<PuppetLayer> layers { get; set; }
    }
    internal sealed class PuppetLayer
    {
        public string id { get; set; }
        public string image { get; set; }
        public int[] crop { get; set; }
        public double[] frame { get; set; }
        public string channel { get; set; }
        public double fadeLeft { get; set; }
    }
    internal sealed class PuppetModel
    {
        public readonly string Name;
        public readonly List<PuppetPiece> Pieces = new List<PuppetPiece>();
        public readonly string Path;
        private PuppetModel(string path, string name) { Path = path; Name = name; }

        public static PuppetModel Load(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new InvalidDataException("找不到 model.json。请完整保留模型文件夹。");
            if (new FileInfo(path).Length > 131072) throw new InvalidDataException("模型配置过大。");
            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            PuppetDefinition data;
            try { data = new JavaScriptSerializer().Deserialize<PuppetDefinition>(File.ReadAllText(path)); }
            catch (Exception) { throw new InvalidDataException("模型配置不是有效 JSON。"); }
            if (data == null || data.format != "whale-layered-1" || data.layers == null || data.layers.Count < 3 || data.layers.Count > 48)
                throw new InvalidDataException("需要 whale-layered-1 格式的模型，且部件数为 3–48。Cubism 的 model3.json 不能直接作为此格式加载。");
            var model = new PuppetModel(System.IO.Path.GetFullPath(path), String.IsNullOrWhiteSpace(data.name) ? "未命名模型" : data.name.Substring(0, Math.Min(40, data.name.Length)));
            var bitmaps = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PuppetLayer part in data.layers)
            {
                if (part == null || String.IsNullOrWhiteSpace(part.id) || !ids.Add(part.id) ||
                    String.IsNullOrWhiteSpace(part.image) || System.IO.Path.GetFileName(part.image) != part.image ||
                    part.image.Contains("..") || part.crop == null || part.crop.Length != 4 ||
                    part.frame == null || part.frame.Length != 4 || !IsChannel(part.channel))
                    throw new InvalidDataException("模型部件配置有误或包含不安全的文件名。");
                string full = System.IO.Path.Combine(directory, part.image);
                if (!bitmaps.ContainsKey(part.image))
                {
                    if (!File.Exists(full) || new FileInfo(full).Length > 16777216) throw new InvalidDataException("缺少贴图或贴图过大：" + part.image);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(full); bitmap.EndInit(); bitmap.Freeze();
                    if (bitmap.PixelWidth > 4096 || bitmap.PixelHeight > 4096) throw new InvalidDataException("贴图尺寸不得超过 4096 像素。");
                    bitmaps.Add(part.image, bitmap);
                }
                BitmapSource sheet = bitmaps[part.image];
                int[] c = part.crop; double[] f = part.frame;
                if (c[0] < 0 || c[1] < 0 || c[2] <= 0 || c[3] <= 0 || c[0]+c[2] > sheet.PixelWidth || c[1]+c[3] > sheet.PixelHeight ||
                    f.Any(v => Double.IsNaN(v) || Double.IsInfinity(v)) || Double.IsNaN(part.fadeLeft) || part.fadeLeft<0 || part.fadeLeft>.5 || f[2] <= 0 || f[3] <= 0 || f[2] > 300 || f[3] > 300 ||
                    f[0] < -100 || f[1] < -100 || f[0]+f[2] > 400 || f[1]+f[3] > 400)
                    throw new InvalidDataException("模型部件裁切或放置范围不正确：" + part.id);
                var cropped = new CroppedBitmap(sheet, new Int32Rect(c[0],c[1],c[2],c[3])); cropped.Freeze();
                model.Pieces.Add(new PuppetPiece(part.channel, cropped, f, part.fadeLeft));
            }
            return model;
        }
        private static bool IsChannel(string value)
        {
            return value == "back-hair" || value == "front-hair" || value == "head" || value == "head-open" || value == "head-closed" ||
                   value == "eye-open" || value == "eye-closed" || value == "torso" ||
                   value == "skirt" || value == "left-arm" || value == "right-arm" ||
                   value == "left-leg" || value == "right-leg";
        }
    }
    internal sealed class PuppetPiece
    {
        public readonly string Channel;
        public readonly BitmapSource Bitmap;
        public readonly double[] Frame;
        public readonly double FadeLeft;
        public PuppetPiece(string channel, BitmapSource bitmap, double[] frame,double fadeLeft) { Channel=channel; Bitmap=bitmap; Frame=frame;FadeLeft=fadeLeft; }
    }
    internal sealed class PuppetVisual : Canvas
    {
        private sealed class MotionPart
        {
            public string Channel;
            public Image Image;
            public RotateTransform Turn;
            public TranslateTransform Move;
            public ScaleTransform Scale;
        }
        private readonly List<MotionPart> parts = new List<MotionPart>();
        public PuppetVisual(PuppetModel model)
        {
            Width=300; Height=300; IsHitTestVisible=false;
            foreach (var piece in model.Pieces)
            {
                double[] f=piece.Frame;
                var image=new Image { Source=piece.Bitmap, Width=f[2], Height=f[3], Stretch=Stretch.Fill, IsHitTestVisible=false };
                if(piece.FadeLeft>0) image.OpacityMask=new LinearGradientBrush(
                    new GradientStopCollection {new GradientStop(Colors.Transparent,0),new GradientStop(Colors.White,piece.FadeLeft),new GradientStop(Colors.White,1)},
                    new Point(0,0),new Point(1,0));
                SetLeft(image,f[0]); SetTop(image,f[1]);
                double px=150,py=105;
                if (piece.Channel=="left-arm") {px=111;py=164;}
                else if (piece.Channel=="right-arm") {px=194;py=164;}
                else if (piece.Channel=="left-leg") {px=135;py=225;}
                else if (piece.Channel=="right-leg") {px=182;py=225;}
                else if (piece.Channel=="torso" || piece.Channel=="skirt") {px=150;py=188;}
                image.RenderTransformOrigin=new Point((px-f[0])/f[2],(py-f[1])/f[3]);
                var item=new MotionPart {Channel=piece.Channel,Image=image,Turn=new RotateTransform(),Move=new TranslateTransform(),Scale=new ScaleTransform(1,1)};
                var transforms=new TransformGroup(); transforms.Children.Add(item.Scale); transforms.Children.Add(item.Turn); transforms.Children.Add(item.Move);
                image.RenderTransform=transforms;
                RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
                Children.Add(image); parts.Add(item);
            }
        }
        public void Apply(PetMotion motion)
        {
            foreach (var part in parts)
            {
                double head=motion.HeadAngle;
                part.Turn.Angle=part.Channel=="back-hair" ? head*.48+motion.Hair*.65 :
                    part.Channel=="front-hair" ? head*.8+motion.Hair*.22 :
                    part.Channel=="left-arm" ? -motion.Limb*.9 :
                    part.Channel=="right-arm" ? motion.Limb*.72 :
                    part.Channel=="left-leg" ? -motion.Limb*.15 :
                    part.Channel=="right-leg" ? motion.Limb*.15 :
                    part.Channel=="skirt" ? motion.Limb*.15 :
                    part.Channel=="torso" ? motion.Angle*.25 : head;
                part.Move.X=part.Channel=="back-hair" ? motion.Hair*.42 : 0;
                part.Move.Y=part.Channel=="head" || part.Channel=="head-open" || part.Channel=="head-closed" || part.Channel=="eye-open" || part.Channel=="eye-closed" || part.Channel=="front-hair"
                    ? motion.HeadY*.6 : part.Channel=="torso" ? -motion.Breath*.4 : 0;
                part.Scale.ScaleY=part.Channel=="torso" ? 1+motion.Breath*.002 : 1;
                double blink=Math.Max(0,Math.Min(1,motion.Blink));
                if (part.Channel=="head-open") part.Image.Opacity=1-blink;
                if (part.Channel=="head-closed") part.Image.Opacity=blink;
                if (part.Channel=="eye-open") {part.Image.Opacity=1-blink;part.Scale.ScaleY=Math.Max(.08,1-blink*.82);}
                if (part.Channel=="eye-closed") part.Image.Opacity=blink;
            }
        }
    }
}
