using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DeepSeekBalanceViewer;

namespace DeepSeekWhaleWpf
{
    internal sealed class BalanceDrop
    {
        public string Currency;
        public decimal Amount;
        public string FloatingText { get { return "−" + Amount.ToString(Amount<.00000001m?"0.##E+0":"0.00########", CultureInfo.InvariantCulture) +
            (String.Equals(Currency,"CNY",StringComparison.OrdinalIgnoreCase) ? "" : " " + Currency); } }
        public static List<BalanceDrop> Between(BalanceResult before, BalanceResult after)
        {
            var list = new List<BalanceDrop>();
            if(before==null || after==null || before.Items==null || after.Items==null) return list;
            foreach(var current in after.Items) foreach(var old in before.Items)
            {
                if(!String.Equals(current.Currency,old.Currency,StringComparison.OrdinalIgnoreCase)) continue;
                decimal a,b;
                if(Decimal.TryParse(old.Total,NumberStyles.Number,CultureInfo.InvariantCulture,out a) &&
                   Decimal.TryParse(current.Total,NumberStyles.Number,CultureInfo.InvariantCulture,out b) && a>b)
                    list.Add(new BalanceDrop { Currency=current.Currency,Amount=a-b });
                break;
            }
            return list;
        }
    }

    internal struct PetMotion
    {
        public double X,Y,Angle,ScaleX,ScaleY,HeadAngle,HeadY,Hair,Breath,Limb,Blink;
        public bool Closed;
    }
    internal static class PetMotionEngine
    {
        public static double Duration(string pose,string action)
        { return action=="shy"?2.3:action=="annoyed"?1.9:action=="pet"?2.2:action=="charge"?1.35:action=="hop"?1.6:3.2; }
        public static PetMotion Sample(string pose,string action,double elapsed,double clock)
        {
            double breath=Math.Sin(clock*(pose=="prone"?1.2:1.55));
            // Deterministic, irregular intervals; no metronomic double blink every cycle.
            double cycle=Math.Floor(clock/13.7),blink=clock-cycle*13.7;
            double first=2.8+.65*Math.Sin(cycle*2.39),second=8.7+.9*Math.Sin(cycle*1.71);
            double naturalBlink=Math.Max(BlinkCurve(blink,first,.13),BlinkCurve(blink,second,.16));
            var m=new PetMotion { X=0,ScaleX=.92,ScaleY=.92,
                Breath=.9*breath,HeadY=-.35*breath,
                HeadAngle=.45*Math.Sin(clock*.73)+.18*Math.Sin(clock*1.13),
                Hair=.7*Math.Sin(clock*.73-.6)+.25*Math.Sin(clock*1.31),
                Limb=(pose=="prone"?1.2:.35)*Math.Sin(clock*.95-.8),
                Blink=naturalBlink,Closed=(blink>first&&blink<first+.13)||(blink>second&&blink<second+.16) };
            if(action=="sleep") {m.Closed=true;m.Blink=1;m.HeadAngle=-1.4;m.Breath=.8*Math.Sin(clock*1.1);return m;}
            if(String.IsNullOrEmpty(action)) return m;
            double t=Math.Max(0,Math.Min(1,elapsed/Duration(pose,action)));
            double envelope=Math.Pow(Math.Sin(Math.PI*t),2);
            if(t>=1) return m;
            if(action=="shy") { m.HeadAngle-=3.2*envelope;m.HeadY+=2.2*envelope;m.Angle=-.65*envelope;m.Closed=t>.26&&t<.70;m.Blink=Math.Max(m.Blink,SoftWindow(t,.26,.70,.07)); }
            else if(action=="annoyed") {m.HeadAngle+=2.7*Math.Sin(t*Math.PI*4)*envelope;m.Angle=.35*Math.Sin(t*Math.PI*4)*envelope;}
            else if(action=="pet") {m.HeadY+=1.6*envelope;m.HeadAngle-=2.5*envelope;m.Closed=t>.16&&t<.78;m.Blink=Math.Max(m.Blink,SoftWindow(t,.16,.78,.07));m.Limb+=1.8*envelope;}
            else if(action=="charge") {m.HeadY-=2.4*envelope;m.HeadAngle+=1.5*envelope;m.Limb-=2.5*envelope;}
            else if(action=="hop" && pose!="prone") {Jump(ref m,t,pose=="stand"?12:5);m.HeadY+=1.8*Math.Sin(t*Math.PI*3)*envelope;}
            else if(action=="tilt") {m.HeadAngle+=(pose=="prone"?2.2:-3)*envelope;m.Angle=-.4*envelope;}
            else {m.HeadY-=1.8*envelope;m.Limb+=(pose=="prone"?4:1.6)*Math.Sin(t*Math.PI*2)*envelope;m.Breath+=1.2*envelope;}
            m.Hair+=1.4*Math.Sin(t*Math.PI*2-.5)*envelope;
            return m;
        }
        private static double BlinkCurve(double clock,double start,double duration)
        {
            double t=(clock-start)/duration;
            return t<=0||t>=1?0:Math.Sin(Math.PI*t);
        }
        private static double SoftWindow(double t,double start,double end,double edge)
        {
            double a=Math.Max(0,Math.Min(1,(t-start)/edge));
            double b=Math.Max(0,Math.Min(1,(end-t)/edge));
            return Math.Min(a*a*(3-2*a),b*b*(3-2*b));
        }
        private static void Jump(ref PetMotion m,double t,double height)
        {
            // Feet stay anchored during compression; constant downward acceleration
            // gives the airborne section a parabolic trajectory. Landing is damped.
            if(t<.18)
            {
                double k=Math.Pow(Math.Sin(Math.PI*t/.18),2);
                m.ScaleY*=1-.022*k; m.ScaleX*=1+.012*k;
            }
            else if(t<.73)
            {
                double u=(t-.18)/.55;
                m.Y=-4*height*u*(1-u);
                double stretch=Math.Sin(Math.PI*u);
                m.ScaleY*=1+.009*stretch; m.ScaleX*=1-.006*stretch;
            }
            else
            {
                double u=(t-.73)/.27;
                double compression=Math.Sin(u*Math.PI*2)*Math.Exp(-4*u)*(1-u);
                m.ScaleY*=1-.028*compression; m.ScaleX*=1+.014*compression;
            }
        }
    }

    // Vector glyph outlines stay legible over both light and dark desktop backgrounds.
    internal sealed class FloatingAmount : FrameworkElement
    {
        private readonly Geometry outline;
        public FloatingAmount(string text,double size)
        {
            var formatted=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Microsoft YaHei UI"),FontStyles.Normal,FontWeights.Bold,FontStretches.Normal),size,Brushes.Coral,1.0);
            outline=formatted.BuildGeometry(new Point(4,4));
            outline.Freeze(); Width=formatted.Width+10; Height=formatted.Height+10;
            IsHitTestVisible=false;
        }
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawGeometry(null,new Pen(Brushes.White,3),outline);
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(238,103,88)),null,outline);
        }
    }
    internal sealed class ChargeParticle
    {
        public FloatingAmount Visual;
        public double Start;
        public int Lane;
    }
}
