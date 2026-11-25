using System.Drawing.Drawing2D;
namespace IntegraGym;

public static class Theme
{
 public static readonly Color Background=Color.FromArgb(244,247,251);
 public static readonly Color Sidebar=Color.FromArgb(20,29,46);
 public static readonly Color Selected=Color.FromArgb(35,56,88);
 public static readonly Color Accent=Color.FromArgb(57,97,170);
 public static readonly Color Text=Color.FromArgb(26,39,57);
 public static readonly Color Muted=Color.FromArgb(102,117,136);
 public static readonly Color Border=Color.FromArgb(224,231,238);
}

// Pinta el fondo y borde; los hijos deben mantenerse dentro del Padding.
// No necesita Guna ni elimina los controles nativos accesibles de Windows.
public sealed class RoundedPanel : Panel
{
 private int radius=18;
 [System.ComponentModel.DefaultValue(18)]
 public int CornerRadius {get=>radius;set{radius=Math.Max(0,value);Invalidate();}}
 private Color surface=Color.White;
 [System.ComponentModel.DefaultValue(typeof(Color), "White")]
 public Color SurfaceColor {get=>surface;set{surface=value;Invalidate();}}
 public RoundedPanel()
 {
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|
   ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
  BackColor=Theme.Background; Padding=new Padding(22);
 }
 protected override void OnPaint(PaintEventArgs e)
 {
  base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
  float inset=DeviceDpi/96f, d=Math.Min(CornerRadius*2*inset,Math.Min(Width-2,Height-2));
  if(d<=0)return;
  var bounds=new RectangleF(inset/2,inset/2,Width-inset,Height-inset);
  using var shape=new GraphicsPath();
  shape.AddArc(bounds.Left,bounds.Top,d,d,180,90);
  shape.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);
  shape.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);
  shape.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90); shape.CloseFigure();
  using var fill=new SolidBrush(SurfaceColor);using var pen=new Pen(Theme.Border,inset);
  e.Graphics.FillPath(fill,shape);e.Graphics.DrawPath(pen,shape);
 }
}




