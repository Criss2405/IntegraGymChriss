namespace IntegraGym;

// Apply the shared palette after each form has constructed its controls.
internal class StudioForm : Form
{
    protected override void OnLoad(EventArgs e){StudioSkin.Apply(this);base.OnLoad(e);}
}
internal static class StudioSkin
{
    static readonly Color Input=Color.FromArgb(33,51,78), Ink=Color.FromArgb(231,240,255);
    private static void Round(Control control, int radius)
    {
        void UpdateRegion()
        {
            if (control.Width < 2 || control.Height < 2) return;
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            int size = Math.Min(control.LogicalToDeviceUnits(radius), Math.Min(control.Width, control.Height) / 2);
            path.AddRoundedRectangle(new Rectangle(Point.Empty, control.Size), new Size(size, size));
            var previous = control.Region;
            control.Region = new Region(path);
            previous?.Dispose();
        }
        control.SizeChanged += (_, _) => UpdateRegion();
        UpdateRegion();
    }
    public static void Apply(Control c)
    {
        var previous = c.Font;
        c.Font = DashboardLook.Face(Math.Max(10, previous.SizeInPoints), previous.Bold ? 2 : c is Button ? 1 : 0);
        switch(c){
        case Form: c.BackColor=DashboardLook.Canvas;c.ForeColor=Ink;break;
        case DataGridView g: GymTableStyle.Apply(g);Round(g,18);return;
        case Button b:
            bool primary=b.BackColor.ToArgb()==Theme.Accent.ToArgb();
            b.FlatStyle=FlatStyle.Flat;b.UseVisualStyleBackColor=false;b.ForeColor=Ink;b.BackColor=primary?Color.FromArgb(13,75,119):Input;
            b.FlatAppearance.BorderColor=primary?Theme.Accent:Night.Line;b.Cursor=Cursors.Hand;
            b.FlatAppearance.BorderSize=0;Round(b,10);
            var idle=b.BackColor;var hot=Night.Mix(idle,Night.Blue,.22f);
            var timer=new System.Windows.Forms.Timer{Interval=16};float t=0;bool hover=false;
            b.MouseEnter+=(_,_)=>{hover=true;timer.Start();};b.MouseLeave+=(_,_)=>{hover=false;timer.Start();};
            b.FlatAppearance.MouseOverBackColor=idle;b.FlatAppearance.MouseDownBackColor=Night.Selected;
            timer.Tick+=(_,_)=>{t=Math.Clamp(t+(hover?.14f:-.14f),0,1);b.BackColor=b.FlatAppearance.MouseOverBackColor=Night.Mix(idle,hot,t);if(t==0||t==1)timer.Stop();};
            b.Disposed+=(_,_)=>timer.Dispose();return;
        case TextBoxBase or NumericUpDown or ListBox or ComboBox: c.BackColor=Input;c.ForeColor=Ink;if(c is ComboBox cb)cb.FlatStyle=FlatStyle.Flat;Round(c,8);return;
        case DateTimePicker date: date.CalendarMonthBackground=Input;date.CalendarForeColor=Ink;Round(date,8);return;
        case TabControl tabs: tabs.DrawMode=TabDrawMode.OwnerDrawFixed;tabs.DrawItem+=(_,e)=>{if(e.Index<0)return;using var b=new SolidBrush(e.Index==tabs.SelectedIndex?Night.Selected:DashboardLook.Surface);e.Graphics.FillRectangle(b,e.Bounds);TextRenderer.DrawText(e.Graphics,tabs.TabPages[e.Index].Text,tabs.Font,e.Bounds,Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);};break;
        case RoundedPanel rp:rp.SurfaceColor=DashboardLook.Surface;rp.BackColor=DashboardLook.Canvas;break;
        case Panel panel when panel.GetType()==typeof(Panel): Round(panel,16);panel.BackColor=DashboardLook.Surface;panel.ForeColor=Ink;break;
        case Panel or GroupBox: c.BackColor=DashboardLook.Surface;c.ForeColor=Ink;break;
        case Label: c.ForeColor=c.ForeColor==Color.Firebrick||c.ForeColor==Color.Red?Color.LightSalmon:c.Font.Bold?Color.White:DashboardLook.Muted;c.BackColor=Color.Transparent;break;
        case CheckBox or RadioButton:c.BackColor=Color.Transparent;c.ForeColor=Ink;break;
        }
        foreach(Control child in c.Controls)Apply(child);
    }
}


