using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace DeepSeekWhaleWpf
{
    // One continuous mesh: no cut-out seams, and the contact points stay planted.
    internal sealed class PetSprite : Decorator
    {
        const int Segments = 24;
        readonly MeshGeometry3D mesh = new MeshGeometry3D();
        readonly DrawingBrush texture;
        readonly ImageDrawing currentFrame=new ImageDrawing {Rect=new Rect(0,0,300,300)};
        readonly ImageDrawing previousFrame=new ImageDrawing {Rect=new Rect(0,0,300,300)};
        readonly DrawingGroup previousLayer=new DrawingGroup();
        double frameBlend=1;
        BitmapSource source;
        public Stretch Stretch { get; set; }
        public BitmapSource Source { get { return source; } set {
            if(source==value)return;
            previousFrame.ImageSource=source;previousLayer.Opacity=source==null?0:1;frameBlend=source==null?1:0;
            source=value;currentFrame.ImageSource=value;
            if(value==null){previousFrame.ImageSource=null;previousLayer.Opacity=0;frameBlend=1;}
        } }
        public PetSprite()
        {
            var drawing=new DrawingGroup();drawing.Children.Add(currentFrame);previousLayer.Children.Add(previousFrame);drawing.Children.Add(previousLayer);
            texture=new DrawingBrush(drawing) {Stretch=Stretch.Fill,ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,300,300)};
            var viewport = new Viewport3D { ClipToBounds=false, IsHitTestVisible=false };
            viewport.Camera = new OrthographicCamera(new Point3D(150,150,10),new Vector3D(0,0,-1),new Vector3D(0,1,0),300);
            for(int y=0;y<=Segments;y++) for(int x=0;x<=Segments;x++) {
                mesh.Positions.Add(new Point3D(300.0*x/Segments,300-300.0*y/Segments,0));
                mesh.TextureCoordinates.Add(new Point((double)x/Segments,(double)y/Segments));
            }
            for(int y=0;y<Segments;y++) for(int x=0;x<Segments;x++) {
                int a=y*(Segments+1)+x,b=a+Segments+1;
                mesh.TriangleIndices.Add(a);mesh.TriangleIndices.Add(b);mesh.TriangleIndices.Add(a+1);
                mesh.TriangleIndices.Add(a+1);mesh.TriangleIndices.Add(b);mesh.TriangleIndices.Add(b+1);
            }
            // Diffuse + uniform ambient preserves PNG alpha on a layered window;
            // emissive-only material writes RGB with zero alpha in WPF.
            var material=new DiffuseMaterial(texture);
            viewport.Children.Add(new ModelVisual3D {Content=new AmbientLight(Colors.White)});
            viewport.Children.Add(new ModelVisual3D { Content=new GeometryModel3D(mesh,material) });
            Child=viewport;
        }
        protected override HitTestResult HitTestCore(PointHitTestParameters hit)
        { return new PointHitTestResult(this,hit.HitPoint); }
        internal static Point Deform(double x,double y,string pose,PetMotion m)
        {
            bool prone=pose=="prone";
            double px=prone?.32:.5,py=prone?.67:.51;
            double head=1-Smooth(prone?.64:.45,prone?.83:.68,y);
            if(prone) head*=1-Smooth(.47,.72,x);
            double theta=m.HeadAngle*Math.PI/180*head;
            double dx=x-px,dy=y-py;
            double xx=px+dx*Math.Cos(theta)-dy*Math.Sin(theta);
            double yy=py+dx*Math.Sin(theta)+dy*Math.Cos(theta);
            yy+=m.HeadY/300*head;
            // Hair tips lag behind the head; soft masks avoid visible joints.
            double side=Smooth(.14,.32,Math.Abs(x-(prone?.34:.5)));
            double hair=side*Smooth(.18,.38,y)*(1-Smooth(.70,.91,y));
            xx+=m.Hair/300*hair;
            double chest=Smooth(.42,.61,y)*(1-Smooth(.77,.95,y));
            yy-=m.Breath/300*chest*(prone?.35:1);
            double limb=prone?Smooth(.58,.85,x)*Smooth(.35,.52,y)*(1-Smooth(.69,.85,y)):
                Smooth(.20,.37,Math.Abs(x-.5))*Smooth(.43,.57,y)*(1-Smooth(.75,.86,y));
            yy+=m.Limb/300*limb;
            return new Point(xx*300,yy*300);
        }
        static double Smooth(double a,double b,double x) { x=Math.Max(0,Math.Min(1,(x-a)/(b-a)));return x*x*(3-2*x); }
        public void BlendFrame(double dt)
        {
            if(frameBlend>=1)return;
            frameBlend=Math.Min(1,frameBlend+Math.Max(0,dt)/.055);
            previousLayer.Opacity=1-frameBlend*frameBlend*(3-2*frameBlend);
            if(frameBlend>=1)previousFrame.ImageSource=null;
        }
        public void Apply(string pose,PetMotion motion)
        {
            // Detach while editing to avoid a render invalidation for every vertex.
            var points=mesh.Positions; mesh.Positions=null;
            for(int y=0;y<=Segments;y++)for(int x=0;x<=Segments;x++) {
                Point p=Deform((double)x/Segments,(double)y/Segments,pose,motion);
                points[y*(Segments+1)+x]=new Point3D(p.X,300-p.Y,0);
            }
            mesh.Positions=points;
        }
    }

    internal sealed class SoftValue
    {
        public double Value,Velocity;
        public SoftValue(double initial) { Value=initial; }
        public double Step(double target,double dt,double speed)
        {
            dt=Math.Max(0,Math.Min(.05,dt));
            double delta=Value-target,term=(Velocity+speed*delta)*dt,decay=Math.Exp(-speed*dt);
            Value=target+(delta+term)*decay;
            Velocity=(Velocity-speed*term)*decay;
            return Value;
        }
    }
}
